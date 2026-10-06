namespace Asteria.Core.World;

public sealed class BiomeWorldGenerator : IChunkProvider, IChunkSurfaceRangeProvider
{
    private readonly ulong _seed;
    private readonly int _seaLevel;
    private readonly BlockRuntimeId _shellBlock;
    private readonly int? _floorY;
    private readonly int? _roofY;
    private readonly BiomeField _field;
    private readonly BiomeSurfaceMaterialField _materials;
    private readonly Dictionary<
        string,
        ResolvedBiomeProfile> _profiles;

    public BiomeWorldGenerator(
        ulong seed,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        BiomeRegistry biomes)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            blocks);
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

        var activeBiomes =
            dimension
                .Biomes
                .Select(
                    biomes.Get)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();

        _materials =
            new BiomeSurfaceMaterialField(
                seed,
                activeBiomes,
                blocks);
        Tints =
            new BiomeTintField(
                _field,
                activeBiomes);
        _profiles =
            activeBiomes
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

    public BiomeTintField Tints { get; }

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
                        _materials.BlockAt(
                            sample,
                            worldX,
                            worldZ,
                            checked((uint)depth));

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
                        _materials.BlockAt(
                            sample,
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
            ResolvedDecoration[] decorations)
        {
            Id = id;
            Terrain = terrain;
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

        public static ResolvedBiomeProfile Create(
            BiomeDefinition definition,
            BlockRegistry blocks)
        {
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
                decorations);
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
