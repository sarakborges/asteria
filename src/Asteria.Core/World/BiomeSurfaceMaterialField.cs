namespace Asteria.Core.World;

/// <summary>
/// Resolves authored surface coatings and three-dimensional solid geology.
/// Terrain height may blend biome influences; material ownership stays with
/// the authoritative primary surface biome.
/// </summary>
public sealed class BiomeSurfaceMaterialField
{
    private readonly ulong _seed;
    private readonly SurfaceTerrainField? _terrain;
    private readonly SurfaceMosaicField? _mosaic;
    private readonly Dictionary<string, MaterialRule> _rules;
    private readonly Dictionary<string, FaceRules> _faceRules;
    private readonly Dictionary<string, ResolvedGeology> _geologyRules;

    public BiomeSurfaceMaterialField(
        ulong seed,
        IEnumerable<BiomeDefinition> biomes,
        BlockRegistry blocks,
        SurfaceTerrainField? terrain = null,
        SurfaceMosaicField? mosaic = null)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(blocks);

        _seed = seed;
        _terrain = terrain;
        _mosaic = mosaic;
        var biomeDefinitions = biomes.ToArray();
        _faceRules = biomeDefinitions
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(
                biome => biome.Id,
                biome => FaceRules.Create(biome, blocks),
                StringComparer.Ordinal);
        _rules = _faceRules.ToDictionary(
            pair => pair.Key, pair => pair.Value.Floor,
            StringComparer.Ordinal);
        _geologyRules = biomeDefinitions
            .Where(biome => biome.Geology is not null)
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(
                biome => biome.Id,
                biome => ResolvedGeology.Create(biome.Id, biome.Geology!, blocks),
                StringComparer.Ordinal);
        HasExteriorOverrides = _faceRules.Values.Any(
            profile => profile.Exterior &&
                (profile.OverrideWalls || profile.OverrideCeiling));
        MaximumCavePaintDepth = _faceRules.Values
            .Where(profile => !profile.Exterior)
            .Select(profile => profile.MaxDepth)
            .DefaultIfEmpty(0u)
            .Max();

        if (_rules.Count == 0)
        {
            throw new ArgumentException(
                "Surface material field requires at least one biome.",
                nameof(biomes));
        }
    }

    /// <summary>
    /// True only if a surface or additive volume biome authors wall/ceiling
    /// overrides. Cave-only wall rules do not trigger exterior scanning.
    /// </summary>
    public bool HasExteriorOverrides { get; }

    /// <summary>
    /// Immutable upper bound on cave-facing layer depth. Lets the chunk
    /// materializer detect exposed faces before resolving biome ownership.
    /// </summary>
    public uint MaximumCavePaintDepth { get; }

    public BlockRuntimeId BlockAt(
        BiomeSample sample,
        int worldX,
        int worldZ,
        uint depth,
        SurfacePlacementContext? placement = null)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (!_rules.TryGetValue(
                sample.Primary,
                out var rule))
        {
            throw new KeyNotFoundException(
                $"Surface biome {sample.Primary} has no material rule.");
        }

        if (_geologyRules.TryGetValue(sample.Primary, out var geology) &&
            _terrain is not null &&
            depth >= geology.SurfaceCoverDepth)
        {
            var surfaceY = _terrain.SurfaceHeight(worldX, worldZ);
            var geologicalY = (int)Math.Max(
                int.MinValue, (long)surfaceY - depth);
            return geology.BlockAt(_seed, worldX, geologicalY, worldZ);
        }

        if (depth == 0 &&
            _mosaic?.Sample(sample, worldX, worldZ) is
                { Block.IsAir: false } choice)
            return choice.Block;

        if (depth >= rule.CoreStartDepth)
        {
            return rule.CoreBlock;
        }

        return rule.BlockAtDepth(
            _seed,
            worldX,
            worldZ,
            depth,
            PlacementFor(rule, worldX, worldZ, placement));
    }

    /// <summary>
    /// Returns the authored surface-cover depth, or null for biomes using only
    /// the legacy surface profile. Beyond this depth, the 3D geology owns solids.
    /// </summary>
    public uint? GeologySurfaceCoverDepth(BiomeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return _geologyRules.TryGetValue(sample.Primary, out var geology)
            ? geology.SurfaceCoverDepth
            : null;
    }

    /// <summary>
    /// Resolves the volumetric geology at a solid world-space voxel. Material
    /// selection uses a continuous 3D field, not altitude-specific layer rules.
    /// </summary>
    public bool TryGeologyBlockAt(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        out BlockRuntimeId block)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_geologyRules.TryGetValue(sample.Primary, out var geology))
        {
            block = BlockRuntimeId.Air;
            return false;
        }

        block = geology.BlockAt(_seed, worldX, worldY, worldZ);
        return true;
    }

    /// <summary>
    /// True when this biome's exposed geological deposits are restricted to
    /// terrain with generated fluid immediately above its surface.
    /// </summary>
    public bool SurfaceGeologyRequiresSubmerged(BiomeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return _geologyRules.TryGetValue(sample.Primary, out var geology) &&
            geology.SurfaceCoverage > 0f &&
            geology.SurfaceSubmergedOnly;
    }

    /// <summary>
    /// Resolves a deterministic world-space deposit for an exposed top voxel.
    /// The caller applies the authored biome cover when this returns false.
    /// </summary>
    public bool TrySurfaceGeologyBlockAt(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        out BlockRuntimeId block)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_geologyRules.TryGetValue(sample.Primary, out var geology) ||
            !geology.TrySurfaceBlockAt(_seed, worldX, worldY, worldZ, out block))
        {
            block = BlockRuntimeId.Air;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Samples the same authored palette across 3D exposed faces, where
    /// depth increases into the solid along the face normal.
    /// </summary>
    public BlockRuntimeId VolumeBlockAt(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        BiomePaletteFace face = BiomePaletteFace.Floor, uint depth = 0)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_faceRules.TryGetValue(sample.Primary, out var rules))
            throw new KeyNotFoundException(
                $"Biome {sample.Primary} has no material palette.");
        if (!rules.HasOverride(face) &&
            _geologyRules.TryGetValue(sample.Primary, out var geology))
            return geology.BlockAt(_seed, worldX, worldY, worldZ);

        var rule = rules.For(face);
        SurfacePlacementContext? placement = null;
        if (rule.HasConditions)
        {
            if (_terrain is null)
            {
                throw new InvalidOperationException(
                    "Conditional volume material patches require a terrain field.");
            }

            var sampled = SurfacePlacementContext.Sample(
                _terrain, worldX, worldZ, rule.RequiresSlope);
            placement = new SurfacePlacementContext(worldY, sampled.Slope);
        }

        return rule.VolumeBlockAt(
            _seed, worldX, worldY, worldZ, depth, placement);
    }

    /// <summary>Maximum relevant inward distance (at least one voxel).</summary>
    public uint MaxVolumePaintDepth(BiomeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_faceRules.TryGetValue(sample.Primary, out var rules))
            throw new KeyNotFoundException(
                $"Biome {sample.Primary} has no material palette.");
        return rules.MaxDepth;
    }

    public bool HasDirectionalOverride(
        BiomeSample sample, BiomePaletteFace face) =>
        _faceRules.TryGetValue(sample.Primary, out var rules) &&
        rules.HasOverride(face);

    public BiomeSurfaceMaterialColumn SampleColumn(
        BiomeSample sample,
        int worldX,
        int worldZ,
        SurfacePlacementContext? placement = null)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (!_rules.TryGetValue(sample.Primary, out var rule))
        {
            throw new KeyNotFoundException(
                $"Surface biome {sample.Primary} has no material rule.");
        }

        var mosaic = _mosaic?.Sample(sample, worldX, worldZ) ?? default;
        return rule.SampleColumn(
            _seed,
            worldX,
            worldZ,
            PlacementFor(rule, worldX, worldZ, placement),
            mosaic.Block);
    }

    /// <summary>
    /// Immutable deepest layer; queries below its start depth cannot be
    /// affected by shallow patches or conditional terrain sampling.
    /// </summary>
    public (BlockRuntimeId Block, uint StartDepth) CoreLayer(
        BiomeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_rules.TryGetValue(sample.Primary, out var rule))
        {
            throw new KeyNotFoundException(
                $"Surface biome {sample.Primary} has no material rule.");
        }

        return (rule.CoreBlock, rule.CoreStartDepth);
    }

    private SurfacePlacementContext? PlacementFor(
        MaterialRule rule,
        int worldX,
        int worldZ,
        SurfacePlacementContext? supplied)
    {
        if (supplied.HasValue)
        {
            return supplied;
        }

        if (!rule.HasConditions)
        {
            return null;
        }

        if (_terrain is null)
        {
            throw new InvalidOperationException(
                "Conditional material patches require a terrain field.");
        }

        return SurfacePlacementContext.Sample(
            _terrain, worldX, worldZ,
            rule.RequiresSlope, rule.UsesBaseSurface);
    }

    private sealed record FaceRules(
        MaterialRule Floor,
        MaterialRule Walls,
        MaterialRule Ceiling,
        bool OverrideFloor,
        bool OverrideWalls,
        bool OverrideCeiling,
        bool Exterior)
    {
        public uint MaxDepth =>
            Math.Max(1u, Math.Max(Floor.CoreStartDepth,
                Math.Max(Walls.CoreStartDepth, Ceiling.CoreStartDepth)));

        public MaterialRule For(BiomePaletteFace face) => face switch
        {
            BiomePaletteFace.Floor => Floor,
            BiomePaletteFace.Walls => Walls,
            BiomePaletteFace.Ceiling => Ceiling,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };

        public bool HasOverride(BiomePaletteFace face) => face switch
        {
            BiomePaletteFace.Floor => OverrideFloor,
            BiomePaletteFace.Walls => OverrideWalls,
            BiomePaletteFace.Ceiling => OverrideCeiling,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };

        public static FaceRules Create(BiomeDefinition biome, BlockRegistry blocks)
        {
            var palette = biome.Palette;
            var defaults = MaterialRule.Create(biome, blocks, palette.Default, null);
            var floor = palette.Floor is null
                ? defaults : MaterialRule.Create(biome, blocks, palette.Floor, "floor");
            var walls = palette.Walls is null
                ? defaults : MaterialRule.Create(biome, blocks, palette.Walls, "walls");
            var ceiling = palette.Ceiling is null
                ? defaults : MaterialRule.Create(biome, blocks, palette.Ceiling, "ceiling");
            return new FaceRules(floor, walls, ceiling,
                palette.Floor is not null, palette.Walls is not null,
                palette.Ceiling is not null,
                biome.SurfaceLayout is not null ||
                biome.VolumeLayout?.Placement == VolumeBiomePlacement.Additive);
        }
    }

    private sealed class ResolvedGeology
    {
        private readonly record struct SpatialPosition(double X, double Y, double Z);

        private readonly BiomeGeologyDefinition _definition;
        private readonly BlockRuntimeId _baseBlock;
        private readonly BlockRuntimeId[] _blocks;
        private readonly GenerationDomain _formationDomain;
        private readonly GenerationDomain _detailDomain;
        private readonly GenerationDomain _surfaceFormationDomain;
        private readonly GenerationDomain _surfaceDetailDomain;
        private readonly GenerationDomain[] _warpDomains;
        private readonly GenerationDomain[] _materialDomains;
        private readonly GenerationDomain[] _materialDetailDomains;
        private readonly double[] _materialBiases;

        private ResolvedGeology(
            BiomeGeologyDefinition definition,
            BlockRuntimeId baseBlock,
            BlockRuntimeId[] blocks,
            string biomeId)
        {
            _definition = definition;
            _baseBlock = baseBlock;
            _blocks = blocks;
            _formationDomain = GenerationDomain.Named(
                $"material/geology/formation/v3/{biomeId}");
            _detailDomain = GenerationDomain.Named(
                $"material/geology/detail/v3/{biomeId}");
            _surfaceFormationDomain = GenerationDomain.Named(
                $"material/geology/surface-formation/v1/{biomeId}");
            _surfaceDetailDomain = GenerationDomain.Named(
                $"material/geology/surface-detail/v1/{biomeId}");
            _warpDomains = new[] { "x", "y", "z" }
                .Select(axis => GenerationDomain.Named(
                    $"material/geology/domain-warp/v3/{biomeId}/{axis}"))
                .ToArray();
            _materialDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/material-field/v4/{biomeId}/{block}"))
                .ToArray();
            _materialDetailDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/material-detail/v2/{biomeId}/{block}"))
                .ToArray();
            _materialBiases = definition.BlockWeights
                .Select(weight => 0.2d * Math.Log(weight))
                .ToArray();
        }

        public uint SurfaceCoverDepth => _definition.SurfaceCoverDepth;
        public float SurfaceCoverage => _definition.SurfaceCoverage;
        public bool SurfaceSubmergedOnly => _definition.SurfaceSubmergedOnly;

        public BlockRuntimeId BlockAt(ulong seed, int x, int y, int z)
        {
            var warped = WarpCoordinates(seed, x, y, z);
            var formation = FormationAt(
                seed, warped, _formationDomain, _detailDomain);
            if (formation < 1d - _definition.Coverage * 2d)
                return _baseBlock;

            return SelectMaterial(seed, warped);
        }

        public bool TrySurfaceBlockAt(
            ulong seed, int x, int y, int z, out BlockRuntimeId block)
        {
            if (_definition.SurfaceCoverage <= 0f)
            {
                block = BlockRuntimeId.Air;
                return false;
            }

            var warped = WarpCoordinates(seed, x, y, z);
            var depositFormation = FormationAt(
                seed, warped, _formationDomain, _detailDomain);
            if (depositFormation < 1d - _definition.Coverage * 2d)
            {
                block = BlockRuntimeId.Air;
                return false;
            }

            // Surface exposure reveals a material already present in the
            // continuous 3D deposit field; the exposure field never invents
            // a disconnected surface-only patch.
            var surfaceFormation = FormationAt(
                seed, warped, _surfaceFormationDomain, _surfaceDetailDomain);
            if (surfaceFormation < 1d - _definition.SurfaceCoverage * 2d)
            {
                block = BlockRuntimeId.Air;
                return false;
            }

            block = SelectMaterial(seed, warped);
            return true;
        }

        private static SpatialPosition RotateCoordinates(int x, int y, int z)
        {
            // Two fixed orthogonal rotations make the noise lattice oblique to
            // world axes. This avoids axis-aligned value-noise cells extending
            // as persistent columns/sheets in the rendered voxel field.
            const double cosineY = 0.8660254037844386d; // cos(30°)
            const double sineY = 0.5d;
            const double cosineX = 0.9396926207859084d; // cos(20°)
            const double sineX = 0.3420201433256687d;   // sin(20°)

            return new SpatialPosition(
                cosineY * x + sineY * sineX * y + sineY * cosineX * z,
                cosineX * y - sineX * z,
                -sineY * x + cosineY * sineX * y + cosineY * cosineX * z);
        }

        private SpatialPosition WarpCoordinates(
            ulong seed, int x, int y, int z)
        {
            var rotated = RotateCoordinates(x, y, z);
            var warpHorizontalScale = Math.Max(8u, _definition.Scale / 2u);
            var warpVerticalScale = Math.Max(8u, _definition.VerticalScale / 2u);
            var horizontalStrength = Math.Max(8d, _definition.Scale * 0.35d);
            var verticalStrength = Math.Max(6d, _definition.VerticalScale * 0.45d);

            var warpX = WorldGenerationEntropy.ValueNoise3D(
                seed, _warpDomains[0], rotated.X, rotated.Y, rotated.Z,
                warpHorizontalScale, warpVerticalScale);
            var warpY = WorldGenerationEntropy.ValueNoise3D(
                seed, _warpDomains[1], rotated.X, rotated.Y, rotated.Z,
                warpHorizontalScale, warpVerticalScale);
            var warpZ = WorldGenerationEntropy.ValueNoise3D(
                seed, _warpDomains[2], rotated.X, rotated.Y, rotated.Z,
                warpHorizontalScale, warpVerticalScale);

            // Keep transformed coordinates continuous. Rounding each warped
            // axis to a voxel reintroduced plateaus that resembled columns.
            return new SpatialPosition(
                rotated.X + warpX * horizontalStrength,
                rotated.Y + warpY * verticalStrength,
                rotated.Z + warpZ * horizontalStrength);
        }

        private double FormationAt(
            ulong seed,
            SpatialPosition position,
            GenerationDomain formationDomain,
            GenerationDomain detailDomain)
        {
            var macro = WorldGenerationEntropy.ValueNoise3D(
                seed, formationDomain,
                position.X, position.Y, position.Z,
                _definition.Scale, _definition.VerticalScale);
            var detail = WorldGenerationEntropy.ValueNoise3D(
                seed, detailDomain,
                position.X, position.Y, position.Z,
                _definition.DetailScale,
                Math.Max(6u, _definition.VerticalScale / 4u));

            // Formation presence also needs genuine 3D breakup. The former
            // roughness-weighted detail was too weak and used half the vertical
            // wavelength, letting the host-rock mask persist as broad sheets.
            var detailInfluence = 0.55d + _definition.Roughness * 0.35d;
            return macro * (1d - detailInfluence) + detail * detailInfluence;
        }

        private BlockRuntimeId SelectMaterial(ulong seed, SpatialPosition position)
        {
            if (_blocks.Length == 1)
                return _blocks[0];

            var detailScale = Math.Max(6u, _definition.SelectionScale / 6u);
            var detailVerticalScale = Math.Max(6u, _definition.VerticalScale / 6u);
            var bestScore = double.NegativeInfinity;
            var bestIndex = 0;
            for (var index = 0; index < _blocks.Length; index++)
            {
                var macro = WorldGenerationEntropy.ValueNoise3D(
                    seed, _materialDomains[index],
                    position.X, position.Y, position.Z,
                    _definition.SelectionScale, _definition.VerticalScale);
                var detail = WorldGenerationEntropy.ValueNoise3D(
                    seed, _materialDetailDomains[index],
                    position.X, position.Y, position.Z,
                    detailScale, detailVerticalScale);
                // Fine independent fields dominate local boundary shape while
                // broad fields retain coherent deposits rather than voxel noise.
                // A shorter vertical wavelength specifically prevents long
                // material columns through several adjacent chunks.
                var field = macro * 0.18d + detail * 0.82d;
                var score = field + _materialBiases[index];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            return _blocks[bestIndex];
        }

        public static ResolvedGeology Create(
            string biomeId,
            BiomeGeologyDefinition definition,
            BlockRegistry blocks) =>
            new ResolvedGeology(
                definition,
                blocks.GetId(definition.BaseBlock),
                definition.Blocks.Select(blocks.GetId).ToArray(),
                biomeId);
    }

    private sealed class MaterialRule
    {
        // Broad X/Z variation keeps stratum boundaries irregular without
        // introducing a second, high-frequency signal that reads as banding.
        private const uint BoundaryNoiseScale = 48;
        private MaterialRule(
            ResolvedLayer[] layers,
            GenerationDomain[] boundaryDomains,
            bool usesBaseSurface)
        {
            Layers = layers;
            BoundaryDomains = boundaryDomains;
            UsesBaseSurface = usesBaseSurface;
            HasConditions = layers.Any(layer => layer.HasConditions);
            RequiresSlope = layers.Any(layer => layer.RequiresSlope);
            CoreBlock = layers[^1].Block;
            var nominalCoreDepth = layers.Length > 1
                ? layers[^2].EndDepthExclusive!.Value
                : 0u;
            CoreStartDepth = checked(
                nominalCoreDepth +
                (layers.Length > 1 ? layers[^2].BoundaryNoise : 0u));
        }

        private ResolvedLayer[] Layers { get; }
        private GenerationDomain[] BoundaryDomains { get; }

        public bool HasConditions { get; }

        public bool RequiresSlope { get; }

        public bool UsesBaseSurface { get; }
        public BlockRuntimeId CoreBlock { get; }
        public uint CoreStartDepth { get; }

        public BlockRuntimeId BlockAtDepth(
            ulong seed,
            int worldX,
            int worldZ,
            uint depth,
            SurfacePlacementContext? placement)
        {
            var previousEndDepth = 0u;
            for (var index = 0; index < Layers.Length; index++)
            {
                var layer = Layers[index];
                if (layer.EndDepthExclusive is { } nominalEndDepth)
                {
                    var endDepth = AdjustedEndDepth(
                        seed, worldX, worldZ, index,
                        nominalEndDepth, previousEndDepth);
                    previousEndDepth = endDepth;
                    if (depth >= endDepth)
                        continue;
                }

                return layer.Resolve(
                    seed, worldX, worldZ, placement);
            }

            throw new InvalidOperationException(
                "Validated surface material profile must end with a core layer.");
        }

        public BlockRuntimeId VolumeBlockAt(
            ulong seed, int x, int y, int z, uint depth,
            SurfacePlacementContext? placement)
        {
            if (depth >= CoreStartDepth)
                return CoreBlock;

            var previousEndDepth = 0u;
            for (var index = 0; index < Layers.Length; index++)
            {
                var layer = Layers[index];
                if (layer.EndDepthExclusive is { } nominalEndDepth)
                {
                    var endDepth = AdjustedEndDepth(
                        seed, x, z, index, nominalEndDepth, previousEndDepth);
                    previousEndDepth = endDepth;
                    if (depth >= endDepth)
                        continue;
                }
                return layer.ResolveVolume(seed, x, y, z, placement);
            }
            throw new InvalidOperationException(
                "Validated palette must end with a core layer.");
        }

        public BiomeSurfaceMaterialColumn SampleColumn(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement,
            BlockRuntimeId surfaceOverride)
        {
            var depths = new uint[Layers.Length];
            var blocks = new BlockRuntimeId[Layers.Length];

            var previousEndDepth = 0u;
            for (var index = 0; index < Layers.Length; index++)
            {
                var layer = Layers[index];
                depths[index] = layer.EndDepthExclusive is { } nominalEndDepth
                    ? AdjustedEndDepth(
                        seed, worldX, worldZ, index,
                        nominalEndDepth, previousEndDepth)
                    : uint.MaxValue;
                if (depths[index] != uint.MaxValue)
                    previousEndDepth = depths[index];
                blocks[index] = layer.Resolve(
                    seed, worldX, worldZ, placement);
            }

            return new BiomeSurfaceMaterialColumn(
                depths, blocks, surfaceOverride);
        }

        private uint AdjustedEndDepth(
            ulong seed,
            int worldX,
            int worldZ,
            int layerIndex,
            uint nominalEndDepth,
            uint previousEndDepth)
        {
            var amplitude = Layers[layerIndex].BoundaryNoise;
            var displacement = 0;
            if (amplitude > 0)
            {
                var broadNoise = WorldGenerationEntropy.ValueNoise2D(
                    seed, BoundaryDomains[layerIndex], worldX, worldZ,
                    BoundaryNoiseScale);
                displacement = (int)Math.Round(
                    broadNoise * amplitude,
                    MidpointRounding.AwayFromZero);
            }
            var displaced = Math.Max(
                (long)nominalEndDepth + displacement,
                (long)previousEndDepth + 1L);
            return checked((uint)displaced);
        }

        public static MaterialRule Create(
            BiomeDefinition biome,
            BlockRegistry blocks,
            IReadOnlyList<BiomeSurfaceLayerDefinition> profile,
            string? face)
        {
            var finiteDepth = 0u;
            var layers =
                new ResolvedLayer[
                    profile.Count];

            for (var index = 0;
                 index < layers.Length;
                 index++)
            {
                var definition =
                    profile[index];
                uint? endDepth = null;

                if (definition.Depth is
                    { } depth)
                {
                    finiteDepth =
                        checked(
                            finiteDepth +
                            depth);
                    endDepth =
                        finiteDepth;
                }

                layers[index] =
                    new ResolvedLayer(
                        blocks.GetId(
                            definition.Block),
                        endDepth,
                        definition.BoundaryNoise,
                        definition.Patch is
                            { } patch
                            ? ResolvedPatch.Create(
                                face is null ? biome.Id : $"{biome.Id}/{face}",
                                index,
                                patch,
                                blocks)
                            : null);
            }

            var boundaryDomains = new GenerationDomain[layers.Length];
            var profileName = face is null
                ? biome.Id
                : $"{biome.Id}/{face}";
            for (var index = 0; index < layers.Length - 1; index++)
            {
                boundaryDomains[index] = GenerationDomain.Named(
                    $"material/surface-strata-boundary/v1/{profileName}/{index}");
            }

            return new MaterialRule(
                layers,
                boundaryDomains,
                biome.SurfaceLayout is not null);
        }
    }

    private sealed class ResolvedLayer
    {
        public ResolvedLayer(
            BlockRuntimeId block,
            uint? endDepthExclusive,
            uint boundaryNoise,
            ResolvedPatch? patch)
        {
            Block = block;
            EndDepthExclusive =
                endDepthExclusive;
            BoundaryNoise = boundaryNoise;
            Patch = patch;
        }

        public BlockRuntimeId Block { get; }

        public uint? EndDepthExclusive { get; }

        public uint BoundaryNoise { get; }

        private ResolvedPatch? Patch { get; }

        public bool HasConditions => Patch?.HasConditions == true;

        public bool RequiresSlope => Patch?.RequiresSlope == true;

        public BlockRuntimeId ResolveVolume(
            ulong seed, int x, int y, int z,
            SurfacePlacementContext? placement) =>
            Patch?.ResolveVolume(seed, x, y, z, placement) ?? Block;

        public BlockRuntimeId Resolve(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement) =>
            Patch?.Resolve(
                seed,
                worldX,
                worldZ,
                placement) ??
            Block;
    }

    private sealed class ResolvedPatch
    {
        private readonly BiomeSurfacePatchDefinition _definition;
        private readonly BlockRuntimeId[] _blocks;
        private readonly double[] _cumulativeWeights;
        private readonly double _totalWeight;
        private readonly GenerationDomain _shapeDomain;
        private readonly GenerationDomain _detailDomain;
        private readonly GenerationDomain _blockDomain;
        private readonly GenerationDomain _warpXDomain;
        private readonly GenerationDomain _warpZDomain;
        private readonly GenerationDomain _volumeMaskDomain;
        private readonly GenerationDomain _volumeSelectionDomain;

        private ResolvedPatch(
            BiomeSurfacePatchDefinition definition,
            BlockRuntimeId[] blocks,
            string suffix)
        {
            _definition = definition;
            _blocks = blocks;
            _cumulativeWeights = new double[blocks.Length];
            var total = 0d;
            for (var index = 0; index < blocks.Length; index++)
            {
                total += definition.BlockWeights[index];
                _cumulativeWeights[index] = total;
            }

            _totalWeight = total;
            _shapeDomain = GenerationDomain.Named(
                $"material/surface-patch/shape/v2/{suffix}");
            _detailDomain = GenerationDomain.Named(
                $"material/surface-patch/detail/v2/{suffix}");
            _blockDomain = GenerationDomain.Named(
                $"material/surface-patch/block/v2/{suffix}");
            _warpXDomain = GenerationDomain.Named(
                $"material/surface-patch/warp-x/v1/{suffix}");
            _warpZDomain = GenerationDomain.Named(
                $"material/surface-patch/warp-z/v1/{suffix}");
            _volumeMaskDomain = GenerationDomain.Named(
                $"material/volume-patch/mask/v1/{suffix}");
            _volumeSelectionDomain = GenerationDomain.Named(
                $"material/volume-patch/selection/v1/{suffix}");
        }

        public BlockRuntimeId? ResolveVolume(
            ulong seed, int x, int y, int z,
            SurfacePlacementContext? placement)
        {
            if (_definition.Conditions is { } conditions &&
                (placement is not { } context ||
                 !conditions.Allows(context)))
                return null;

            var verticalScale = Math.Max(2u, _definition.DetailScale);
            var noise = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeMaskDomain, x, y, z,
                _definition.Scale, verticalScale);
            if (noise < 1d - _definition.Coverage * 2d)
                return null;

            if (_blocks.Length == 1)
                return _blocks[0];

            var selection = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeSelectionDomain, x, y, z,
                _definition.SelectionScale, verticalScale);
            var unit = Math.Clamp((selection + 1d) * 0.5d,
                0d, 0.999999999999d);
            var target = unit * _totalWeight;
            for (var i = 0; i < _blocks.Length; i++)
            {
                if (target < _cumulativeWeights[i])
                    return _blocks[i];
            }
            return _blocks[^1];
        }

        public bool HasConditions => _definition.Conditions is not null;
        public bool RequiresSlope => _definition.Conditions?.RequiresSlope == true;

        public BlockRuntimeId? Resolve(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement)
        {
            if (_definition.Conditions is { } conditions &&
                (placement is not { } context ||
                 !conditions.Allows(context)))
            {
                return null;
            }

            var x = (double)worldX;
            var z = (double)worldZ;
            if (_definition.WarpStrength > 0d)
            {
                var dx = WorldGenerationEntropy.SmoothNoise2D(
                    seed, _warpXDomain, x, z, _definition.WarpScale);
                var dz = WorldGenerationEntropy.SmoothNoise2D(
                    seed, _warpZDomain, x + 19.7d, z - 11.3d,
                    _definition.WarpScale);
                x += dx * _definition.WarpStrength;
                z += dz * _definition.WarpStrength;
            }

            z /= _definition.StretchZ;
            var morphed = _definition.WarpStrength > 0d ||
                _definition.StretchZ != 1d;
            var macro = Noise(
                seed, _shapeDomain, worldX, worldZ, x, z,
                _definition.Scale, morphed);
            var detail = Noise(
                seed, _detailDomain, worldX, worldZ, x, z,
                _definition.DetailScale, morphed);
            var field = (macro + detail * _definition.Roughness) /
                (1d + _definition.Roughness);
            if (field < 1d - _definition.Coverage * 2d)
            {
                return null;
            }

            if (_blocks.Length == 1)
            {
                return _blocks[0];
            }

            var selection = Noise(
                seed, _blockDomain, worldX, worldZ, x, z,
                _definition.SelectionScale, morphed);
            var unit = Math.Clamp(
                (selection + 1d) * 0.5d,
                0d,
                0.999999999999d);
            var target = unit * _totalWeight;
            for (var index = 0; index < _blocks.Length; index++)
            {
                if (target < _cumulativeWeights[index])
                {
                    return _blocks[index];
                }
            }

            return _blocks[^1];
        }

        private static double Noise(
            ulong seed,
            GenerationDomain domain,
            int worldX,
            int worldZ,
            double x,
            double z,
            uint scale,
            bool morphed) =>
            morphed
                ? WorldGenerationEntropy.SmoothNoise2D(
                    seed, domain, x, z, scale)
                : WorldGenerationEntropy.ValueNoise2D(
                    seed, domain, worldX, worldZ, scale);

        public static ResolvedPatch Create(
            string biomeId,
            int layerIndex,
            BiomeSurfacePatchDefinition definition,
            BlockRegistry blocks) =>
            new(
                definition,
                definition.Blocks.Select(blocks.GetId).ToArray(),
                $"{biomeId}/{layerIndex}");
    }
}


/// <summary>
/// A resolved X/Z material profile; patch decisions are constant for an
/// authored layer and are evaluated once, not for every voxel depth.
/// </summary>
public sealed class BiomeSurfaceMaterialColumn
{
    private readonly uint[] _endDepths;
    private readonly BlockRuntimeId[] _blocks;
    private readonly BlockRuntimeId _surfaceOverride;

    internal BiomeSurfaceMaterialColumn(
        uint[] endDepths,
        BlockRuntimeId[] blocks,
        BlockRuntimeId surfaceOverride = default)
    {
        _endDepths = endDepths;
        _blocks = blocks;
        _surfaceOverride = surfaceOverride;
    }

    public uint FiniteDepth =>
        _endDepths.Length > 1
            ? _endDepths[^2]
            : 0u;

    public BlockRuntimeId BlockAt(uint depth)
    {
        if (depth == 0 && !_surfaceOverride.IsAir)
            return _surfaceOverride;

        for (var i = 0; i < _endDepths.Length; i++)
        {
            if (depth < _endDepths[i])
            {
                return _blocks[i];
            }
        }

        throw new InvalidOperationException(
            "Validated surface material profile must end with a core layer.");
    }
}
