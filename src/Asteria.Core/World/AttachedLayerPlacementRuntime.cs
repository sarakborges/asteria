namespace Asteria.Core.World;

/// <summary>
/// Authoritative placement policy for inventory-owned face layers. All world
/// consequences are delegated to the voxel mutation runtime.
/// </summary>
public sealed class AttachedLayerPlacementRuntime
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly AttachedLayerRegistry _layers;
    private readonly VoxelMutationRuntime _mutations;

    public AttachedLayerPlacementRuntime(
        VoxelWorld world, BlockRegistry blocks,
        AttachedLayerRegistry layers, VoxelMutationRuntime mutations)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
    }

    public bool TryPlace(InventoryStack? selected, VoxelWorldHit hit)
    {
        if (selected?.Kind != InventoryEntryKind.Layer ||
            !_layers.TryGet(selected.Id, out var definition) || definition is null ||
            !VoxelHitFace.TryResolve(hit, out var face) ||
            !definition.Supports(face) ||
            !_world.TryGetCell(hit.Voxel, out var cell) || cell.IsEmpty)
            return false;

        var host = _blocks.GetDefinition(cell.Block);
        if (host.Visual.Kind != BlockVisualKind.Geometry || host.Mining.Unbreakable)
            return false;

        var surface = _world.GetBlockSurfaceStateOrEmpty(hit.Voxel);
        if (!surface.TryAttach(new AttachedBlockLayer(face, definition.Id), out var next))
            return false;

        return _mutations.SetBlockSurfaceStateAt(hit.Voxel, next, out _);
    }
}
