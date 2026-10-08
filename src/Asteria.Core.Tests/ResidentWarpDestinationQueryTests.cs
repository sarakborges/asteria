using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ResidentWarpDestinationQueryTests
{
    private static VoxelWorld FlatWorld()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        for (var x = 0; x < Chunk.Size; x++)
        for (var z = 0; z < Chunk.Size; z++)
            Assert.True(world.SetBlockAt(
                new WorldVoxelCoord(x, 0, z), new BlockRuntimeId(1), out _));
        return world;
    }

    [Fact]
    public void KeepsGeneratedEntryIfResidentAndUnobstructed()
    {
        var world = FlatWorld();
        Assert.Equal(new Vector3(7.5f, 1f, 8.5f),
            ResidentWarpDestinationQuery.Find(world, new Vector3(7.5f, 1, 8.5f)));
    }

    [Fact]
    public void AvoidsPersistedEditsAndChoosesStableNearbyEntry()
    {
        var world = FlatWorld();
        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(7, 1, 8), new BlockRuntimeId(1), out _));
        var found = ResidentWarpDestinationQuery.Find(world,
            new Vector3(7.5f, 1f, 8.5f));
        Assert.NotNull(found);
        Assert.NotEqual(new Vector3(7.5f, 1f, 8.5f), found.Value);
        Assert.True(world.GetCellOrEmpty(new WorldVoxelCoord(
            (int)found.Value.X, (int)found.Value.Y, (int)found.Value.Z)).IsEmpty);
    }

    [Fact]
    public void RestoredModifiedDestinationUsesAuthoritativeArchivedVoxels()
    {
        var world = FlatWorld();
        var requested = new Vector3(7.5f, 1f, 8.5f);
        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(7, 1, 8), new BlockRuntimeId(1), out _));

        world.ArchiveChunk(ChunkCoord.Zero);
        Assert.Null(ResidentWarpDestinationQuery.Find(world, requested));
        Assert.Equal(ChunkRestoreResult.Restored,
            world.RestoreChunk(ChunkCoord.Zero));

        var resolved = ResidentWarpDestinationQuery.Find(world, requested);
        Assert.NotNull(resolved);
        Assert.NotEqual(requested, resolved.Value);
        Assert.True(world.GetCellOrEmpty(new WorldVoxelCoord(7, 1, 8)).Block.Value != 0);
        Assert.True(world.GetCellOrEmpty(new WorldVoxelCoord(
            (int)resolved.Value.X, (int)resolved.Value.Y,
            (int)resolved.Value.Z)).IsEmpty);
    }

    [Fact]
    public void RejectsFullyBlockedRestoredArrivalInsteadOfSpawningInSolid()
    {
        var world = FlatWorld();
        // The validator can legitimately stand on a newly placed block:
        // obstruct every feet/head level within its vertical search radius,
        // not just the original feet level.
        for (var x = 0; x < Chunk.Size; x++)
        for (var z = 0; z < Chunk.Size; z++)
        for (var y = 1; y <= 5; y++)
            Assert.True(world.SetBlockAt(
                new WorldVoxelCoord(x, y, z), new BlockRuntimeId(1), out _));

        world.ArchiveChunk(ChunkCoord.Zero);
        Assert.Equal(ChunkRestoreResult.Restored,
            world.RestoreChunk(ChunkCoord.Zero));

        Assert.Null(ResidentWarpDestinationQuery.Find(
            world, new Vector3(7.5f, 1f, 8.5f)));
    }

    [Fact]
    public void RejectsMissingResidencyAndNegativeHeight()
    {
        var world = new VoxelWorld();
        Assert.Null(ResidentWarpDestinationQuery.Find(world, new Vector3(3.5f, 1f, 4.5f)));
        Assert.Null(ResidentWarpDestinationQuery.Find(world, new Vector3(3.5f, -1f, 4.5f)));
    }

    [Fact]
    public void RejectsFluidObstructingPreparedEntry()
    {
        var world = FlatWorld();
        Assert.True(world.SetFluidAt(new WorldVoxelCoord(7, 1, 8),
            FluidCell.Source(new FluidRuntimeId(1)), out _));
        var found = ResidentWarpDestinationQuery.Find(world, new Vector3(7.5f, 1, 8.5f));
        Assert.NotNull(found);
        Assert.NotEqual(new Vector3(7.5f, 1, 8.5f), found.Value);
    }
}
