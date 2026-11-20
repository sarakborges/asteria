using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class LightingDependencyChangesTests
{
    [Fact]
    public void ClassifiesContentAndResidencyChangesWithoutChangingValidation()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var seed = new WorldVoxelCoord(2, 2, 2);
        var dependencies = VoxelLightingSnapshot.Capture(world, [seed]).Dependencies;
        Assert.Equal(LightingDependencyChanges.None, dependencies.DetectChanges(world));
        Assert.True(dependencies.IsCurrent(world));

        // A column outside the snapshot's 3x3 dependency envelope is irrelevant.
        world.InsertChunk(new ChunkCoord(3, 0, 0), new Chunk());
        Assert.Equal(LightingDependencyChanges.None, dependencies.DetectChanges(world));
        Assert.True(dependencies.IsCurrent(world));

        Assert.True(world.SetCellAt(seed,
            new VoxelCell(new BlockRuntimeId(1)), out _));
        Assert.Equal(LightingDependencyChanges.Content,
            dependencies.DetectChanges(world));
        Assert.False(dependencies.IsCurrent(world));

        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        Assert.Equal(
            LightingDependencyChanges.Content | LightingDependencyChanges.Residency,
            dependencies.DetectChanges(world));
        Assert.False(dependencies.IsCurrent(world));
    }

    [Fact]
    public void ClassifiesResidencyChangeInPreviouslyEmptyNeighborColumn()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var dependencies = VoxelLightingSnapshot.Capture(
            world, [new WorldVoxelCoord(1, 1, 1)]).Dependencies;

        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());
        Assert.Equal(LightingDependencyChanges.Residency,
            dependencies.DetectChanges(world));
        Assert.False(dependencies.IsCurrent(world));
    }
}
