using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class AttachedLayerPlacementRuntimeTests
{
    private static (
        AttachedLayerPlacementRuntime Runtime,
        VoxelWorld World,
        VoxelMutationRuntime Mutations,
        BlockRegistry Blocks,
        WorldUpdateQueue Updates) Setup()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone", tags: ["fragmentable"]),
            new BlockDefinition("asteria:protected", mining:
                new BlockMiningDefinition(unbreakable: true)),
            new BlockDefinition("asteria:grass", visual:
                BlockVisualDefinition.CrossedSprite(
                    new BlockTextureLayer("textures/grass.png")),
                isCollidable: false)
        ]);
        var layers = AttachedLayerRegistry.FromJson([
            """
            {"id":"asteria:moss","texture":"textures/layers/moss.png",
             "faces":["top","front"]}
            """,
            """
            {"id":"asteria:ivy","texture":"textures/layers/ivy.png",
             "faces":["top"]}
            """
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var updates = new WorldUpdateQueue();
        var mutations = new VoxelMutationRuntime(
            world, updates, new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        return (new AttachedLayerPlacementRuntime(world, blocks, layers, mutations),
            world, mutations, blocks, updates);
    }

    [Fact]
    public void PlacingAndRemovingAreOrderedFaceSpecificAndPreserveDye()
    {
        var (runtime, world, mutations, blocks, updates) = Setup();
        var position = new WorldVoxelCoord(2, 3, 4);
        var hit = new VoxelWorldHit(position, 0, 1, 0);
        var stone = blocks.GetId("asteria:stone");
        Assert.True(mutations.SetBlockAt(position, stone, out _));
        updates.DrainMeshlets();
        updates.DrainLighting();
        Assert.True(mutations.SetBlockSurfaceStateAt(
            position, new BlockSurfaceState("asteria:red"), out _));
        updates.DrainMeshlets();

        var moss = new InventoryStack(InventoryEntry.FromLayer("asteria:moss"), 3);
        var ivy = new InventoryStack(InventoryEntry.FromLayer("asteria:ivy"), 3);
        Assert.True(runtime.TryPlace(moss, hit));
        Assert.True(runtime.TryPlace(ivy, hit));
        Assert.False(runtime.TryPlace(moss, hit));
        Assert.False(runtime.TryPlace(ivy,
            new VoxelWorldHit(position, 0, 0, 1)));
        Assert.False(runtime.TryPlace(moss,
            new VoxelWorldHit(position, 0, 0, 0)));
        Assert.False(updates.HasLightingWork);
        Assert.True(updates.HasPriorityMeshWork);
        var state = world.GetBlockSurfaceStateOrEmpty(position);
        Assert.Equal("asteria:red", state.DyeId);
        Assert.Equal(new[] { "asteria:moss", "asteria:ivy" },
            state.Layers.Select(layer => layer.LayerId));

        Assert.True(state.TryRemoveTop(BlockFace.Top, out var after, out var removed));
        Assert.Equal("asteria:ivy", removed.LayerId);
        Assert.True(mutations.SetBlockSurfaceStateAt(position, after, out _));
        Assert.Single(world.GetBlockSurfaceStateOrEmpty(position).Layers);
    }

    [Fact]
    public void InvalidUnloadedAirAndUnbreakableTargetsNeverMutate()
    {
        var (runtime, world, mutations, blocks, _) = Setup();
        var moss = new InventoryStack(InventoryEntry.FromLayer("asteria:moss"));
        var hit = new VoxelWorldHit(new WorldVoxelCoord(2, 3, 4), 0, 1, 0);
        Assert.False(runtime.TryPlace(moss, hit));
        Assert.False(runtime.TryPlace(moss,
            new VoxelWorldHit(new WorldVoxelCoord(160, 3, 4), 0, 1, 0)));
        Assert.True(mutations.SetBlockAt(hit.Voxel,
            blocks.GetId("asteria:protected"), out _));
        Assert.False(runtime.TryPlace(moss, hit));
        Assert.True(mutations.SetBlockAt(hit.Voxel,
            blocks.GetId("asteria:grass"), out _));
        Assert.False(runtime.TryPlace(moss, hit));
        Assert.False(runtime.TryPlace(
            new InventoryStack(InventoryEntry.FromItem("asteria:moss")), hit));
        Assert.True(world.GetBlockSurfaceStateOrEmpty(hit.Voxel).IsEmpty);
    }

    [Fact]
    public void LayerLimitIsEnforcedWithoutMutatingWorld()
    {
        var (runtime, world, mutations, blocks, _) = Setup();
        var position = new WorldVoxelCoord(2, 3, 4);
        Assert.True(mutations.SetBlockAt(position,
            blocks.GetId("asteria:stone"), out _));
        var initial = new BlockSurfaceState(null,
            Enumerable.Range(0, BlockSurfaceState.MaximumAttachedLayers)
                .Select(i => new AttachedBlockLayer(BlockFace.Top, $"asteria:layer_{i}")));
        Assert.True(mutations.SetBlockSurfaceStateAt(position, initial, out _));
        Assert.False(runtime.TryPlace(
            new InventoryStack(InventoryEntry.FromLayer("asteria:moss")),
            new VoxelWorldHit(position, 0, 1, 0)));
        Assert.Equal(initial, world.GetBlockSurfaceStateOrEmpty(position));
    }
}
