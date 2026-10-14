using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class IncrementalVoxelWorldLightingTests
{
    private static void AssertMatchesFullLighting(
        VoxelWorld world, BlockRegistry blocks, FluidRegistry fluids)
    {
        var expected = world.CloneForWorker();
        VoxelWorldLightingSolver.Initialize(expected, blocks, fluids);
        foreach (var coord in world.LoadedChunkCoords)
        {
            var (ox, oy, oz) = VoxelCoordinates.ChunkOrigin(coord);
            for (var y = 0; y < Chunk.Size; y++)
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
            {
                var position = new WorldVoxelCoord(ox + x, oy + y, oz + z);
                Assert.Equal(expected.GetLightOrDark(position),
                    world.GetLightOrDark(position));
            }
        }
    }

    [Fact]
    public void RemovingEmissionExtinguishesAcrossChunkBoundaryIncrementally()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:red_lamp",
                lightDampening: 0,
                lightEmission: new BlockLightEmission(15, 0, 0)),
        ]);
        var lamp = blocks.GetId("asteria:red_lamp");
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        var source = new WorldVoxelCoord(15, 5, 5);

        world.SetBlockAt(source, lamp, out _);
        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            new FluidRegistry([]));
        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(
                new WorldVoxelCoord(16, 5, 5)).Red);

        world.SetBlockAt(source, BlockRuntimeId.Air, out _);
        var update =
            VoxelWorldLightingSolver.RelightAfterEdits(
                world,
                blocks,
                new FluidRegistry([]),
                [source]);

        Assert.Equal(
            (byte)0,
            world.GetLightOrDark(source).Red);
        Assert.Equal(
            (byte)0,
            world.GetLightOrDark(
                new WorldVoxelCoord(16, 5, 5)).Red);
        Assert.Contains(source, update.ChangedPositions);
    }

    [Fact]
    public void OpaquePlacementAndRemovalUpdateDirectSkyWithoutFullWorldReset()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:stone",
                lightDampening: 15),
        ]);
        var stone = blocks.GetId("asteria:stone");
        var world = new VoxelWorld();

        for (var x = 0; x < 3; x++)
        {
            world.InsertChunk(
                new ChunkCoord(x, 0, 0),
                new Chunk());
        }

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            new FluidRegistry([]));

        var edited = new WorldVoxelCoord(8, 10, 8);
        var below = new WorldVoxelCoord(8, 9, 8);
        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(below).Sky);

        world.SetBlockAt(edited, stone, out _);
        var darken =
            VoxelWorldLightingSolver.RelightAfterEdits(
                world,
                blocks,
                new FluidRegistry([]),
                [edited]);

        Assert.Equal(
            (byte)0,
            world.GetLightOrDark(edited).Sky);
        Assert.True(
            world.GetLightOrDark(below).Sky < 15);
        Assert.True(
            darken.ProcessedVoxelCount <
            world.ChunkCount * Chunk.Volume);

        world.SetBlockAt(
            edited,
            BlockRuntimeId.Air,
            out _);
        VoxelWorldLightingSolver.RelightAfterEdits(
            world,
            blocks,
            new FluidRegistry([]),
            [edited]);

        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(edited).Sky);
        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(below).Sky);
    }

    [Fact]
    public void RemovingOneOfTwoEmittersMatchesFullRecomputation()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:lamp",
                lightDampening: 0,
                lightEmission: new BlockLightEmission(15, 8, 0)),
        ]);
        var fluids = new FluidRegistry([]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        var lamp = blocks.GetId("asteria:lamp");
        var first = new WorldVoxelCoord(14, 5, 5);
        var second = new WorldVoxelCoord(19, 5, 5);
        world.SetBlockAt(first, lamp, out _);
        world.SetBlockAt(second, lamp, out _);
        VoxelWorldLightingSolver.Initialize(world, blocks, fluids);
        world.SetBlockAt(first, BlockRuntimeId.Air, out _);

        var update = VoxelWorldLightingSolver.RelightAfterEdits(
            world, blocks, fluids, [first]);
        Assert.NotEmpty(update.ChangedPositions);
        Assert.Equal((byte)15, world.GetLightOrDark(second).Red);
        AssertMatchesFullLighting(world, blocks, fluids);
    }

    [Fact]
    public void OcclusionAcrossVerticalChunksMatchesFullRecomputation()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone", lightDampening: 15),
        ]);
        var fluids = new FluidRegistry([]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());
        VoxelWorldLightingSolver.Initialize(world, blocks, fluids);
        var roof = new WorldVoxelCoord(8, 18, 8);
        world.SetBlockAt(roof, blocks.GetId("asteria:stone"), out _);

        var update = VoxelWorldLightingSolver.RelightAfterEdits(
            world, blocks, fluids, [roof]);
        Assert.NotEmpty(update.ChangedPositions);
        AssertMatchesFullLighting(world, blocks, fluids);
    }

    [Fact]
    public void UnrelatedDistantChunkKeepsItsLightFieldUntouched()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:stone",
                lightDampening: 15),
        ]);
        var stone = blocks.GetId("asteria:stone");
        var world = new VoxelWorld();
        world.InsertChunk(
            new ChunkCoord(0, 0, 0),
            new Chunk());
        world.InsertChunk(
            new ChunkCoord(4, 0, 0),
            new Chunk());

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            new FluidRegistry([]));

        var distantBefore =
            world.GetLightOrDark(
                new WorldVoxelCoord(64, 7, 7));

        var edited = new WorldVoxelCoord(2, 8, 2);
        world.SetBlockAt(edited, stone, out _);
        var update =
            VoxelWorldLightingSolver.RelightAfterEdits(
                world,
                blocks,
                new FluidRegistry([]),
                [edited]);

        Assert.Equal(
            distantBefore,
            world.GetLightOrDark(
                new WorldVoxelCoord(64, 7, 7)));
        Assert.DoesNotContain(
            update.ChangedPositions,
            position => position.X >= 64);
    }
    [Fact]
    public void RelightChangesStayGroupedByChunkForBoundedMeshPublication()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:lamp",
                lightDampening: 0,
                lightEmission: new BlockLightEmission(15, 0, 0)),
        ]);
        var fluids = new FluidRegistry([]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        var lamp = blocks.GetId("asteria:lamp");
        var left = new WorldVoxelCoord(4, 4, 4);
        var right = new WorldVoxelCoord(20, 4, 4);
        world.SetBlockAt(left, lamp, out _);
        world.SetBlockAt(right, lamp, out _);
        VoxelWorldLightingSolver.Initialize(world, blocks, fluids);

        world.SetBlockAt(left, BlockRuntimeId.Air, out _);
        world.SetBlockAt(right, BlockRuntimeId.Air, out _);
        var changed = VoxelWorldLightingSolver.RelightAfterEdits(
            world, blocks, fluids, [left, right]).ChangedPositions;
        var chunks = changed.Select(position =>
            VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z).Chunk).ToArray();
        Assert.Contains(ChunkCoord.Zero, chunks);
        Assert.Contains(new ChunkCoord(1, 0, 0), chunks);
        var transitions = chunks.Where((chunk, index) =>
            index == 0 || chunk != chunks[index - 1]).ToArray();
        Assert.Equal(chunks.Distinct().ToArray(), transitions);
    }

}
