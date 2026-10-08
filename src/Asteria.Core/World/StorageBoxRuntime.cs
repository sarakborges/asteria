namespace Asteria.Core.World;

/// <summary>Immutable presentation snapshot. No browser or adapter owns slots.</summary>
public sealed record StorageBoxSnapshot(
    WorldVoxelCoord Position,
    InventoryStack?[] Slots,
    ulong Revision);

/// <summary>
/// Sphere-local, position-keyed inventory. Slots are retained independently
/// of chunk residency and can only be transferred through this owner.
/// </summary>
public sealed class StorageBoxRuntime
{
    public const int SlotCount = 27;
    public const string BlockId = "asteria:storage_box";
    private readonly Dictionary<WorldVoxelCoord, InventoryStack?[]> _boxes = [];
    private WorldVoxelCoord? _active;
    private ulong _revision;

    public WorldVoxelCoord? ActivePosition => _active;
    public ulong Revision => _revision;
    public int Count => _boxes.Count;

    public bool TryOpen(
        WorldVoxelCoord position, VoxelWorld world, BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!IsActualBox(position, world, blocks))
            return false;

        if (!_boxes.ContainsKey(position))
            _boxes.Add(position, new InventoryStack?[SlotCount]);
        if (_active == position) return true;
        _active = position;
        _revision++;
        return true;
    }

    public void Close()
    {
        if (_active is null) return;
        _active = null;
        _revision++;
    }

    /// <summary>Closes a modal whose voxel is no longer resident or no longer
    /// contains the original container. Archived contents are not drained.</summary>
    public bool CloseIfUnavailable(VoxelWorld world, BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        if (_active is not { } position || IsActualBox(position, world, blocks))
            return false;
        Close();
        return true;
    }

    public StorageBoxSnapshot? CaptureActive()
    {
        if (_active is not { } position ||
            !_boxes.TryGetValue(position, out var slots))
            return null;
        return new StorageBoxSnapshot(position, slots.ToArray(), _revision);
    }

    /// <summary>One in-process transactional cursor/slot exchange.
    /// Invalid indices and unopened storage never consume the cursor.</summary>
    public bool TryClickActive(
        int index, PlayerInventory player, VoxelWorld world,
        BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        if (index is < 0 or >= SlotCount || _active is not { } position ||
            !_boxes.TryGetValue(position, out var slots) ||
            !IsActualBox(position, world, blocks))
            return false;

        if (!player.ClickExternalSlot(slots[index], out var replacement))
            return false;
        slots[index] = replacement;
        _revision++;
        return true;
    }

    public bool TryInsertActive(
        InventoryStack incoming, VoxelWorld world, BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        if (_active is not { } position ||
            !_boxes.TryGetValue(position, out var slots) ||
            !IsActualBox(position, world, blocks))
            return false;

        var capacity = 0;
        foreach (var slot in slots)
        {
            if (slot is null)
                capacity += incoming.MaxStackSize;
            else if (slot.CanStackWith(incoming))
                capacity += slot.MaxStackSize - slot.Quantity;
        }
        if (capacity < incoming.Quantity) return false;

        var remaining = incoming.Quantity;
        for (var phase = 0; phase < 2; phase++)
        for (var i = 0; i < slots.Length && remaining > 0; i++)
        {
            var slot = slots[i];
            if (phase == 0 && (slot is null || !slot.CanStackWith(incoming)))
                continue;
            if (phase == 1 && slot is not null) continue;
            var moved = Math.Min(
                incoming.MaxStackSize - (slot?.Quantity ?? 0), remaining);
            if (moved == 0) continue;
            slots[i] = incoming.WithQuantity((slot?.Quantity ?? 0) + moved);
            remaining -= moved;
        }
        if (remaining != 0)
            throw new InvalidOperationException(
                "Reserved storage box capacity could not accommodate the stack.");
        _revision++;
        return true;
    }

    /// <summary>Block removal drains exactly once. Call from the canonical
    /// world mutation lifecycle; never infer removals from unloaded chunks.</summary>
    public IReadOnlyList<InventoryStack> Drain(WorldVoxelCoord position)
    {
        if (_active == position) Close();
        if (!_boxes.Remove(position, out var slots))
            return Array.Empty<InventoryStack>();

        _revision++;
        return slots.Where(stack => stack is not null)
            .Select(stack => stack!).ToArray();
    }

    private static bool IsActualBox(
        WorldVoxelCoord position, VoxelWorld world, BlockRegistry blocks) =>
        position.Y >= 0 && world.IsLoadedAt(position) &&
        blocks.TryGetId(BlockId, out var id) &&
        world.GetCellOrEmpty(position).Block == id;

    /// <summary>Deterministic occupied positions for session serialization.</summary>
    public IReadOnlyList<StorageBoxSnapshot> CaptureOccupied() =>
        _boxes
            .Where(pair => pair.Value.Any(stack => stack is not null))
            .OrderBy(pair => pair.Key.X)
            .ThenBy(pair => pair.Key.Z)
            .ThenBy(pair => pair.Key.Y)
            .Select(pair => new StorageBoxSnapshot(
                pair.Key, pair.Value.ToArray(), _revision))
            .ToArray();
}
