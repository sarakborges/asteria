using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelMutationRuntimeTests
{
    [Fact]
    public void BlockEditQueuesTerrainFluidLightingAndFluidTopologyTogether()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates = new FluidMeshUpdateQueue();
        var blockGravityUpdates = new BlockGravityUpdateQueue();
        var terrainRevisions = new MeshletContentRevisions();
        var fluidRevisions = new MeshletContentRevisions();
        var runtime = new VoxelMutationRuntime(
            world,
            worldUpdates,
            fluidUpdates,
            fluidMeshUpdates,
            blockGravityUpdates,
            terrainRevisions,
            fluidRevisions);
        var position = new WorldVoxelCoord(3, 3, 3);
        var key = new ChunkMeshletKey(ChunkCoord.Zero, 0);

        Assert.True(runtime.SetBlockAt(
            position,
            new BlockRuntimeId(1),
            out var edit));

        Assert.Equal(position, edit.Position);
        Assert.True(worldUpdates.HasLightingWork);
        Assert.True(worldUpdates.HasMeshWork);
        Assert.Equal(7, fluidUpdates.TopologyCount);
        Assert.True(fluidMeshUpdates.HasWork);
        Assert.Equal(2, blockGravityUpdates.Count);
        Assert.True(terrainRevisions.Get(key) > 0);
        Assert.True(fluidRevisions.Get(key) > 0);
    }

    [Fact]
    public void NoOpBlockEditDoesNotWakeDerivedSystemsAgain()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates = new FluidMeshUpdateQueue();
        var blockGravityUpdates = new BlockGravityUpdateQueue();
        var terrainRevisions = new MeshletContentRevisions();
        var fluidRevisions = new MeshletContentRevisions();
        var runtime = new VoxelMutationRuntime(
            world,
            worldUpdates,
            fluidUpdates,
            fluidMeshUpdates,
            blockGravityUpdates,
            terrainRevisions,
            fluidRevisions);
        var position = new WorldVoxelCoord(3, 3, 3);
        var block = new BlockRuntimeId(1);
        var key = new ChunkMeshletKey(ChunkCoord.Zero, 0);

        Assert.True(runtime.SetBlockAt(position, block, out _));
        worldUpdates.DrainLighting();
        worldUpdates.DrainMeshlets();
        fluidUpdates.DrainReady(0, 32);
        fluidMeshUpdates.Drain();
        blockGravityUpdates.DrainBatch();
        var terrainRevision = terrainRevisions.Get(key);
        var fluidRevision = fluidRevisions.Get(key);

        Assert.False(runtime.SetBlockAt(position, block, out _));

        Assert.False(worldUpdates.HasWork);
        Assert.False(fluidUpdates.HasReadyWork(0));
        Assert.False(fluidMeshUpdates.HasWork);
        Assert.Equal(0, blockGravityUpdates.Count);
        Assert.Equal(terrainRevision, terrainRevisions.Get(key));
        Assert.Equal(fluidRevision, fluidRevisions.Get(key));
    }
}
