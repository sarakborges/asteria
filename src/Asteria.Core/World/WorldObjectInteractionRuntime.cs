using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Canonical world Object interaction owner. The Object's attached support
/// voxel remains unchanged; placement, removal and drops flow through the
/// existing mutation and dropped-item owners.
/// </summary>
public sealed class WorldObjectInteractionRuntime
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly PackContentRegistry<ObjectDefinition> _definitions;
    private readonly VoxelMutationRuntime _mutations;
    private readonly DroppedBlockRuntime _drops;

    public WorldObjectInteractionRuntime(
        VoxelWorld world, BlockRegistry blocks,
        PackContentRegistry<ObjectDefinition> definitions,
        VoxelMutationRuntime mutations, DroppedBlockRuntime drops)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _drops = drops ?? throw new ArgumentNullException(nameof(drops));
    }

    public bool TryPlace(InventoryStack? held, VoxelWorldHit supportHit)
    {
        var decision = ObjectPlacementResolver.Resolve(
            _world, _blocks, _definitions, held, supportHit);
        return decision.Accepted &&
            _mutations.AddObjectAt(decision.Support, decision.Object);
    }

    public bool TryBreak(WorldObjectHit hit, PlayerGameMode mode)
    {
        if (!_definitions.TryGet(hit.Object.Id, out var definition) ||
            definition is null ||
            definition.Interaction != ObjectInteraction.Break ||
            !_world.TryGetObjects(hit.Support, out var resident) ||
            !resident.Contains(hit.Object))
            return false;

        var spawn = mode == PlayerGameMode.Survival && definition.DropSelf;
        if (spawn && _drops.AvailableCapacity == 0)
            return false;
        if (!_mutations.RemoveObjectAt(hit.Support, hit.Object))
            return false;

        if (spawn)
        {
            var position = WorldObjectGeometry.Pose(
                hit.Support, hit.Object, definition).Position;
            _drops.Spawn(
                new InventoryStack(InventoryEntry.FromObject(definition.Id)),
                position + Vector3.UnitY * .25f);
        }
        return true;
    }

    public bool TryPickup(WorldObjectHit hit, PlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!_definitions.TryGet(hit.Object.Id, out var definition) ||
            definition is null ||
            definition.Interaction != ObjectInteraction.Pickup ||
            !_world.TryGetObjects(hit.Support, out var resident) ||
            !resident.Contains(hit.Object))
            return false;

        var stack = new InventoryStack(InventoryEntry.FromObject(definition.Id));
        if (!inventory.CanInsert(stack) ||
            !_mutations.RemoveObjectAt(hit.Support, hit.Object))
            return false;

        if (!inventory.TryInsert(stack))
            throw new InvalidOperationException(
                "Validated Object pickup failed to insert its item.");
        return true;
    }
}
