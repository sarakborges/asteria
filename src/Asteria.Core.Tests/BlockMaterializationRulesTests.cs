using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockMaterializationRulesTests
{
    [Fact]
    public void EmptyLoadedVoxelIsAvailable()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        Assert.Equal(
            BlockMaterializationState.Available,
            BlockMaterializationRules.Evaluate(
                world,
                new WorldVoxelCoord(
                    2,
                    2,
                    2)));
    }

    [Fact]
    public void PartialGeometryStillOwnsItsVoxelCell()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(
                    2,
                    2,
                    2),
                new BlockRuntimeId(1),
                out _));

        Assert.Equal(
            BlockMaterializationState.Occupied,
            BlockMaterializationRules.Evaluate(
                world,
                new WorldVoxelCoord(
                    2,
                    2,
                    2)));
    }

    [Fact]
    public void MissingChunkAndBelowWorldRemainDistinct()
    {
        var world = new VoxelWorld();

        Assert.Equal(
            BlockMaterializationState.Unloaded,
            BlockMaterializationRules.Evaluate(
                world,
                new WorldVoxelCoord(
                    2,
                    2,
                    2)));
        Assert.Equal(
            BlockMaterializationState.BelowWorld,
            BlockMaterializationRules.Evaluate(
                world,
                new WorldVoxelCoord(
                    2,
                    -1,
                    2)));
    }
}
