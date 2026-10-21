namespace Asteria.Core.World;

/// <summary>
/// Resolves generated block material for already-solid surface terrain.
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
        _faceRules = biomes
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(
                biome => biome.Id,
                biome => FaceRules.Create(biome, blocks),
                StringComparer.Ordinal);
        _rules = _faceRules.ToDictionary(
            pair => pair.Key, pair => pair.Value.Floor,
            StringComparer.Ordinal);
        HasExteriorOverrides = _faceRules.Values.Any(
            profile => profile.Exterior &&
                (profile.OverrideWalls || profile.OverrideCeiling));
        HasExteriorFaces = _faceRules.Values.Any(profile =>
            profile.Exterior &&
            (profile.OverrideWalls || profile.OverrideCeiling ||
             !profile.Walls.HasUniformMaterial || profile.Walls.HasPatches ||
             profile.Floor.HasPatches));
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
    /// True when the generation contains surface or additive-volume solids
    /// whose exposed faces need materialization from world-space coordinates.
    /// Unlike HasExteriorOverrides, this includes inherited default palettes.
    /// </summary>
    public bool HasExteriorFaces { get; }

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

        if (depth == 0 &&
            _mosaic?.Sample(sample, worldX, worldZ) is
                { Block.IsAir: false } choice)
            return choice.Block;

        if (depth >= rule.CoreStartDepth)
            return rule.CoreBlock;

        return rule.BlockAtDepth(
            _seed,
            worldX,
            worldZ,
            depth,
            PlacementFor(rule, worldX, worldZ, placement));
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
        var rule = rules.For(face);
        if (face == BiomePaletteFace.Walls &&
            rules.Exterior && !rules.OverrideWalls)
            return rule.ExteriorWallBlockAtWorldPosition(
                _seed, worldX, worldY, worldZ);
        return rule.VolumeBlockAt(
            _seed, worldX, worldY, worldZ, depth,
            VolumePlacementFor(rule, worldX, worldY, worldZ));
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

    /// <summary>
    /// True when exterior walls need an authored layer or patch palette.
    /// Inherited multi-layer profiles use deterministic 3D material regions.
    /// </summary>
    public bool HasExteriorWallMaterial(BiomeSample sample) =>
        _faceRules.TryGetValue(sample.Primary, out var rules) &&
        rules.Exterior &&
        (rules.OverrideWalls || !rules.Walls.HasUniformMaterial ||
         rules.Walls.HasPatches || rules.Floor.HasPatches);

    /// <summary>
    /// Creates a world-space sampler for an inherited wall palette. Material
    /// regions are continuous across chunk and column boundaries.
    /// </summary>
    public ExteriorWallMaterialColumn SampleExteriorWallColumn(
        BiomeSample sample, int worldX, int worldZ)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_faceRules.TryGetValue(sample.Primary, out var rules))
            throw new KeyNotFoundException(
                $"Biome {sample.Primary} has no material palette.");
        if (rules.OverrideWalls)
            throw new InvalidOperationException(
                $"Biome {sample.Primary} defines an explicit wall palette.");

        return CreateExteriorWallMaterialColumn(
            _seed, worldX, worldZ, rules.Floor);
    }

    /// <summary>
    /// Resolves an exposed wall voxel. Explicit directional wall profiles remain
    /// authoritative and measure depth inward from the face. Inherited palettes
    /// use warped, categorical 3D regions instead of surface-relative layers.
    /// </summary>
    public BlockRuntimeId ExteriorWallBlockAt(
        BiomeSample sample,
        int worldX,
        int worldY,
        int worldZ,
        int surfaceTopY,
        uint depth = 0)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_faceRules.TryGetValue(sample.Primary, out var rules))
            throw new KeyNotFoundException(
                $"Biome {sample.Primary} has no material palette.");

        if (!rules.OverrideWalls)
        {
            return rules.Floor.ExteriorWallBlockAtWorldPosition(
                _seed, worldX, worldY, worldZ);
        }

        var wallRule = rules.Walls;
        var wallBlock = wallRule.VolumeBlockAt(
            _seed, worldX, worldY, worldZ, depth,
            placement: null, ignorePlacementConditions: true);

        if (!rules.Exterior || !rules.Floor.HasPatches ||
            !wallRule.HasUniformMaterial || wallRule.HasPatches)
            return wallBlock;

        // A uniform explicit wall palette may inherit authored surface patches
        // as 3D outcrops without top-surface altitude/slope placement gates.
        return rules.Floor.VolumePatchBlockAt(
            _seed, worldX, worldY, worldZ, depth: 0,
            placement: null, ignorePlacementConditions: true)
            ?? wallBlock;
    }

    private ExteriorWallMaterialColumn CreateExteriorWallMaterialColumn(
        ulong seed, int worldX, int worldZ, MaterialRule rule)
    {
        // Inherited wall palettes use deterministic 3D material regions.
        return new ExteriorWallMaterialColumn(
            worldY => rule.ExteriorWallBlockAtWorldPosition(
                seed, worldX, worldY, worldZ));
    }

    public sealed class ExteriorWallMaterialColumn
    {
        private readonly Func<int, BlockRuntimeId> _resolve;

        internal ExteriorWallMaterialColumn(
            Func<int, BlockRuntimeId> resolve) =>
            _resolve = resolve;

        public BlockRuntimeId BlockAt(int worldY, int surfaceTopY) =>
            _resolve(worldY);
    }

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

    private SurfacePlacementContext? VolumePlacementFor(
        MaterialRule rule,
        int worldX,
        int worldY,
        int worldZ)
    {
        if (!rule.HasConditions)
            return null;

        if (_terrain is null)
        {
            throw new InvalidOperationException(
                "Conditional volume material patches require a terrain field.");
        }

        var sampled = SurfacePlacementContext.Sample(
            _terrain, worldX, worldZ, rule.RequiresSlope);
        return new SurfacePlacementContext(worldY, sampled.Slope);
    }

    private SurfacePlacementContext? PlacementFor(
        MaterialRule rule,
        int worldX,
        int worldZ,
        SurfacePlacementContext? supplied)
    {
        if (supplied.HasValue)
            return supplied;

        if (!rule.HasConditions && !rule.HasPatches)
            return null;

        if (_terrain is null)
        {
            if (rule.HasConditions)
            {
                throw new InvalidOperationException(
                    "Conditional material patches require a terrain field.");
            }

            // Standalone palette queries without a terrain field retain their
            // deterministic origin slice. World generation always has terrain.
            return null;
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

    private sealed class MaterialRule
    {
        private const uint BoundaryNoiseScale = 48;
        private const uint ExteriorWallHorizontalScale = 36;
        private const uint ExteriorWallVerticalScale = 24;
        private const uint ExteriorWallDetailHorizontalScale = 12;
        private const uint ExteriorWallDetailVerticalScale = 8;
        private const uint ExteriorWallWarpHorizontalScale = 108;
        private const uint ExteriorWallWarpVerticalScale = 72;
        private const double ExteriorWallWarpHorizontalAmplitude = 12d;
        private const double ExteriorWallWarpVerticalAmplitude = 7d;
        private const double ExteriorWallDetailWeight = 0.28d;

        private MaterialRule(
            ResolvedLayer[] layers,
            GenerationDomain[] boundaryDomains,
            GenerationDomain exteriorWallMaterialDomain,
            GenerationDomain exteriorWallDetailDomain,
            GenerationDomain exteriorWallWarpXDomain,
            GenerationDomain exteriorWallWarpYDomain,
            GenerationDomain exteriorWallWarpZDomain,
            bool usesBaseSurface)
        {
            Layers = layers;
            BoundaryDomains = boundaryDomains;
            ExteriorWallMaterialDomain = exteriorWallMaterialDomain;
            ExteriorWallDetailDomain = exteriorWallDetailDomain;
            ExteriorWallWarpXDomain = exteriorWallWarpXDomain;
            ExteriorWallWarpYDomain = exteriorWallWarpYDomain;
            ExteriorWallWarpZDomain = exteriorWallWarpZDomain;
            _materialCumulativeWeights = new double[layers.Length];
            var totalWeight = 0d;
            for (var index = 0; index < layers.Length; index++)
            {
                totalWeight += ExteriorWallMaterialWeight(layers, index);
                _materialCumulativeWeights[index] = totalWeight;
            }
            _materialWeightTotal = totalWeight;
            UsesBaseSurface = usesBaseSurface;
            HasConditions = layers.Any(layer => layer.HasConditions);
            HasPatches = layers.Any(layer => layer.HasPatch);
            HasUniformMaterial = layers.All(layer => layer.Block == layers[0].Block);
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
        private GenerationDomain ExteriorWallMaterialDomain { get; }
        private GenerationDomain ExteriorWallDetailDomain { get; }
        private GenerationDomain ExteriorWallWarpXDomain { get; }
        private GenerationDomain ExteriorWallWarpYDomain { get; }
        private GenerationDomain ExteriorWallWarpZDomain { get; }
        private readonly double[] _materialCumulativeWeights;
        private readonly double _materialWeightTotal;

        public bool HasConditions { get; }

        public bool HasPatches { get; }

        public bool HasUniformMaterial { get; }

        public bool RequiresSlope { get; }

        public BlockRuntimeId ExteriorWallBlockAtWorldPosition(
            ulong seed, int worldX, int worldY, int worldZ)
        {
            // A warped, multi-scale field in world-space XYZ selects material
            // regions throughout the voxel volume, rather than assigning the
            // same material to a depth interval or a regular cellular grid.
            var warpX = WorldGenerationEntropy.ValueNoise3D(
                seed, ExteriorWallWarpXDomain, worldX, worldY, worldZ,
                ExteriorWallWarpHorizontalScale, ExteriorWallWarpVerticalScale);
            var warpY = WorldGenerationEntropy.ValueNoise3D(
                seed, ExteriorWallWarpYDomain, worldX, worldY, worldZ,
                ExteriorWallWarpHorizontalScale, ExteriorWallWarpVerticalScale);
            var warpZ = WorldGenerationEntropy.ValueNoise3D(
                seed, ExteriorWallWarpZDomain, worldX, worldY, worldZ,
                ExteriorWallWarpHorizontalScale, ExteriorWallWarpVerticalScale);
            var warpedX = worldX + warpX * ExteriorWallWarpHorizontalAmplitude;
            var warpedY = worldY + warpY * ExteriorWallWarpVerticalAmplitude;
            var warpedZ = worldZ + warpZ * ExteriorWallWarpHorizontalAmplitude;

            var broad = WorldGenerationEntropy.ValueNoise3D(
                seed, ExteriorWallMaterialDomain, warpedX, warpedY, warpedZ,
                ExteriorWallHorizontalScale, ExteriorWallVerticalScale);
            var detail = WorldGenerationEntropy.ValueNoise3D(
                seed, ExteriorWallDetailDomain, warpedX, warpedY, warpedZ,
                ExteriorWallDetailHorizontalScale, ExteriorWallDetailVerticalScale);
            var field = broad * (1d - ExteriorWallDetailWeight) +
                        detail * ExteriorWallDetailWeight;
            var target = Math.Clamp((field + 1d) * 0.5d, 0d, 0.999999999999d) *
                         _materialWeightTotal;
            var selected = _materialCumulativeWeights.Length - 1;
            for (var index = 0; index < _materialCumulativeWeights.Length; index++)
            {
                if (target < _materialCumulativeWeights[index])
                {
                    selected = index;
                    break;
                }
            }

            return Layers[selected].ResolveVolume(
                seed, worldX, worldY, worldZ,
                placement: null, ignorePlacementConditions: true);
        }

        private static double ExteriorWallMaterialWeight(
            ResolvedLayer[] layers, int index)
        {
            if (layers[index].EndDepthExclusive is not { } endDepth)
            {
                if (index == 0)
                    return 1d;
                var previous = layers[index - 1].EndDepthExclusive.GetValueOrDefault();
                var beforePrevious = index > 1
                    ? layers[index - 2].EndDepthExclusive.GetValueOrDefault()
                    : 0u;
                return Math.Max(1u, previous - beforePrevious);
            }

            var previousEnd = index == 0
                ? 0u
                : layers[index - 1].EndDepthExclusive.GetValueOrDefault();
            return Math.Max(1u, endDepth - previousEnd);
        }

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
            SurfacePlacementContext? placement,
            bool ignorePlacementConditions = false)
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
                return layer.ResolveVolume(
                    seed, x, y, z, placement, ignorePlacementConditions);
            }
            throw new InvalidOperationException(
                "Validated palette must end with a core layer.");
        }

        public BlockRuntimeId? VolumePatchBlockAt(
            ulong seed, int x, int y, int z, uint depth,
            SurfacePlacementContext? placement,
            bool ignorePlacementConditions = false)
        {
            if (depth >= CoreStartDepth)
                return null;

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

                return layer.ResolveVolumePatch(
                    seed, x, y, z, placement, ignorePlacementConditions);
            }

            return null;
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
                GenerationDomain.Named($"material/volumetric-regions/v1/{profileName}"),
                GenerationDomain.Named($"material/volumetric-detail/v1/{profileName}"),
                GenerationDomain.Named($"material/volumetric-warp-x/v1/{profileName}"),
                GenerationDomain.Named($"material/volumetric-warp-y/v1/{profileName}"),
                GenerationDomain.Named($"material/volumetric-warp-z/v1/{profileName}"),
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

        public bool HasPatch => Patch is not null;

        public bool RequiresSlope => Patch?.RequiresSlope == true;

        public BlockRuntimeId? ResolveVolumePatch(
            ulong seed, int x, int y, int z,
            SurfacePlacementContext? placement,
            bool ignorePlacementConditions) =>
            Patch?.ResolveVolume(
                seed, x, y, z, placement, ignorePlacementConditions);

        public BlockRuntimeId ResolveVolume(
            ulong seed, int x, int y, int z,
            SurfacePlacementContext? placement,
            bool ignorePlacementConditions = false) =>
            Patch?.ResolveVolume(
                seed, x, y, z, placement, ignorePlacementConditions) ?? Block;

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
        private const uint PlacementBoundaryScale = 48;
        private const int MaximumAltitudeBoundaryDisplacement = 4;

        private readonly BiomeSurfacePatchDefinition _definition;
        private readonly BlockRuntimeId[] _blocks;
        private readonly double[] _cumulativeWeights;
        private readonly double _totalWeight;
        private readonly GenerationDomain _detailDomain;
        private readonly GenerationDomain _warpXDomain;
        private readonly GenerationDomain _warpZDomain;
        private readonly GenerationDomain _volumeMaskDomain;
        private readonly GenerationDomain _volumeFineDetailDomain;
        private readonly GenerationDomain _volumeSelectionDomain;
        private readonly GenerationDomain _volumeSelectionDetailDomain;
        private readonly GenerationDomain _volumeWarpXDomain;
        private readonly GenerationDomain _volumeWarpYDomain;
        private readonly GenerationDomain _volumeWarpZDomain;
        private readonly GenerationDomain _altitudeBoundaryDomain;

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
            _detailDomain = GenerationDomain.Named(
                $"material/surface-patch/detail/v2/{suffix}");
            _warpXDomain = GenerationDomain.Named(
                $"material/surface-patch/warp-x/v1/{suffix}");
            _warpZDomain = GenerationDomain.Named(
                $"material/surface-patch/warp-z/v1/{suffix}");
            _volumeMaskDomain = GenerationDomain.Named(
                $"material/volume-patch/mask/v1/{suffix}");
            _volumeFineDetailDomain = GenerationDomain.Named(
                $"material/volume-patch/fine-detail/v1/{suffix}");
            _volumeSelectionDomain = GenerationDomain.Named(
                $"material/volume-patch/selection/v1/{suffix}");
            _volumeSelectionDetailDomain = GenerationDomain.Named(
                $"material/volume-patch/selection-detail/v1/{suffix}");
            _volumeWarpXDomain = GenerationDomain.Named(
                $"material/volume-patch/warp-x/v1/{suffix}");
            _volumeWarpYDomain = GenerationDomain.Named(
                $"material/volume-patch/warp-y/v1/{suffix}");
            _volumeWarpZDomain = GenerationDomain.Named(
                $"material/volume-patch/warp-z/v1/{suffix}");
            _altitudeBoundaryDomain = GenerationDomain.Named(
                $"material/surface-patch/altitude-boundary/v1/{suffix}");
        }

        public BlockRuntimeId? ResolveVolume(
            ulong seed, int x, int y, int z,
            SurfacePlacementContext? placement,
            bool ignorePlacementConditions = false)
        {
            if (!ignorePlacementConditions &&
                !AllowsPlacement(seed, x, z, placement))
                return null;

            var (sampleX, sampleZ) = TransformCoordinates(seed, x, z);
            var (warpedX, warpedY, warpedZ) =
                TransformVolumeCoordinates(seed, sampleX, y, sampleZ);
            if (!IsInsideMask(seed, warpedX, warpedY, warpedZ))
                return null;

            return SelectBlock(seed, warpedX, warpedY, warpedZ);
        }

        public bool HasConditions => _definition.Conditions is not null;
        public bool RequiresSlope => _definition.Conditions?.RequiresSlope == true;

        public BlockRuntimeId? Resolve(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement)
        {
            if (!AllowsPlacement(seed, worldX, worldZ, placement))
                return null;

            var (sampleX, sampleZ) =
                TransformCoordinates(seed, worldX, worldZ);
            // This exact Y slice is also queried by ResolveVolume, so a patch
            // visible at the surface continues naturally onto adjoining walls.
            var sampleY = placement?.Y ?? 0;
            var (warpedX, warpedY, warpedZ) =
                TransformVolumeCoordinates(seed, sampleX, sampleY, sampleZ);
            if (!IsInsideMask(seed, warpedX, warpedY, warpedZ))
                return null;

            return SelectBlock(seed, warpedX, warpedY, warpedZ);
        }

        private (double X, double Z) TransformCoordinates(
            ulong seed, int worldX, int worldZ)
        {
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

            return (x, z / _definition.StretchZ);
        }

        private (double X, double Y, double Z) TransformVolumeCoordinates(
            ulong seed, double x, double y, double z)
        {
            var amplitude = Math.Min(_definition.WarpStrength, 14d);
            if (amplitude <= 0d)
                return (x, y, z);

            // A second, fully 3D warp bends the authored patch itself. The
            // earlier X/Z-only transform could leave broad, nearly parallel
            // contours on flat seabeds and cliff faces.
            var warpScale = (uint)Math.Clamp(
                (long)Math.Max(2u, _definition.Scale) * 2L, 2L, 256L);
            var warpX = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeWarpXDomain, x, y, z, warpScale, warpScale);
            var warpY = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeWarpYDomain, x, y, z, warpScale, warpScale);
            var warpZ = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeWarpZDomain, x, y, z, warpScale, warpScale);
            return (
                x + warpX * amplitude,
                y + warpY * amplitude * 0.55d,
                z + warpZ * amplitude);
        }

        private bool IsInsideMask(
            ulong seed, double x, double y, double z)
        {
            var macroScale = Math.Max(2u, _definition.Scale);
            var detailScale = Math.Max(2u, _definition.DetailScale);
            var fineDetailScale = Math.Max(2u, detailScale / 2u);
            var macro = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeMaskDomain, x, y, z,
                macroScale, macroScale);
            var detail = WorldGenerationEntropy.ValueNoise3D(
                seed, _detailDomain, x, y, z,
                detailScale, detailScale);
            var fineDetail = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeFineDetailDomain, x, y, z,
                fineDetailScale, fineDetailScale);
            var fineWeight = Math.Max(0.12d, _definition.Roughness * 0.65d);
            var field = (macro +
                         detail * _definition.Roughness +
                         fineDetail * fineWeight) /
                (1d + _definition.Roughness + fineWeight);
            return field >= 1d - _definition.Coverage * 2d;
        }

        private BlockRuntimeId SelectBlock(
            ulong seed, double x, double y, double z)
        {
            if (_blocks.Length == 1)
                return _blocks[0];

            var selectionScale = Math.Max(2u, _definition.SelectionScale);
            var detailScale = Math.Max(2u, _definition.DetailScale);
            var selection = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeSelectionDomain, x, y, z,
                selectionScale, selectionScale);
            var detail = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeSelectionDetailDomain, x, y, z,
                detailScale, detailScale);
            var field = selection * 0.72d + detail * 0.28d;
            var unit = Math.Clamp((field + 1d) * 0.5d,
                0d, 0.999999999999d);
            var target = unit * _totalWeight;
            for (var index = 0; index < _blocks.Length; index++)
            {
                if (target < _cumulativeWeights[index])
                    return _blocks[index];
            }

            return _blocks[^1];
        }

        private bool AllowsPlacement(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement)
        {
            if (_definition.Conditions is not { } conditions)
                return true;
            if (placement is not { } context)
                return false;

            var maximumY = conditions.MaxY is { } authoredMaximum
                ? (long)authoredMaximum
                : long.MaxValue;
            if (conditions.MaxY.HasValue)
            {
                // Retraction only removes patch coverage from inside the
                // authored range: it never permits a patch above maxY.
                var noise = WorldGenerationEntropy.ValueNoise2D(
                    seed, _altitudeBoundaryDomain, worldX, worldZ,
                    PlacementBoundaryScale);
                var unit = Math.Clamp((noise + 1d) * 0.5d, 0d, 1d);
                var retraction = (int)Math.Round(
                    unit * MaximumAltitudeBoundaryDisplacement,
                    MidpointRounding.AwayFromZero);
                maximumY -= retraction;
            }

            return (!conditions.MinY.HasValue ||
                    context.Y >= conditions.MinY.Value) &&
                context.Y <= maximumY &&
                (!conditions.MinSlope.HasValue ||
                    context.Slope >= conditions.MinSlope.Value) &&
                (!conditions.MaxSlope.HasValue ||
                    context.Slope <= conditions.MaxSlope.Value);
        }

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
