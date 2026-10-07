namespace Asteria.Core.World;

/// <summary>
/// Resolves generated block material for already-solid surface terrain.
/// Terrain height may blend biome influences; material ownership stays with
/// the authoritative primary surface biome.
/// </summary>
public sealed class BiomeSurfaceMaterialField
{
    private readonly ulong _seed;
    private readonly Dictionary<string, MaterialRule> _rules;

    public BiomeSurfaceMaterialField(
        ulong seed,
        IEnumerable<BiomeDefinition> biomes,
        BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(blocks);

        _seed = seed;
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
        uint depth)
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
            depth);
    }

    public BiomeSurfaceMaterialColumn SampleColumn(
        BiomeSample sample,
        int worldX,
        int worldZ)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (!_rules.TryGetValue(sample.Primary, out var rule))
        {
            throw new KeyNotFoundException(
                $"Surface biome {sample.Primary} has no material rule.");
        }

        return rule.SampleColumn(_seed, worldX, worldZ);
    }

    private sealed class MaterialRule
    {
        private MaterialRule(
            ResolvedLayer[] layers)
        {
            Layers = layers;
        }

        private ResolvedLayer[] Layers { get; }

        public BlockRuntimeId BlockAtDepth(
            ulong seed,
            int worldX,
            int worldZ,
            uint depth)
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
                    worldZ);
            }

            throw new InvalidOperationException(
                "Validated surface material profile must end with a core layer.");
        }

        public BiomeSurfaceMaterialColumn SampleColumn(
            ulong seed,
            int worldX,
            int worldZ)
        {
            var depths = new uint[Layers.Length];
            var blocks = new BlockRuntimeId[Layers.Length];

            for (var index = 0; index < Layers.Length; index++)
            {
                var layer = Layers[index];
                depths[index] =
                    layer.EndDepthExclusive ?? uint.MaxValue;
                blocks[index] = layer.Resolve(seed, worldX, worldZ);
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
                layers);
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

        public BlockRuntimeId Resolve(
            ulong seed,
            int worldX,
            int worldZ) =>
            Patch?.Resolve(
                seed,
                worldX,
                worldZ) ??
            Block;
    }

    private sealed class ResolvedPatch
    {
        private ResolvedPatch(
            int spacing,
            long radius,
            uint jitter,
            float chance,
            BlockRuntimeId[] blocks,
            GenerationDomain presenceDomain,
            GenerationDomain jitterXDomain,
            GenerationDomain jitterZDomain,
            GenerationDomain blockDomain)
        {
            Spacing = spacing;
            Radius = radius;
            Jitter = jitter;
            Chance = chance;
            Blocks = blocks;
            PresenceDomain =
                presenceDomain;
            JitterXDomain =
                jitterXDomain;
            JitterZDomain =
                jitterZDomain;
            BlockDomain =
                blockDomain;
        }

        private int Spacing { get; }

        private long Radius { get; }

        private uint Jitter { get; }

        private float Chance { get; }

        private BlockRuntimeId[] Blocks { get; }

        private GenerationDomain PresenceDomain { get; }

        private GenerationDomain JitterXDomain { get; }

        private GenerationDomain JitterZDomain { get; }

        private GenerationDomain BlockDomain { get; }

        public BlockRuntimeId? Resolve(
            ulong seed,
            int worldX,
            int worldZ)
        {
            var centerCellX =
                FloorDiv(
                    worldX,
                    Spacing);
            var centerCellZ =
                FloorDiv(
                    worldZ,
                    Spacing);
            var radiusSquared =
                (Int128)Radius *
                Radius;
            PatchCandidate? winner =
                null;

            for (var dz = -1;
                 dz <= 1;
                 dz++)
            {
                if (!TryOffset(
                        centerCellZ,
                        dz,
                        out var cellZ))
                {
                    continue;
                }

                for (var dx = -1;
                     dx <= 1;
                     dx++)
                {
                    if (!TryOffset(
                            centerCellX,
                            dx,
                            out var cellX))
                    {
                        continue;
                    }

                    var presence =
                        WorldGenerationEntropy
                            .Sample2D(
                                seed,
                                PresenceDomain,
                                cellX,
                                cellZ);

                    if (UnitProbability(
                            presence) >
                        Chance)
                    {
                        continue;
                    }

                    var patchX =
                        (long)cellX *
                        Spacing +
                        Spacing / 2L +
                        JitterOffset(
                            seed,
                            JitterXDomain,
                            cellX,
                            cellZ);
                    var patchZ =
                        (long)cellZ *
                        Spacing +
                        Spacing / 2L +
                        JitterOffset(
                            seed,
                            JitterZDomain,
                            cellX,
                            cellZ);
                    var deltaX =
                        (Int128)worldX -
                        patchX;
                    var deltaZ =
                        (Int128)worldZ -
                        patchZ;
                    var distanceSquared =
                        deltaX *
                        deltaX +
                        deltaZ *
                        deltaZ;

                    if (distanceSquared >
                        radiusSquared)
                    {
                        continue;
                    }

                    var blockIndex =
                        checked(
                            (int)(
                                WorldGenerationEntropy
                                    .Sample2D(
                                        seed,
                                        BlockDomain,
                                        cellX,
                                        cellZ) %
                                (ulong)Blocks.Length));
                    var candidate =
                        new PatchCandidate(
                            distanceSquared,
                            cellX,
                            cellZ,
                            blockIndex);

                    if (winner is null ||
                        candidate.CompareTo(
                            winner.Value) <
                        0)
                    {
                        winner =
                            candidate;
                    }
                }
            }

            return winner is
                { } matched
                ? Blocks[
                    matched.BlockIndex]
                : null;
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
                checked(
                    (int)definition.Spacing),
                definition.Radius,
                definition.Jitter,
                definition.Chance,
                definition.Blocks
                    .Select(
                        blocks.GetId)
                    .ToArray(),
                GenerationDomain.Named(
                    $"material/surface-patch/presence/v1/{suffix}"),
                GenerationDomain.Named(
                    $"material/surface-patch/jitter-x/v1/{suffix}"),
                GenerationDomain.Named(
                    $"material/surface-patch/jitter-z/v1/{suffix}"),
                GenerationDomain.Named(
                    $"material/surface-patch/block/v1/{suffix}"));
        }

        private long JitterOffset(
            ulong seed,
            GenerationDomain domain,
            int cellX,
            int cellZ)
        {
            if (Jitter == 0)
            {
                return 0;
            }

            var span =
                (ulong)Jitter *
                2UL +
                1UL;
            var value =
                WorldGenerationEntropy
                    .Sample2D(
                        seed,
                        domain,
                        cellX,
                        cellZ);

            return checked(
                (long)(value % span) -
                (long)Jitter);
        }

        private static double UnitProbability(
            ulong value) =>
            value /
            (double)ulong.MaxValue;

        private static int FloorDiv(
            int value,
            int divisor)
        {
            var quotient =
                Math.DivRem(
                    value,
                    divisor,
                    out var remainder);

            return remainder >= 0
                ? quotient
                : quotient - 1;
        }

        private static bool TryOffset(
            int value,
            int offset,
            out int result)
        {
            var candidate =
                (long)value +
                offset;

            if (candidate is <
                    int.MinValue or >
                    int.MaxValue)
            {
                result = default;
                return false;
            }

            result =
                (int)candidate;
            return true;
        }

        private readonly record struct PatchCandidate(
            Int128 DistanceSquared,
            int CellX,
            int CellZ,
            int BlockIndex) :
            IComparable<PatchCandidate>
        {
            public int CompareTo(
                PatchCandidate other)
            {
                var result =
                    DistanceSquared.CompareTo(
                        other.DistanceSquared);
                if (result != 0)
                {
                    return result;
                }

                result =
                    CellX.CompareTo(
                        other.CellX);
                if (result != 0)
                {
                    return result;
                }

                result =
                    CellZ.CompareTo(
                        other.CellZ);
                return result != 0
                    ? result
                    : BlockIndex.CompareTo(
                        other.BlockIndex);
            }
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
