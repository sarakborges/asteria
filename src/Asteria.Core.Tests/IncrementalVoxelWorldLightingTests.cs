using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class IncrementalVoxelWorldLightingTests
{
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
        VoxelWorldLightingSolver.Initialize(world, blocks);
        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(
                new WorldVoxelCoord(16, 5, 5)).Red);

        world.SetBlockAt(source, BlockRuntimeId.Air, out _);
        var update =
            VoxelWorldLightingSolver.RelightAfterEdits(
                world,
                blocks,
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

        VoxelWorldLightingSolver.Initialize(world, blocks);

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
            [edited]);

        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(edited).Sky);
        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(below).Sky);
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

        VoxelWorldLightingSolver.Initialize(world, blocks);

        var distantBefore =
            world.GetLightOrDark(
                new WorldVoxelCoord(64, 7, 7));

        var edited = new WorldVoxelCoord(2, 8, 2);
        world.SetBlockAt(edited, stone, out _);
        var update =
            VoxelWorldLightingSolver.RelightAfterEdits(
                world,
                blocks,
                [edited]);

        Assert.Equal(
            distantBefore,
            world.GetLightOrDark(
                new WorldVoxelCoord(64, 7, 7)));
        Assert.DoesNotContain(
            update.ChangedPositions,
            position => position.X >= 64);
    }
}
