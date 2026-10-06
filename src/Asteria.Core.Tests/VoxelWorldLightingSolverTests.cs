using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelWorldLightingSolverTests
{
    [Fact]
    public void BlockLightPropagatesAcrossChunkBoundary()
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
        world.SetBlockAt(
            new WorldVoxelCoord(15, 5, 5),
            lamp,
            out _);

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            new FluidRegistry([]));

        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(
                new WorldVoxelCoord(16, 5, 5)).Red);
    }
}


public sealed class VoxelLightingSnapshotTests
{
    [Fact]
    public void SnapshotScopesContentToNearbyHorizontalColumns()
    {
        var world = new VoxelWorld();
        var center = new ChunkCoord(0, 0, 0);
        var above = new ChunkCoord(0, 1, 0);
        var neighbor = new ChunkCoord(1, 0, 0);
        var far = new ChunkCoord(3, 0, 0);

        foreach (var coord in new[] { center, above, neighbor, far })
        {
            world.InsertChunk(coord, new Chunk());
        }

        var snapshot = VoxelLightingSnapshot.Capture(
            world,
            [new WorldVoxelCoord(3, 3, 3)]);

        Assert.True(snapshot.World.ContainsChunk(center));
        Assert.True(snapshot.World.ContainsChunk(above));
        Assert.True(snapshot.World.ContainsChunk(neighbor));
        Assert.False(snapshot.World.ContainsChunk(far));
    }

    [Fact]
    public void UnrelatedContentEditDoesNotInvalidateLightingSnapshot()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        world.InsertChunk(new ChunkCoord(3, 0, 0), new Chunk());

        var snapshot = VoxelLightingSnapshot.Capture(
            world,
            [new WorldVoxelCoord(3, 3, 3)]);

        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(3 * Chunk.Size + 1, 3, 3),
            new BlockRuntimeId(1),
            out _));

        Assert.True(snapshot.Dependencies.IsCurrent(world));
    }

    [Fact]
    public void RelevantContentOrColumnResidencyInvalidatesLightingSnapshot()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        var contentSnapshot = VoxelLightingSnapshot.Capture(
            world,
            [new WorldVoxelCoord(3, 3, 3)]);

        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(4, 3, 3),
            new BlockRuntimeId(1),
            out _));
        Assert.False(contentSnapshot.Dependencies.IsCurrent(world));

        var residencySnapshot = VoxelLightingSnapshot.Capture(
            world,
            [new WorldVoxelCoord(3, 3, 3)]);

        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());

        Assert.False(residencySnapshot.Dependencies.IsCurrent(world));
    }
}
