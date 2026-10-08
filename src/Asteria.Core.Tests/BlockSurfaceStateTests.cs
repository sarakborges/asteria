using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockSurfaceStateTests
{
    private static VoxelMutationRuntime Mutations(
        VoxelWorld world,
        WorldUpdateQueue updates,
        MeshletContentRevisions revisions) =>
        new(world, updates, new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            revisions, new MeshletContentRevisions());

    [Fact]
    public void LayersAreOrderedPerFaceAndTheirCollectionsAreImmutable()
    {
        var red = new AttachedBlockLayer(BlockFace.Top, "asteria:moss");
        var blue = new AttachedBlockLayer(BlockFace.Front, "asteria:moss");
        var green = new AttachedBlockLayer(BlockFace.Top, "asteria:ivy");
        var state = BlockSurfaceState.Empty.WithDye("asteria:red");
        Assert.True(state.TryAttach(red, out state));
        Assert.True(state.TryAttach(blue, out state));
        Assert.True(state.TryAttach(green, out state));
        Assert.False(state.TryAttach(red, out _));
        Assert.Equal(3, state.Layers.Count);
        Assert.Equal("asteria:red", state.DyeId);
        Assert.True(state.TryRemoveTop(BlockFace.Top, out var removedState, out var removed));
        Assert.Equal(green, removed);
        Assert.Equal(new[] { red, blue }, removedState.Layers);
        Assert.False(removedState.TryRemoveTop(BlockFace.Bottom, out _, out _));
        Assert.Equal(3, state.Layers.Count);
        Assert.Equal(state, new BlockSurfaceState("asteria:red", [red, blue, green]));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<AttachedBlockLayer>)state.Layers)[0] = green);
    }

    [Fact]
    public void SurfaceMutationIsSparsePortableAndRemeshOnly()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        var updates = new WorldUpdateQueue();
        var revisions = new MeshletContentRevisions();
        var mutations = Mutations(world, updates, revisions);
        var source = new WorldVoxelCoord(3, 3, 3);
        var target = new WorldVoxelCoord(19, 3, 3);
        var block = new BlockRuntimeId(1);
        var surface = new BlockSurfaceState("asteria:red",
            [new AttachedBlockLayer(BlockFace.Top, "asteria:moss")]);

        Assert.False(mutations.SetBlockSurfaceStateAt(source, surface, out _));
        Assert.True(mutations.SetBlockAt(source, block, out _));
        updates.DrainLighting();
        updates.DrainMeshlets();

        Assert.True(mutations.SetBlockSurfaceStateAt(source, surface, out var edit));
        Assert.Equal(block, edit.Current.Block);
        Assert.False(mutations.SetBlockSurfaceStateAt(source, surface, out _));
        Assert.False(updates.HasLightingWork);
        Assert.True(updates.HasPriorityMeshWork);
        Assert.True(revisions.Get(new ChunkMeshletKey(ChunkCoord.Zero, 0)) > 0);

        var copied = world.GetChunk(ChunkCoord.Zero).CloneForWorker();
        Assert.Equal(surface, copied.GetSurfaceState(3, 3, 3));
        var snapshot = BlockStateSnapshot.Capture(world, source, world.GetCellOrEmpty(source));
        Assert.Equal(surface, snapshot.SurfaceState);
        Assert.True(mutations.SetBlockStateAt(target, snapshot, out _));
        Assert.Equal(surface, world.GetBlockSurfaceStateOrEmpty(target));

        Assert.True(mutations.SetBlockAt(source, new BlockRuntimeId(2), out _));
        Assert.True(world.GetBlockSurfaceStateOrEmpty(source).IsEmpty);
        Assert.Equal(surface, world.GetBlockSurfaceStateOrEmpty(target));
        Assert.True(mutations.SetCellAt(target, VoxelCell.Empty, out _));
        Assert.True(world.GetBlockSurfaceStateOrEmpty(target).IsEmpty);
    }

    [Fact]
    public void SurfaceStateRejectsAirAndDuplicateAttachments()
    {
        Assert.Throws<ArgumentException>(() =>
            new BlockSurfaceState(null,
                [new AttachedBlockLayer(BlockFace.Top, "asteria:moss"),
                 new AttachedBlockLayer(BlockFace.Top, "asteria:moss")]));
        Assert.Throws<ArgumentException>(() =>
            new BlockSurfaceState("invalid"));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = Mutations(world, new WorldUpdateQueue(), new MeshletContentRevisions());
        Assert.False(mutations.SetBlockSurfaceStateAt(
            new WorldVoxelCoord(0, 0, 0),
            new BlockSurfaceState("asteria:red"), out _));
    }
}
