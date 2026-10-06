namespace Asteria.Core.World;

public sealed class BiomeWorldGenerator : IChunkProvider, IChunkSurfaceRangeProvider
{
    private readonly ulong _seed;
    private readonly int _seaLevel;
    private readonly BlockRuntimeId _shellBlock;
    private readonly int? _floorY;
    private readonly int? _roofY;
    private readonly BlockRegistry _blocks;
    private readonly BiomeField _field;
    private readonly Dictionary<
        string,
        ResolvedBiomeProfile> _profiles;
    private readonly GenerationDomain _surfaceChoiceDomain =
        GenerationDomain.Named(
            "worldgen/surface-biome-choice/v1");

    public BiomeWorldGenerator(
        ulong seed,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        BiomeRegistry biomes)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        _blocks =
            blocks ??
            throw new ArgumentNullException(
                nameof(blocks));
        ArgumentNullException.ThrowIfNull(
            biomes);

        biomes.ValidateBlocks(
            blocks);

        _seed = seed;
        _seaLevel =
            dimension.SeaLevel;
        _shellBlock =
            dimension.Shell is
                { } shell
                ? blocks.GetId(
                    shell.Block)
                : BlockRuntimeId.Air;
        _floorY =
            dimension.Shell?.FloorY;
        _roofY =
            dimension.Shell?.RoofY;
        DimensionId =
            dimension.Id;
        _field =
            new BiomeField(
                seed,
                dimension,
                biomes);

        _profiles =
            dimension
                .Biomes
                .Select(
                    biomes.Get)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    definition =>
                        definition.Id,
                    definition =>
                        ResolvedBiomeProfile.Create(
                            definition,
                            blocks),
                    StringComparer.Ordinal);

        if (_profiles.Count == 0)
        {
            throw new ArgumentException(
                $"Dimension {dimension.Id} has no surface biome profiles.",
                nameof(dimension));
        }
    }

    public DimensionId DimensionId { get; }

    public BiomeField Biomes =>
        _field;

    public Chunk Materialize(
        ChunkCoord coord)
    {
        if (coord.Y < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coord),
                "Sphere chunk Y cannot be negative.");
        }

        var chunk =
            new Chunk();
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(
                coord);
        var samples =
            _field.SampleGrid(
                originX,
                originZ,
                Chunk.Size,
                Chunk.Size);

        for (var localZ = 0;
             localZ < Chunk.Size;
             localZ++)
        {
            for (var localX = 0;
                 localX < Chunk.Size;
                 localX++)
            {
                var worldX =
                    checked(
                        originX +
                        localX);
                var worldZ =
                    checked(
                        originZ +
                        localZ);
                var sample =
                    samples[
                        localX,
                        localZ];
                var surfaceY =
                    SurfaceHeight(
                        sample,
                        worldX,
                        worldZ);
                var materialProfile =
                    SelectProfile(
                        sample,
                        worldX,
                        worldZ,
                        _surfaceChoiceDomain);
                var surfaceBlock =
                    BlockRuntimeId.Air;

                for (var localY = 0;
                     localY < Chunk.Size;
                     localY++)
                {
                    var worldY =
                        checked(
                            originY +
                            localY);

                    if (IsShellY(
                            worldY))
                    {
                        chunk.SetBlock(
                            localX,
                            localY,
                            localZ,
                            _shellBlock);
                        continue;
                    }

                    if (IsOutsideSphere(
                            worldY) ||
                        worldY >
                            surfaceY)
                    {
                        continue;
                    }

                    var depth =
                        checked(
                            surfaceY -
                            worldY);
                    var block =
                        materialProfile
                            .SurfaceBlock(
                                _seed,
                                worldX,
                                worldZ,
                                depth);

                    chunk.SetBlock(
                        localX,
                        localY,
                        localZ,
                        block);

                    if (worldY ==
                        surfaceY)
                    {
                        surfaceBlock =
                            block;
                    }
                }

                if (surfaceBlock.IsAir)
                {
                    surfaceBlock =
                        materialProfile
                            .SurfaceBlock(
                                _seed,
                                worldX,
                                worldZ,
                                depth: 0);
                }

                var decorationY =
                    checked(
                        surfaceY +
                        1);
                if (IsShellY(
                        decorationY) ||
                    IsOutsideSphere(
                        decorationY) ||
                    decorationY <
                        originY ||
                    decorationY >=
                        originY +
                        Chunk.Size)
                {
                    continue;
                }

                var decoration =
                    DecorationAt(
                        sample,
                        surfaceBlock,
                        worldX,
                        worldZ);

                if (decoration.IsAir)
                {
                    continue;
                }

                chunk.SetBlock(
                    localX,
                    decorationY -
                        originY,
                    localZ,
                    decoration);
            }
        }

        return chunk;
    }

    public int SurfaceHeight(
        int worldX,
        int worldZ) =>
        SurfaceHeight(
            _field.Sample(
                worldX,
                worldZ),
            worldX,
            worldZ);

    public ChunkSurfaceRange GetSurfaceRange(
        int chunkX,
        int chunkZ)
    {
        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(
                new ChunkCoord(
                    chunkX,
                    0,
                    chunkZ));
        var samples =
            _field.SampleGrid(
                originX,
                originZ,
                Chunk.Size,
                Chunk.Size);
        var minimum =
            int.MaxValue;
        var maximum =
            int.MinValue;

        for (var localZ = 0;
             localZ < Chunk.Size;
             localZ++)
        {
            for (var localX = 0;
                 localX < Chunk.Size;
                 localX++)
            {
                var worldX =
                    checked(
                        originX +
                        localX);
                var worldZ =
                    checked(
                        originZ +
                        localZ);
                var height =
                    SurfaceHeight(
                        samples[
                            localX,
                            localZ],
                        worldX,
                        worldZ);
                minimum =
                    Math.Min(
                        minimum,
                        height);
                maximum =
                    Math.Max(
                        maximum,
                        height);
            }
        }

        return new ChunkSurfaceRange(
            minimum,
            maximum);
    }

    private int SurfaceHeight(
        BiomeSample sample,
        int worldX,
        int worldZ)
    {
        var height =
            (double)_seaLevel;

        foreach (var influence in
                 sample.Influences)
        {
            var profile =
                _profiles[
                    influence.BiomeId];
            height +=
                profile.HeightOffsetAt(
                    _seed,
                    worldX,
                    worldZ) *
                influence.Weight;
        }

        var surfaceY =
            checked(
                (int)Math.Floor(
                    height));

        if (_roofY is
                { } roofY &&
            surfaceY >=
                roofY)
        {
            surfaceY =
                roofY -
                1;
        }

        return surfaceY;
    }

    private bool IsShellY(
        int worldY) =>
        !_shellBlock.IsAir &&
        (
            _floorY ==
                worldY ||
            _roofY ==
                worldY
        );

    private bool IsOutsideSphere(
        int worldY) =>
        (_floorY is
             { } floorY &&
         worldY <
             floorY) ||
        (_roofY is
             { } roofY &&
         worldY >
             roofY);

    private ResolvedBiomeProfile SelectProfile(
        BiomeSample sample,
        int worldX,
        int worldZ,
        GenerationDomain domain)
    {
        var pick =
            WorldGenerationEntropy
                .Unit(
                    WorldGenerationEntropy
                        .Sample2D(
                            _seed,
                            domain,
                            worldX,
                            worldZ));
        var cursor =
            0d;

        foreach (var influence in
                 sample.Influences)
        {
            cursor +=
                influence.Weight;

            if (pick <=
                cursor)
            {
                return _profiles[
                    influence.BiomeId];
            }
        }

        return _profiles[
            sample.Influences[
                sample.Influences.Count -
                1].BiomeId];
    }

    private BlockRuntimeId DecorationAt(
        BiomeSample sample,
        BlockRuntimeId surfaceBlock,
        int worldX,
        int worldZ)
    {
        foreach (var influence in
                 sample.Influences)
        {
            var profile =
                _profiles[
                    influence.BiomeId];

            foreach (var decoration in
                     profile.Decorations)
            {
                if (!decoration
                        .SurfaceBlocks
                        .Contains(
                            surfaceBlock))
                {
                    continue;
                }

                var effectiveChance =
                    decoration.Chance *
                    influence.Weight;
                var roll =
                    WorldGenerationEntropy
                        .Unit(
                            WorldGenerationEntropy
                                .Sample2D(
                                    _seed,
                                    decoration.Domain,
                                    worldX,
                                    worldZ));

                if (roll <
                    effectiveChance)
                {
                    return decoration.Block;
                }
            }
        }

        return BlockRuntimeId.Air;
    }

    private sealed class ResolvedBiomeProfile
    {
        private ResolvedBiomeProfile(
            string id,
            BiomeTerrainDefinition terrain,
            ResolvedSurfaceLayer[] layers,
            ResolvedDecoration[] decorations)
        {
            Id = id;
            Terrain = terrain;
            Layers = layers;
            Decorations = decorations;
            MacroDomain =
                GenerationDomain.Named(
                    $"terrain/base-surface/macro/v1/{id}");
            DetailDomain =
                GenerationDomain.Named(
                    $"terrain/base-surface/detail/v1/{id}");
        }

        public string Id { get; }

        public BiomeTerrainDefinition Terrain { get; }

        public ResolvedSurfaceLayer[] Layers { get; }

        public ResolvedDecoration[] Decorations { get; }

        public GenerationDomain MacroDomain { get; }

        public GenerationDomain DetailDomain { get; }

        public double HeightOffsetAt(
            ulong seed,
            int worldX,
            int worldZ)
        {
            var macro =
                WorldGenerationEntropy
                    .ValueNoise2D(
                        seed,
                        MacroDomain,
                        worldX,
                        worldZ,
                        Terrain.MacroScale);
            var detail =
                WorldGenerationEntropy
                    .ValueNoise2D(
                        seed,
                        DetailDomain,
                        worldX,
                        worldZ,
                        Terrain.DetailScale);

            return Terrain.BaseHeightOffset +
                   macro *
                   Terrain.MacroAmplitude +
                   detail *
                   Terrain.DetailAmplitude;
        }

        public BlockRuntimeId SurfaceBlock(
            ulong seed,
            int worldX,
            int worldZ,
            int depth)
        {
            var cursor =
                0;

            for (var index = 0;
                 index < Layers.Length;
                 index++)
            {
                var layer =
                    Layers[index];

                if (layer.Depth is
                    not { } layerDepth)
                {
                    return layer.Block;
                }

                if (depth <
                    cursor +
                    layerDepth)
                {
                    return layer.Resolve(
                        seed,
                        worldX,
                        worldZ);
                }

                cursor +=
                    layerDepth;
            }

            return Layers[
                Layers.Length -
                1].Block;
        }

        public static ResolvedBiomeProfile Create(
            BiomeDefinition definition,
            BlockRegistry blocks)
        {
            var layers =
                definition
                    .SurfaceLayers
                    .Select(
                        (layer, index) =>
                            ResolvedSurfaceLayer
                                .Create(
                                    definition.Id,
                                    index,
                                    layer,
                                    blocks))
                    .ToArray();
            var decorations =
                definition
                    .Decorations
                    .OrderBy(
                        decoration =>
                            decoration.Block,
                        StringComparer.Ordinal)
                    .Select(
                        decoration =>
                            ResolvedDecoration
                                .Create(
                                    definition.Id,
                                    decoration,
                                    blocks))
                    .ToArray();

            return new ResolvedBiomeProfile(
                definition.Id,
                definition.SurfaceTerrain,
                layers,
                decorations);
        }
    }

    private sealed class ResolvedSurfaceLayer
    {
        private ResolvedSurfaceLayer(
            BlockRuntimeId block,
            int? depth,
            ResolvedPatch? patch)
        {
            Block = block;
            Depth = depth;
            Patch = patch;
        }

        public BlockRuntimeId Block { get; }

        public int? Depth { get; }

        public ResolvedPatch? Patch { get; }

        public BlockRuntimeId Resolve(
            ulong seed,
            int worldX,
            int worldZ) =>
            Patch?.Resolve(
                seed,
                worldX,
                worldZ,
                Block) ??
            Block;

        public static ResolvedSurfaceLayer Create(
            string biomeId,
            int layerIndex,
            BiomeSurfaceLayerDefinition definition,
            BlockRegistry blocks) =>
            new(
                blocks.GetId(
                    definition.Block),
                definition.Depth is
                    { } depth
                    ? checked((int)depth)
                    : null,
                definition.Patch is
                    { } patch
                    ? ResolvedPatch.Create(
                        biomeId,
                        layerIndex,
                        patch,
                        blocks)
                    : null);
    }

    private sealed class ResolvedPatch
    {
        private ResolvedPatch(
            int spacing,
            int radius,
            int jitter,
            float chance,
            BlockRuntimeId[] blocks,
            GenerationDomain chanceDomain,
            GenerationDomain jitterXDomain,
            GenerationDomain jitterZDomain,
            GenerationDomain blockDomain)
        {
            Spacing = spacing;
            Radius = radius;
            Jitter = jitter;
            Chance = chance;
            Blocks = blocks;
            ChanceDomain = chanceDomain;
            JitterXDomain = jitterXDomain;
            JitterZDomain = jitterZDomain;
            BlockDomain = blockDomain;
        }

        public int Spacing { get; }

        public int Radius { get; }

        public int Jitter { get; }

        public float Chance { get; }

        public BlockRuntimeId[] Blocks { get; }

        public GenerationDomain ChanceDomain { get; }

        public GenerationDomain JitterXDomain { get; }

        public GenerationDomain JitterZDomain { get; }

        public GenerationDomain BlockDomain { get; }

        public BlockRuntimeId Resolve(
            ulong seed,
            int worldX,
            int worldZ,
            BlockRuntimeId fallback)
        {
            var bucketX =
                FloorDiv(
                    worldX,
                    Spacing);
            var bucketZ =
                FloorDiv(
                    worldZ,
                    Spacing);
            var matched =
                false;
            var bestDistanceSquared =
                double.PositiveInfinity;
            var bestBucket =
                default((int X, int Z));

            for (var dz = -1;
                 dz <= 1;
                 dz++)
            {
                for (var dx = -1;
                     dx <= 1;
                     dx++)
                {
                    var x =
                        bucketX +
                        dx;
                    var z =
                        bucketZ +
                        dz;
                    var chance =
                        WorldGenerationEntropy
                            .Unit(
                                WorldGenerationEntropy
                                    .Sample2D(
                                        seed,
                                        ChanceDomain,
                                        x,
                                        z));

                    if (chance >=
                        Chance)
                    {
                        continue;
                    }

                    var centerX =
                        x *
                        (double)Spacing +
                        Spacing *
                        0.5d +
                        JitterOffset(
                            seed,
                            JitterXDomain,
                            x,
                            z);
                    var centerZ =
                        z *
                        (double)Spacing +
                        Spacing *
                        0.5d +
                        JitterOffset(
                            seed,
                            JitterZDomain,
                            x,
                            z);
                    var offsetX =
                        worldX -
                        centerX;
                    var offsetZ =
                        worldZ -
                        centerZ;
                    var distanceSquared =
                        offsetX *
                        offsetX +
                        offsetZ *
                        offsetZ;

                    if (distanceSquared >
                        Radius *
                        (double)Radius)
                    {
                        continue;
                    }

                    if (!matched ||
                        distanceSquared <
                            bestDistanceSquared ||
                        (distanceSquared ==
                             bestDistanceSquared &&
                         (x <
                              bestBucket.X ||
                          (x ==
                               bestBucket.X &&
                           z <
                               bestBucket.Z))))
                    {
                        matched = true;
                        bestDistanceSquared =
                            distanceSquared;
                        bestBucket =
                            (x, z);
                    }
                }
            }

            if (!matched)
            {
                return fallback;
            }

            var hash =
                WorldGenerationEntropy
                    .Sample2D(
                        seed,
                        BlockDomain,
                        bestBucket.X,
                        bestBucket.Z);

            return Blocks[
                (int)(hash %
                      (ulong)Blocks.Length)];
        }

        public static ResolvedPatch Create(
            string biomeId,
            int layerIndex,
            BiomeSurfacePatchDefinition definition,
            BlockRegistry blocks)
        {
            var prefix =
                $"worldgen/patch/{biomeId}/{layerIndex}";

            return new ResolvedPatch(
                checked((int)
                    definition.Spacing),
                checked((int)
                    definition.Radius),
                checked((int)
                    definition.Jitter),
                definition.Chance,
                definition.Blocks
                    .Select(
                        blocks.GetId)
                    .ToArray(),
                GenerationDomain.Named(
                    prefix +
                    "/chance/v1"),
                GenerationDomain.Named(
                    prefix +
                    "/jitter-x/v1"),
                GenerationDomain.Named(
                    prefix +
                    "/jitter-z/v1"),
                GenerationDomain.Named(
                    prefix +
                    "/block/v1"));
        }

        private double JitterOffset(
            ulong seed,
            GenerationDomain domain,
            int x,
            int z) =>
            Jitter == 0
                ? 0d
                : WorldGenerationEntropy
                    .SignedUnit(
                        WorldGenerationEntropy
                            .Sample2D(
                                seed,
                                domain,
                                x,
                                z)) *
                  Jitter;

        private static int FloorDiv(
            int value,
            int divisor)
        {
            var quotient =
                Math.DivRem(
                    value,
                    divisor,
                    out var remainder);

            if (remainder < 0)
            {
                quotient--;
            }

            return quotient;
        }
    }

    private sealed class ResolvedDecoration
    {
        private ResolvedDecoration(
            BlockRuntimeId block,
            float chance,
            HashSet<BlockRuntimeId> surfaceBlocks,
            GenerationDomain domain)
        {
            Block = block;
            Chance = chance;
            SurfaceBlocks = surfaceBlocks;
            Domain = domain;
        }

        public BlockRuntimeId Block { get; }

        public float Chance { get; }

        public HashSet<BlockRuntimeId> SurfaceBlocks { get; }

        public GenerationDomain Domain { get; }

        public static ResolvedDecoration Create(
            string biomeId,
            BiomeDecorationDefinition definition,
            BlockRegistry blocks) =>
            new(
                blocks.GetId(
                    definition.Block),
                definition.Chance,
                definition.SurfaceBlocks
                    .Select(
                        blocks.GetId)
                    .ToHashSet(),
                GenerationDomain.Named(
                    $"worldgen/decorator/{biomeId}/{definition.Block}/v1"));
    }
}
