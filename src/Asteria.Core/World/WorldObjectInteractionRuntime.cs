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
    private readonly FluidRegistry? _fluids;
    private readonly PackContentRegistry<ObjectDefinition> _definitions;
    private readonly VoxelMutationRuntime _mutations;
    private readonly DroppedBlockRuntime _drops;
    private readonly InventoryContentCatalog? _catalog;
    private readonly Func<ulong> _worldTick;

    public WorldObjectInteractionRuntime(
        VoxelWorld world, BlockRegistry blocks,
        PackContentRegistry<ObjectDefinition> definitions,
        VoxelMutationRuntime mutations, DroppedBlockRuntime drops,
        FluidRegistry? fluids = null,
        InventoryContentCatalog? catalog = null,
        Func<ulong>? worldTick = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _fluids = fluids;
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _drops = drops ?? throw new ArgumentNullException(nameof(drops));
        _catalog = catalog;
        _worldTick = worldTick ?? (() => 0UL);
    }

    public bool TryPlace(InventoryStack? held, VoxelWorldHit supportHit)
    {
        var decision = ObjectPlacementResolver.Resolve(
            _world, _blocks, _definitions, held, supportHit, _fluids);
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

        // Preflight all authored physical stacks before removing the Object.
        IReadOnlyList<InventoryStack> rewards = mode == PlayerGameMode.Survival
            ? LootTableRoller.ResolveObject(
                definition, hit.Object, hit.Support, _catalog, _worldTick())
            : [];
        if (rewards.Count > _drops.AvailableCapacity ||
            !_mutations.RemoveObjectAt(hit.Support, hit.Object))
            return false;

        if (rewards.Count > 0)
        {
            var position = WorldObjectGeometry.Pose(
                hit.Support, hit.Object, definition).Position +
                Vector3.UnitY * .25f;
            foreach (var stack in rewards)
                _drops.Spawn(stack, position);
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

        var stack = new InventoryStack(definition.RewardEntry);
        if (!inventory.CanInsert(stack) ||
            !_mutations.RemoveObjectAt(hit.Support, hit.Object))
            return false;

        if (!inventory.TryInsert(stack))
            throw new InvalidOperationException(
                "Validated Object pickup failed to insert its item.");
        return true;
    }
}
