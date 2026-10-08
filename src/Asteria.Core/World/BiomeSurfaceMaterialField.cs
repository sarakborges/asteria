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
        }

        private ResolvedLayer[] Layers { get; }

        public bool HasConditions { get; }

        public bool RequiresSlope { get; }

        public bool UsesBaseSurface { get; }

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

        private BlockRuntimeId Block { get; }

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
        private ResolvedPatch(
            uint scale,
            float coverage,
            float roughness,
            BlockRuntimeId[] blocks,
            GenerationDomain shapeDomain,
            GenerationDomain detailDomain,
            GenerationDomain blockDomain,
            SurfacePlacementConditions? conditions)
        {
            Scale = scale;
            Coverage = coverage;
            Roughness = roughness;
            Blocks = blocks;
            ShapeDomain = shapeDomain;
            DetailDomain = detailDomain;
            BlockDomain = blockDomain;
            Conditions = conditions;
        }

        private uint Scale { get; }

        private float Coverage { get; }

        private float Roughness { get; }

        private BlockRuntimeId[] Blocks { get; }

        private GenerationDomain ShapeDomain { get; }

        private GenerationDomain DetailDomain { get; }

        private GenerationDomain BlockDomain { get; }

        private SurfacePlacementConditions? Conditions { get; }

        public bool HasConditions => Conditions is not null;

        public bool RequiresSlope => Conditions?.RequiresSlope == true;

        public BlockRuntimeId? Resolve(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement)
        {
            if (Conditions is { } conditions)
            {
                if (placement is not { } context ||
                    !conditions.Allows(context))
                {
                    return null;
                }
            }

            var detailScale =
                Math.Max(
                    2u,
                    Scale / 4u);
            var macro =
                WorldGenerationEntropy
                    .ValueNoise2D(
                        seed,
                        ShapeDomain,
                        worldX,
                        worldZ,
                        Scale);
            var detail =
                WorldGenerationEntropy
                    .ValueNoise2D(
                        seed,
                        DetailDomain,
                        worldX,
                        worldZ,
                        detailScale);
            var field =
                (macro +
                 detail * Roughness) /
                (1d + Roughness);
            var threshold =
                1d -
                Coverage * 2d;

            if (field < threshold)
            {
                return null;
            }

            if (Blocks.Length == 1)
            {
                return Blocks[0];
            }

            var blockScale =
                checked(
                    Scale * 2u);
            var selection =
                WorldGenerationEntropy
                    .ValueNoise2D(
                        seed,
                        BlockDomain,
                        worldX,
                        worldZ,
                        blockScale);
            var unit =
                Math.Clamp(
                    (selection + 1d) *
                    0.5d,
                    0d,
                    0.999999999999d);
            var blockIndex =
                Math.Min(
                    Blocks.Length - 1,
                    (int)(
                        unit *
                        Blocks.Length));

            return Blocks[blockIndex];
        }

        public static ResolvedPatch Create(
            string biomeId,
            int layerIndex,
            BiomeSurfacePatchDefinition definition,
            BlockRegistry blocks)
        {
            var suffix =
                $"{biomeId}/{layerIndex}";

            return new ResolvedPatch(
                definition.Scale,
                definition.Coverage,
                definition.Roughness,
                definition.Blocks
                    .Select(
                        blocks.GetId)
                    .ToArray(),
                GenerationDomain.Named(
                    $"material/surface-patch/shape/v2/{suffix}"),
                GenerationDomain.Named(
                    $"material/surface-patch/detail/v2/{suffix}"),
                GenerationDomain.Named(
                    $"material/surface-patch/block/v2/{suffix}"),
                definition.Conditions);
        }
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
