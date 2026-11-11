using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CavernPaletteStreamingDivergenceTests
{
    private const ulong Seed = 181960897289965UL;
    private const string CavernBiomeId = "asteria:overworld/caverns";

    private static readonly (WorldVoxelCoord Position, string BaselineBlock)[] DivergentVoxels =
    [
        (new WorldVoxelCoord(156, 56, -35), "asteria:stone"),
        (new WorldVoxelCoord(156, 57, -35), "asteria:stone"),
        (new WorldVoxelCoord(156, 58, -35), "asteria:stone"),
        (new WorldVoxelCoord(156, 59, -35), "asteria:stone"),
        (new WorldVoxelCoord(159, 59, -34), "asteria:stone"),
        (new WorldVoxelCoord(156, 60, -35), "asteria:cobble_stone"),
        (new WorldVoxelCoord(159, 60, -34), "asteria:cobble_stone"),
        (new WorldVoxelCoord(156, 61, -35), "asteria:cobble_stone"),
        (new WorldVoxelCoord(156, 61, -34), "asteria:cobble_stone"),
        (new WorldVoxelCoord(159, 61, -34), "asteria:cobble_stone"),
        (new WorldVoxelCoord(156, 61, -33), "asteria:cobble_stone"),
        (new WorldVoxelCoord(159, 61, -33), "asteria:cobble_stone"),
    ];

    [Fact]
    public void StreamingBenchmarkDivergenceIsAuthoredByTheCavernWallPalette()
    {
        var dataRoot = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data");
        static IEnumerable<string> Documents(string root, string kind) =>
            Directory.EnumerateFiles(Path.Combine(root, kind), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText);
        static IEnumerable<string> OptionalDocuments(string root, string kind)
        {
            var directory = Path.Combine(root, kind);
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.json")
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(File.ReadAllText)
                : Array.Empty<string>();
        }

        var blocks = BlockRegistry.FromJson(Documents(dataRoot, "blocks"));
        var fluids = FluidRegistry.FromJson(Documents(dataRoot, "fluids"));
        var biomes = new BiomeRegistry(
            Documents(dataRoot, "biomes").Select(BiomeDefinitionJson.Parse));
        var structures = StructureRegistry.FromJson(
            OptionalDocuments(dataRoot, "structures"));
        var structureSets = StructureSetRegistry.FromJson(
            OptionalDocuments(dataRoot, "structure_sets"));
        var dimensions = DimensionRegistry.FromJson(
            Documents(dataRoot, "dimensions"));
        var dimension = dimensions.Get(DimensionId.Overworld);
        var dimensionSeed = DimensionSeed.Derive(Seed, dimension.Id);
        Assert.Equal(14813712133479341624UL, dimensionSeed);

        var generator = new BiomeWorldGenerator(
            dimensionSeed,
            dimension,
            blocks,
            fluids,
            biomes,
            structures,
            structureSets);
        var materials = new BiomeSurfaceMaterialField(
            dimensionSeed,
            dimension.SurfaceBiomes
                .Concat(dimension.VolumeBiomes)
                .Distinct(StringComparer.Ordinal)
                .Select(biomes.Get),
            blocks,
            generator.Terrain);
        var columns = new SurfaceTerrainColumnCache(generator.Terrain);
        var chunkCoord = new ChunkCoord(9, 3, -3);
        var origin = VoxelCoordinates.ChunkOrigin(chunkCoord);
        var sourceColumn = columns.Get(chunkCoord.X, chunkCoord.Z);
        var density = generator.SampleDensityVolume(
            origin.X, origin.Y, origin.Z,
            Chunk.Size, Chunk.Size, Chunk.Size);
        var chunk = generator.Materialize(chunkCoord);
        var world = new VoxelWorld();
        world.InsertChunk(chunkCoord, chunk);

        // The baseline materials are present when this chunk first arrives.
        // The divergence is introduced only when its +Z neighbor becomes
        // resident and the arrival-time face reconciliation runs.
        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            Assert.Equal(
                blocks.GetId(baselineBlock),
                chunk.GetBlock(local.X, local.Y, local.Z));
        }

        var arrivingNeighbor = new ChunkCoord(9, 3, -2);
        world.InsertChunk(
            arrivingNeighbor,
            generator.Materialize(arrivingNeighbor));
        var changedPositions = generator.ReconcileChunkArrival(
            world, arrivingNeighbor).ToHashSet();
        var gravel = blocks.GetId("asteria:gravel");

        var decisions = new List<PaletteDecision>(DivergentVoxels.Length);
        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            var currentBlock = chunk.GetBlock(local.X, local.Y, local.Z);
            Assert.Equal(gravel, currentBlock);
            Assert.Contains(position, changedPositions);

            var decision = FindAppliedPaletteDecision(
                generator,
                blocks,
                materials,
                columns,
                sourceColumn,
                density,
                position,
                dimension.Shell);
            var description = decision is null
                ? $"voxel={position.X},{position.Y},{position.Z}; baseline={baselineBlock}; current=asteria:gravel; no eligible directional palette decision"
                : $"voxel={position.X},{position.Y},{position.Z}; baseline={baselineBlock}; current=asteria:gravel; " +
                  $"owner={decision.OwnerBiome} ({decision.OwnerKind}); face={decision.Face}; depth={decision.Depth}; " +
                  $"air-neighbor={decision.EmptyNeighbor.X},{decision.EmptyNeighbor.Y},{decision.EmptyNeighbor.Z}; " +
                  $"cave-void={decision.IsCaveVoid}; selected={decision.ReplacementBlock}";

            Assert.NotNull(decision);
            Assert.True(decision.OwnerKind == BiomeRegionOwnerKind.Volume, description);
            Assert.Equal(CavernBiomeId, decision.OwnerBiome);
            Assert.Equal(BiomePaletteFace.Walls, decision.Face);
            Assert.True(decision.IsCaveVoid, description);
            Assert.Equal("asteria:gravel", decision.ReplacementBlock);
            decisions.Add(decision);
        }

        Assert.Equal(12, decisions.Count);
        Assert.All(decisions, decision =>
        {
            Assert.Equal(CavernBiomeId, decision.OwnerBiome);
            Assert.Equal(BiomePaletteFace.Walls, decision.Face);
            Assert.Equal("asteria:gravel", decision.ReplacementBlock);
        });
    }

    private static PaletteDecision? FindAppliedPaletteDecision(
        BiomeWorldGenerator generator,
        BlockRegistry blocks,
        BiomeSurfaceMaterialField materials,
        SurfaceTerrainColumnCache columns,
        SurfaceTerrainColumn sourceColumn,
        TerrainDensityVolume density,
        WorldVoxelCoord position,
        DimensionShellDefinition? shell)
    {
        var (_, sourceLocal) = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z);
        var baseY = sourceColumn.BaseHeightAt(
            sourceLocal.X, sourceLocal.Z);
        var volumeBiome = position.Y > baseY
            ? density.VolumeBiomeAt(
                sourceLocal.X, position.Y, sourceLocal.Z)
            : null;
        var surfaceOwner = new BiomeRegionOwner(
            sourceColumn.BiomeAt(sourceLocal.X, sourceLocal.Z),
            BiomeRegionOwnerKind.Surface);
        var solidOwner = volumeBiome is null
            ? surfaceOwner
            : new BiomeRegionOwner(volumeBiome, BiomeRegionOwnerKind.Volume);
        var solidPalette = materials.DirectionalPaletteFor(solidOwner);

        // This is the production ReapplyDirectionalFacePaletteAt tie order.
        // The outer distance loop reproduces nearest-face selection; the
        // inner order preserves Up, -X, +X, -Z, +Z, Down precedence.
        (int Dx, int Dy, int Dz, BiomePaletteFace Face)[] directions =
        [
            (0, 1, 0, BiomePaletteFace.Floor),
            (-1, 0, 0, BiomePaletteFace.Walls),
            (1, 0, 0, BiomePaletteFace.Walls),
            (0, 0, -1, BiomePaletteFace.Walls),
            (0, 0, 1, BiomePaletteFace.Walls),
            (0, -1, 0, BiomePaletteFace.Ceiling),
        ];

        for (var distance = 1;
             distance <= checked((int)materials.MaximumDirectionalPaintDepth);
             distance++)
        foreach (var direction in directions)
        {
            var neighborX = checked(position.X + direction.Dx * distance);
            var neighborY = checked(position.Y + direction.Dy * distance);
            var neighborZ = checked(position.Z + direction.Dz * distance);
            if (neighborY < 0 ||
                generator.DensityAt(neighborX, neighborY, neighborZ) >= 0d)
                continue;

            var (_, neighborLocal) = VoxelCoordinates.FromWorld(
                neighborX, neighborY, neighborZ);
            var neighborChunk = VoxelCoordinates.FromWorld(
                neighborX, 0, neighborZ).Chunk;
            var targetColumn = columns.Get(
                neighborChunk.X, neighborChunk.Z);
            var emptyBiome = targetColumn.CaveBiomeAt(
                neighborLocal.X, neighborLocal.Z);
            var emptyPalette = emptyBiome is null
                ? default
                : materials.DirectionalPaletteFor(
                    new BiomeRegionOwner(
                        emptyBiome, BiomeRegionOwnerKind.Volume));
            var targetBaseY = targetColumn.BaseHeightAt(
                neighborLocal.X, neighborLocal.Z);
            var surfaceFluidCutDepth = targetColumn.SurfaceFluidCutDepthAt(
                neighborLocal.X, neighborLocal.Z);
            var shellBoundary = IsSphereShellBoundaryOrLining(
                neighborY, shell);
            var emptyCanOwn =
                emptyPalette.Has(direction.Face) &&
                emptyBiome is not null &&
                neighborY > 0 &&
                (long)neighborY <= (long)targetBaseY - surfaceFluidCutDepth;
            if (emptyCanOwn)
                emptyCanOwn = !shellBoundary;

            if (!solidPalette.Has(direction.Face) && !emptyCanOwn)
                continue;

            var owner = solidOwner;
            var palette = solidPalette;
            if (emptyCanOwn)
            {
                var resolved = SurfaceTerrainField.ResolveRegionOwner(
                    targetColumn.BiomeAt(neighborLocal.X, neighborLocal.Z),
                    additiveVolumeBiome: null,
                    emptyRegionVolumeBiome: emptyBiome,
                    baseY: targetBaseY,
                    surfaceFluidCutDepth: surfaceFluidCutDepth,
                    worldY: neighborY,
                    isEmpty: true,
                    isSphereShellBoundaryOrLining: shellBoundary);
                if (resolved.Kind == BiomeRegionOwnerKind.Volume)
                {
                    owner = resolved;
                    palette = emptyPalette;
                }
            }

            var depth = checked((uint)distance - 1u);
            if (!palette.Has(direction.Face) || depth >= palette.MaxDepth)
                continue;

            var replacement = materials.VolumeBlockAt(
                owner.Biome,
                position.X,
                position.Y,
                position.Z,
                direction.Face,
                depth);
            return new PaletteDecision(
                owner.Biome.Primary,
                owner.Kind,
                direction.Face,
                depth,
                new WorldVoxelCoord(neighborX, neighborY, neighborZ),
                generator.IsCaveVoidAt(neighborX, neighborY, neighborZ),
                blocks.GetDefinition(replacement).Id);
        }

        return null;
    }

    private static bool IsSphereShellBoundaryOrLining(
        int worldY,
        DimensionShellDefinition? shell) =>
        (shell?.FloorY is { } floorY &&
         (worldY == floorY || (long)worldY == (long)floorY + 1L)) ||
        (shell?.RoofY is { } roofY &&
         (worldY == roofY || (long)worldY == (long)roofY - 1L));

    private sealed record PaletteDecision(
        string OwnerBiome,
        BiomeRegionOwnerKind OwnerKind,
        BiomePaletteFace Face,
        uint Depth,
        WorldVoxelCoord EmptyNeighbor,
        bool IsCaveVoid,
        string ReplacementBlock);
}
