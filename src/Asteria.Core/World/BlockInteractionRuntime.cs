using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

public enum BlockBreakLootPolicy : byte
{
    DropSelf,
    Suppress,
}

public enum BlockPickupResult : byte
{
    Collected = 0,
    UnloadedOrEmpty = 1,
    NotPickupable = 2,
    WrongItem = 3,
    InventoryFull = 4,
    MutationRejected = 5,
}

public sealed class BlockInteractionRuntime
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly DroppedBlockRuntime _droppedBlocks;
    private readonly AttachedLayerRegistry? _layers;
    private readonly InventoryContentCatalog? _inventoryCatalog;
    private readonly SpikeFormationRuntime _spikes;
    private readonly Func<string, WorldVoxelCoord, bool>? _allowBreak;

    public BlockInteractionRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelMutationRuntime mutations,
        DroppedBlockRuntime droppedBlocks,
        AttachedLayerRegistry? layers = null,
        InventoryContentCatalog? inventoryCatalog = null,
        Func<string, WorldVoxelCoord, bool>? allowBreak = null)
    {
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _blocks =
            blocks ??
            throw new ArgumentNullException(nameof(blocks));
        _mutations =
            mutations ??
            throw new ArgumentNullException(nameof(mutations));
        _droppedBlocks =
            droppedBlocks ??
            throw new ArgumentNullException(nameof(droppedBlocks));
        _layers = layers;
        _inventoryCatalog = inventoryCatalog;
        _allowBreak = allowBreak;
        _spikes = new SpikeFormationRuntime(
            _world, _blocks, _mutations, _droppedBlocks);
    }

    public BlockBreakDecision Break(
        VoxelWorldHit hit,
        BlockBreakLootPolicy lootPolicy = BlockBreakLootPolicy.DropSelf,
        ulong worldTick = 0)
    {
        if (!Enum.IsDefined(lootPolicy))
            throw new ArgumentOutOfRangeException(nameof(lootPolicy));

        var decision =
            BlockInteractionResolver.ResolveBreak(
                _world,
                hit);

        if (!decision.Accepted)
        {
            return decision;
        }

        var definition =
            _blocks.GetDefinition(
                decision.Cell.Block);

        if (definition.Interaction == BlockInteractionKind.Pickup)
        {
            return BlockBreakDecision.Reject(
                decision.Position, BlockBreakRejection.PickupOnly);
        }

        if (definition.Mining.Unbreakable)
        {
            return BlockBreakDecision.Reject(
                decision.Position,
                BlockBreakRejection.Unbreakable);
        }

        // This gate runs only for an accepted, breakable target, before any
        // layer/drop/mutation side effects occur.
        if (_allowBreak is not null && !_allowBreak(definition.Id, decision.Position))
            return BlockBreakDecision.Reject(
                decision.Position, BlockBreakRejection.CancelledByMod);

        // A face attachment is mined before the backing voxel. The same
        // authored definition determines its hardness, drops and removal scope.
        if (_layers is not null && VoxelHitFace.TryResolve(hit, out var face))
        {
            var surface = _world.GetBlockSurfaceStateOrEmpty(decision.Position);
            if (surface.TryGetTop(face, out var attached))
            {
                if (!_layers.TryGet(attached.LayerId, out var layer) || layer is null)
                    return BlockBreakDecision.Reject(
                        decision.Position, BlockBreakRejection.MutationRejected);
                if (layer.Mining.Unbreakable)
                    return BlockBreakDecision.Reject(
                        decision.Position, BlockBreakRejection.Unbreakable);

                // Resolve the authored stack limits before removal. A full
                // drop pool must not destroy an ore/moss layer or evict items.
                var layerLoot = lootPolicy == BlockBreakLootPolicy.DropSelf
                    ? ResolveLayerLoot(layer) : null;
                if (layerLoot is not null &&
                    layerLoot.Count > _droppedBlocks.AvailableCapacity)
                    return BlockBreakDecision.Reject(
                        decision.Position, BlockBreakRejection.MutationRejected);

                BlockSurfaceState updated;
                var removed = layer.Scope == LayerRemovalScope.Voxel
                    ? surface.TryRemoveEverywhere(attached.LayerId, out updated)
                    : surface.TryRemoveTop(face, out updated, out _);
                if (!removed || !_mutations.SetBlockSurfaceStateAt(
                        decision.Position, updated, out _))
                    return BlockBreakDecision.Reject(
                        decision.Position, BlockBreakRejection.MutationRejected);

                if (layerLoot is not null)
                    foreach (var stack in layerLoot)
                        _droppedBlocks.Spawn(stack, LayerDropPosition(hit));

                return decision;
            }
        }

        if (definition.Shape.Kind == BlockShapeKind.Spike)
            return _spikes.Break(decision, lootPolicy);

        // Resolve and validate every authored reward before touching the voxel.
        // An explicitly empty table means no drops. Definitions without one
        // preserve their full portable dropsSelf snapshot.
        var authoredLoot = lootPolicy == BlockBreakLootPolicy.DropSelf &&
            definition.LootTable is not null
                ? RollLoot(definition, decision, worldTick)
                : null;
        if (authoredLoot is not null &&
            authoredLoot.Count > _droppedBlocks.AvailableCapacity)
            return BlockBreakDecision.Reject(
                decision.Position, BlockBreakRejection.MutationRejected);

        var snapshot =
            lootPolicy == BlockBreakLootPolicy.DropSelf &&
            definition.LootTable is null && definition.DropsSelf
                ? BlockStateSnapshot.Capture(
                    _world, decision.Position, decision.Cell)
                : null;

        // The host and every dependent spike formation share one physical
        // drop pool. Count *all* immediate drops before destroying anything.
        var hostDropCount = authoredLoot?.Count ?? (snapshot is null ? 0 : 1);
        var objectDropCount = _mutations.ObjectSupportDropCount(
            decision.Position, lootPolicy);
        if (!_spikes.CanRemoveSupport(decision.Position, lootPolicy,
                hostDropCount + objectDropCount))
            return BlockBreakDecision.Reject(
                decision.Position, BlockBreakRejection.MutationRejected);

        if (!_spikes.RemoveSupport(decision.Position, lootPolicy))
            return BlockBreakDecision.Reject(
                decision.Position, BlockBreakRejection.MutationRejected);

        if (authoredLoot is not null && authoredLoot.Count > 0)
        {
            if (!_droppedBlocks.TrySpawnBatch(authoredLoot, CenterOf(decision.Position)))
                throw new InvalidOperationException(
                    "Drop capacity changed during an accepted block removal.");
        }
        else if (snapshot is not null)
            _droppedBlocks.Spawn(snapshot, CenterOf(decision.Position));

        return decision;
    }

    private List<InventoryStack> ResolveLayerLoot(AttachedLayerDefinition layer)
    {
        var stacks = new List<InventoryStack>();
        foreach (var drop in layer.Drops)
        {
            if (_inventoryCatalog is null)
            {
                // Legacy independent Core fixtures can still exercise layers
                // without loading a complete pack; authored default 64 applies.
                stacks.Add(drop.ToStack());
                continue;
            }
            if (!_inventoryCatalog.TryResolve(drop.Kind, drop.Id,
                    null, null, out var entry) || entry is null)
                throw new InvalidOperationException(
                    $"Unknown drop {drop.Id} on layer {layer.Id}.");
            var remaining = drop.Quantity;
            while (remaining > 0)
            {
                var quantity = Math.Min(remaining, entry.MaxStackSize);
                stacks.Add(new InventoryStack(entry, quantity));
                remaining -= quantity;
            }
        }
        return stacks;
    }

    private List<InventoryStack> RollLoot(
        BlockDefinition block, BlockBreakDecision decision, ulong worldTick)
    {
        if (_inventoryCatalog is null)
            throw new InvalidOperationException(
                $"Block {block.Id} has a lootTable but no content catalog is configured.");

        var source = BlockStateSnapshot.Capture(
            _world, decision.Position, decision.Cell);
        return LootTableRoller.Roll(
            block.LootTable!, _inventoryCatalog, decision.Position, worldTick,
            selfEntry: InventoryEntry.FromBlock(block.Id, source));
    }

    /// <summary>
    /// Atomically collects a pickup-only voxel into the authoritative inventory.
    /// No physical dropped entity is created.
    /// </summary>
    public BlockPickupResult Pickup(
        VoxelWorldHit hit,
        PlayerInventory inventory,
        InventoryEntry item)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(item);

        var target = BlockInteractionResolver.ResolveBreak(_world, hit);
        if (!target.Accepted)
            return BlockPickupResult.UnloadedOrEmpty;

        var definition = _blocks.GetDefinition(target.Cell.Block);
        if (definition.Interaction != BlockInteractionKind.Pickup)
            return BlockPickupResult.NotPickupable;

        if (item.Kind != InventoryEntryKind.Item ||
            !string.Equals(item.Id, definition.PickupItemId,
                StringComparison.Ordinal) ||
            item.Metadata.Count != 0)
        {
            return BlockPickupResult.WrongItem;
        }

        var stack = new InventoryStack(item);
        if (!inventory.CanInsert(stack))
            return BlockPickupResult.InventoryFull;

        if (!_mutations.SetCellAt(target.Position, VoxelCell.Empty, out _))
            return BlockPickupResult.MutationRejected;

        if (!inventory.TryInsert(stack))
            throw new InvalidOperationException(
                "Inventory capacity changed inside an accepted pickup mutation.");

        return BlockPickupResult.Collected;
    }

    public BlockPlacementDecision Place(
        VoxelWorldHit hit,
        VoxelCell cell,
        WorldAabb playerBounds)
    {
        if (!cell.IsEmpty &&
            _blocks.GetDefinition(cell.Block).HasTag(TreeBlockTags.DecayableLeaf))
            cell = cell.WithPlayerPlaced(true);

        var spike = !cell.IsEmpty &&
            _blocks.GetDefinition(cell.Block).Shape.Kind == BlockShapeKind.Spike;
        if (spike)
        {
            // Picked-up/generated segment metadata must never leak into a
            // newly placed formation.
            cell = new VoxelCell(
                cell.Block, cell.TextureRotation, cell.Orientation,
                cell.Facing,
                SpikeSegmentState.Encode(0, 1, hit.NormalY < 0));
        }

        var decision =
            BlockInteractionResolver.ResolvePlacement(
                _world,
                _blocks,
                hit,
                cell,
                playerBounds);

        if (!decision.Accepted)
        {
            return decision;
        }

        if (spike)
            return _spikes.Place(decision, hit.NormalY < 0);

        if (!_mutations.SetCellAt(
                decision.Position,
                decision.Cell,
                out _))
        {
            return BlockPlacementDecision.Reject(
                decision.Position,
                decision.Cell,
                BlockPlacementRejection.MutationRejected);
        }

        return decision;
    }

    // The supporting block stays solid; put loot beyond the mined face
    // so the entity doesn't spawn embedded in its collision geometry.
    private static Vector3 LayerDropPosition(VoxelWorldHit hit)
    {
        const float clearance = 0.04f;
        var distance = 0.5f + DroppedBlockRuntime.HalfExtent + clearance;
        return CenterOf(hit.Voxel) + new Vector3(
            hit.NormalX * distance,
            hit.NormalY * distance,
            hit.NormalZ * distance);
    }

    private static Vector3 CenterOf(
        WorldVoxelCoord position) =>
        new(
            position.X + 0.5f,
            position.Y + 0.5f,
            position.Z + 0.5f);
}
