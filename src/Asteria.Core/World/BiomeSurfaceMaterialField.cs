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
    private readonly Dictionary<string, MaterialRule> _rules;

    public BiomeSurfaceMaterialField(
        ulong seed,
        IEnumerable<BiomeDefinition> biomes,
        BlockRegistry blocks,
        SurfaceTerrainField? terrain = null)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(blocks);

        _seed = seed;
        _terrain = terrain;
        _rules =
            biomes
                .OrderBy(
                    biome =>
                        biome.Id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    biome =>
                        biome.Id,
                    biome =>
                        MaterialRule.Create(
                            biome,
                            blocks),
                    StringComparer.Ordinal);

        if (_rules.Count == 0)
        {
            throw new ArgumentException(
                "Surface material field requires at least one biome.",
                nameof(biomes));
        }
    }

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

        return rule.SampleColumn(
            _seed,
            worldX,
            worldZ,
            PlacementFor(rule, worldX, worldZ, placement));
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

    private sealed class MaterialRule
    {
        private MaterialRule(
            ResolvedLayer[] layers,
            bool usesBaseSurface)
        {
            Layers = layers;
            UsesBaseSurface = usesBaseSurface;
            HasConditions = layers.Any(layer => layer.HasConditions);
            RequiresSlope = layers.Any(layer => layer.RequiresSlope);
            CoreBlock = layers[^1].Block;
            CoreStartDepth = layers.Length > 1
                ? layers[^2].EndDepthExclusive!.Value
                : 0u;
        }

        private ResolvedLayer[] Layers { get; }

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
            foreach (var layer in Layers)
            {
                if (layer.EndDepthExclusive is
                        { } endDepth &&
                    depth >= endDepth)
                {
                    continue;
                }

                return layer.Resolve(
                    seed,
                    worldX,
                    worldZ,
                    placement);
            }

            throw new InvalidOperationException(
                "Validated surface material profile must end with a core layer.");
        }

        public BiomeSurfaceMaterialColumn SampleColumn(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement)
        {
            var depths = new uint[Layers.Length];
            var blocks = new BlockRuntimeId[Layers.Length];

            for (var index = 0; index < Layers.Length; index++)
            {
                var layer = Layers[index];
                depths[index] =
                    layer.EndDepthExclusive ?? uint.MaxValue;
                blocks[index] = layer.Resolve(
                    seed, worldX, worldZ, placement);
            }

            return new BiomeSurfaceMaterialColumn(depths, blocks);
        }

        public static MaterialRule Create(
            BiomeDefinition biome,
            BlockRegistry blocks)
        {
            var finiteDepth = 0u;
            var layers =
                new ResolvedLayer[
                    biome.SurfaceLayers.Count];

            for (var index = 0;
                 index < layers.Length;
                 index++)
            {
                var definition =
                    biome.SurfaceLayers[index];
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
                        definition.Patch is
                            { } patch
                            ? ResolvedPatch.Create(
                                biome.Id,
                                index,
                                patch,
                                blocks)
                            : null);
            }

            return new MaterialRule(
                layers,
                biome.SurfaceLayout is not null);
        }
    }

    private sealed class ResolvedLayer
    {
        public ResolvedLayer(
            BlockRuntimeId block,
            uint? endDepthExclusive,
            ResolvedPatch? patch)
        {
            Block = block;
            EndDepthExclusive =
                endDepthExclusive;
            Patch = patch;
        }

        public BlockRuntimeId Block { get; }

        public uint? EndDepthExclusive { get; }

        private ResolvedPatch? Patch { get; }

        public bool HasConditions => Patch?.HasConditions == true;

        public bool RequiresSlope => Patch?.RequiresSlope == true;

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

    internal BiomeSurfaceMaterialColumn(
        uint[] endDepths,
        BlockRuntimeId[] blocks)
    {
        _endDepths = endDepths;
        _blocks = blocks;
    }

    public uint FiniteDepth =>
        _endDepths.Length > 1
            ? _endDepths[^2]
            : 0u;

    public BlockRuntimeId BlockAt(uint depth)
    {
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
