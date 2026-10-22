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
        private readonly BlockRuntimeId[] _volumeBlocks;
        private readonly GenerationDomain _surfaceExposureDomain;
        private readonly GenerationDomain[] _warpDomains;
        private readonly GenerationDomain[] _warpDetailDomains;
        private readonly GenerationDomain[] _volumeMaterialDomains;
        private readonly GenerationDomain[] _volumeDetailDomains;
        private readonly GenerationDomain[] _volumeMicroDomains;
        private readonly GenerationDomain[] _volumeGrainDomains;
        private readonly GenerationDomain[] _surfaceMaterialDomains;
        private readonly GenerationDomain[] _surfaceDetailDomains;
        private readonly GenerationDomain[] _surfaceMicroDomains;
        private readonly GenerationDomain[] _surfaceGrainDomains;
        private readonly double[] _volumeBiases;
        private readonly double[] _surfaceBiases;

        private ResolvedGeology(
            BiomeGeologyDefinition definition,
            BlockRuntimeId baseBlock,
            BlockRuntimeId[] blocks,
            string biomeId)
        {
            _definition = definition;
            _baseBlock = baseBlock;
            _blocks = blocks;
            _volumeBlocks = [baseBlock, .. blocks];
            _surfaceExposureDomain = GenerationDomain.Named(
                $"material/geology/surface-exposure/v2/{biomeId}");
            _warpDomains = new[] { "x", "y", "z" }
                .Select(axis => GenerationDomain.Named(
                    $"material/geology/domain-warp/v4/{biomeId}/{axis}"))
                .ToArray();
            _warpDetailDomains = new[] { "x", "y", "z" }
                .Select(axis => GenerationDomain.Named(
                    $"material/geology/domain-warp-detail/v1/{biomeId}/{axis}"))
                .ToArray();

            var volumeNames = new[] { definition.BaseBlock }
                .Concat(definition.Blocks).ToArray();
            _volumeMaterialDomains = volumeNames
                .Select(block => GenerationDomain.Named(
                    $"material/geology/volume-field/v1/{biomeId}/{block}"))
                .ToArray();
            _volumeDetailDomains = volumeNames
                .Select(block => GenerationDomain.Named(
                    $"material/geology/volume-detail/v1/{biomeId}/{block}"))
                .ToArray();
            _volumeMicroDomains = volumeNames
                .Select(block => GenerationDomain.Named(
                    $"material/geology/volume-micro/v1/{biomeId}/{block}"))
                .ToArray();
            _volumeGrainDomains = volumeNames
                .Select(block => GenerationDomain.Named(
                    $"material/geology/volume-grain/v1/{biomeId}/{block}"))
                .ToArray();

            _surfaceMaterialDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/surface-field/v1/{biomeId}/{block}"))
                .ToArray();
            _surfaceDetailDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/surface-detail/v2/{biomeId}/{block}"))
                .ToArray();
            _surfaceMicroDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/surface-micro/v1/{biomeId}/{block}"))
                .ToArray();
            _surfaceGrainDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/surface-grain/v1/{biomeId}/{block}"))
                .ToArray();

            var alternativeWeightTotal = definition.BlockWeights.Sum();
            var baseProbability = 1d - definition.Coverage;
            // Spatial fields are correlated value-noise samples rather than
            // independent ideal Gumbel variates. Compress log-biases so rare
            // authored materials remain present in every broad region while
            // weights still influence their relative frequency.
            const double biasStrength = 0.35d;
            _volumeBiases = new double[_volumeBlocks.Length];
            _volumeBiases[0] = baseProbability > 0d
                ? biasStrength * Math.Log(baseProbability)
                : double.NegativeInfinity;
            for (var index = 0; index < definition.BlockWeights.Count; index++)
            {
                var probability = definition.Coverage *
                    definition.BlockWeights[index] / alternativeWeightTotal;
                _volumeBiases[index + 1] = biasStrength * Math.Log(probability);
            }

            _surfaceBiases = definition.BlockWeights
                .Select(weight => biasStrength * Math.Log(weight / alternativeWeightTotal))
                .ToArray();
        }

        public uint SurfaceCoverDepth => _definition.SurfaceCoverDepth;
        public float SurfaceCoverage => _definition.SurfaceCoverage;
        public bool SurfaceSubmergedOnly => _definition.SurfaceSubmergedOnly;

        public BlockRuntimeId BlockAt(ulong seed, int x, int y, int z)
        {
            // Base material and deposits now compete in one spatial selector.
            // The previous separate formation mask created a second set of
            // broad, continuous boundaries that looked like columns/sheets.
            var position = WarpCoordinates(seed, x, y, z);
            return SelectMaterial(
                seed, position, _volumeBlocks,
                _volumeMaterialDomains, _volumeDetailDomains, _volumeMicroDomains,
                _volumeGrainDomains, _volumeBiases);
        }

        public bool TrySurfaceBlockAt(
            ulong seed, int x, int y, int z, out BlockRuntimeId block)
        {
            if (_definition.SurfaceCoverage <= 0f)
            {
                block = BlockRuntimeId.Air;
                return false;
            }

            // Surface coverage is a dedicated shallow X/Z exposure field. It
            // must not also pass the volumetric occupancy threshold; that
            // double gate made many flat surfaces remain entirely grass/sand.
            var exposureScale = Math.Max(6u, _definition.DetailScale / 2u);
            var exposure = WorldGenerationEntropy.SmoothNoise2D(
                seed, _surfaceExposureDomain, x, z, exposureScale);
            if (exposure < 1d - _definition.SurfaceCoverage * 2d)
            {
                block = BlockRuntimeId.Air;
                return false;
            }

            var position = WarpCoordinates(seed, x, y, z);
            block = SelectMaterial(
                seed, position, _blocks,
                _surfaceMaterialDomains, _surfaceDetailDomains, _surfaceMicroDomains,
                _surfaceGrainDomains, _surfaceBiases);
            return true;
        }

        private static SpatialPosition RotateCoordinates(int x, int y, int z)
        {
            // Rotate the continuous domain so no noise axis follows a world
            // axis. This matters for vertical walls as well as horizontal cuts.
            const double cosineY = 0.8660254037844386d;
            const double sineY = 0.5d;
            const double cosineX = 0.9396926207859084d;
            const double sineX = 0.3420201433256687d;

            return new SpatialPosition(
                cosineY * x + sineY * sineX * y + sineY * cosineX * z,
                cosineX * y - sineX * z,
                -sineY * x + cosineY * sineX * y + cosineY * cosineX * z);
        }

        private static SpatialPosition RotateFieldPosition(
            SpatialPosition position, int orientation)
        {
            // Each noise scale uses a different orthogonal basis. Reusing one
            // basis for every octave left coincident boundary directions even
            // after adding finer noise, which appeared as aligned mineral bands.
            switch (orientation)
            {
                case 0:
                    return position;
                case 1:
                {
                    // Rotate around Y by 37°, then X by 29°.
                    const double cy = 0.7986355100472928d;
                    const double sy = 0.6018150231520483d;
                    const double cx = 0.8746197071393957d;
                    const double sx = 0.4848096202463371d;
                    var x1 = cy * position.X + sy * position.Z;
                    var z1 = -sy * position.X + cy * position.Z;
                    return new SpatialPosition(
                        x1,
                        cx * position.Y - sx * z1,
                        sx * position.Y + cx * z1);
                }
                case 2:
                {
                    // Rotate around Z by 53°, then Y by -29°.
                    const double cz = 0.6018150231520483d;
                    const double sz = 0.7986355100472928d;
                    const double cy = 0.8746197071393957d;
                    const double sy = -0.4848096202463371d;
                    var x1 = cz * position.X - sz * position.Y;
                    var y1 = sz * position.X + cz * position.Y;
                    return new SpatialPosition(
                        cy * x1 + sy * position.Z,
                        y1,
                        -sy * x1 + cy * position.Z);
                }
                default:
                {
                    // A third basis for the fine-grain signal.
                    const double cz = 0.9063077870366499d;
                    const double sz = 0.4226182617406994d;
                    const double cy = 0.6427876096865394d;
                    const double sy = 0.766044443118978d;
                    const double cx = 0.7313537016191705d;
                    const double sx = 0.6819983600624985d;
                    var x1 = cz * position.X - sz * position.Y;
                    var y1 = sz * position.X + cz * position.Y;
                    var x2 = cy * x1 + sy * position.Z;
                    var z2 = -sy * x1 + cy * position.Z;
                    return new SpatialPosition(
                        x2,
                        cx * y1 - sx * z2,
                        sx * y1 + cx * z2);
                }
            }
        }

        private SpatialPosition WarpCoordinates(
            ulong seed, int x, int y, int z)
        {
            var rotated = RotateCoordinates(x, y, z);
            var warpHorizontalScale = Math.Max(12u, _definition.Scale / 3u);
            var warpVerticalScale = Math.Max(10u, _definition.VerticalScale / 3u);
            var horizontalStrength = Math.Max(6d, _definition.DetailScale * 0.9d);
            var verticalStrength = Math.Max(5d, _definition.DetailScale * 0.7d);

            var warpX = WorldGenerationEntropy.ValueNoise3D(
                seed, _warpDomains[0], rotated.X, rotated.Y, rotated.Z,
                warpHorizontalScale, warpVerticalScale);
            var warpY = WorldGenerationEntropy.ValueNoise3D(
                seed, _warpDomains[1], rotated.X, rotated.Y, rotated.Z,
                warpHorizontalScale, warpVerticalScale);
            var warpZ = WorldGenerationEntropy.ValueNoise3D(
                seed, _warpDomains[2], rotated.X, rotated.Y, rotated.Z,
                warpHorizontalScale, warpVerticalScale);

            // A second, smaller-scale warp breaks the broad domain warp's
            // smooth directional bends. It is sampled in another basis and is
            // strong enough to bend material boundaries, not to randomize voxels.
            var detailPosition = RotateFieldPosition(rotated, 2);
            var detailWarpScale = Math.Max(6u, _definition.DetailScale / 2u);
            var detailWarpVerticalScale = Math.Max(
                5u, Math.Min(_definition.VerticalScale / 4u, _definition.DetailScale / 2u));
            var detailStrength = Math.Max(2d, _definition.DetailScale * 0.35d);
            var detailVerticalStrength = Math.Max(2d, _definition.DetailScale * 0.28d);
            var detailWarpX = WorldGenerationEntropy.ValueNoise3D(
                seed, _warpDetailDomains[0],
                detailPosition.X, detailPosition.Y, detailPosition.Z,
                detailWarpScale, detailWarpVerticalScale);
            var detailWarpY = WorldGenerationEntropy.ValueNoise3D(
                seed, _warpDetailDomains[1],
                detailPosition.X, detailPosition.Y, detailPosition.Z,
                detailWarpScale, detailWarpVerticalScale);
            var detailWarpZ = WorldGenerationEntropy.ValueNoise3D(
                seed, _warpDetailDomains[2],
                detailPosition.X, detailPosition.Y, detailPosition.Z,
                detailWarpScale, detailWarpVerticalScale);

            return new SpatialPosition(
                rotated.X + warpX * horizontalStrength + detailWarpX * detailStrength,
                rotated.Y + warpY * verticalStrength + detailWarpY * detailVerticalStrength,
                rotated.Z + warpZ * horizontalStrength + detailWarpZ * detailStrength);
        }

        private BlockRuntimeId SelectMaterial(
            ulong seed,
            SpatialPosition position,
            BlockRuntimeId[] candidateBlocks,
            GenerationDomain[] broadDomains,
            GenerationDomain[] detailDomains,
            GenerationDomain[] microDomains,
            GenerationDomain[] grainDomains,
            double[] biases)
        {
            var detailScale = Math.Max(6u, _definition.DetailScale);
            var detailVerticalScale = Math.Max(
                6u, Math.Min(_definition.VerticalScale / 3u, _definition.DetailScale));
            var microScale = Math.Max(4u, _definition.DetailScale / 2u);
            var microVerticalScale = Math.Max(4u, detailVerticalScale / 2u);
            // Fine-grain field introduces small three-dimensional pockets so
            // large formations cannot keep a single material through long
            // vertical columns merely because the broad fields change slowly.
            var grainScale = Math.Max(2u, detailScale / 8u);
            var grainVerticalScale = Math.Max(2u, detailVerticalScale / 8u);

            var broadPosition = RotateFieldPosition(position, 0);
            var detailPosition = RotateFieldPosition(position, 1);
            var microPosition = RotateFieldPosition(position, 2);
            var grainPosition = RotateFieldPosition(position, 3);
            var bestScore = double.NegativeInfinity;
            var bestIndex = 0;
            for (var index = 0; index < candidateBlocks.Length; index++)
            {
                var broad = WorldGenerationEntropy.ValueNoise3D(
                    seed, broadDomains[index],
                    broadPosition.X, broadPosition.Y, broadPosition.Z,
                    Math.Max(12u, _definition.SelectionScale),
                    Math.Max(10u, _definition.VerticalScale));
                var detail = WorldGenerationEntropy.ValueNoise3D(
                    seed, detailDomains[index],
                    detailPosition.X, detailPosition.Y, detailPosition.Z,
                    detailScale, detailVerticalScale);
                var micro = WorldGenerationEntropy.ValueNoise3D(
                    seed, microDomains[index],
                    microPosition.X, microPosition.Y, microPosition.Z,
                    microScale, microVerticalScale);
                var grain = WorldGenerationEntropy.ValueNoise3D(
                    seed, grainDomains[index],
                    grainPosition.X, grainPosition.Y, grainPosition.Z,
                    grainScale, grainVerticalScale);

                // Keep broad geology only as a weak coherence signal. Fine
                // and grain fields now dominate boundaries, breaking residual
                // sheets/columns while preserving multi-block deposits.
                var field = broad * 0.06d + detail * 0.17d +
                    micro * 0.22d + grain * 0.55d;
                var unit = Math.Clamp(
                    (field + 1d) * 0.5d, 1e-6d, 1d - 1e-6d);
                var gumbel = -Math.Log(-Math.Log(unit));
                var score = gumbel + biases[index];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            return candidateBlocks[bestIndex];
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
