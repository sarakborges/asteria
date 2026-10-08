namespace Asteria.Core.World;

/// <summary>Portable blocks are stacked by complete authored voxel state.</summary>
public sealed record InventoryBlockStack
{
    public const int MaxQuantity = 64;

    public InventoryBlockStack(BlockStateSnapshot block, int quantity = 1)
    {
        Block = block ?? throw new ArgumentNullException(nameof(block));
        if (quantity is < 1 or > MaxQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Quantity = quantity;
    }

    public BlockStateSnapshot Block { get; }
    public int Quantity { get; }

    public bool CanStackWith(InventoryBlockStack other) =>
        Block == other.Block;

    public InventoryBlockStack WithQuantity(int quantity) => new(Block, quantity);
}

public sealed record PlayerInventorySnapshot(
    int SelectedSlot,
    InventoryBlockStack?[] Backpack,
    InventoryBlockStack?[] Hotbar,
    InventoryBlockStack? Cursor);

/// <summary>
/// One player-owned inventory across Spheres, with deterministic slot priority:
/// merge into existing hotbar, then backpack, then first free hotbar/backpack.
/// Inventory cursor is authoritative, and no drop disappears unless accepted.
/// </summary>
public sealed class PlayerInventory
{
    public const int BackpackSlots = 27;
    public const int HotbarSlots = 9;
    public const int TotalSlots = BackpackSlots + HotbarSlots;

    private readonly InventoryBlockStack?[] _slots = new InventoryBlockStack?[TotalSlots];

    public int SelectedSlot { get; private set; }
    public InventoryBlockStack? Cursor { get; private set; }
    public ulong Revision { get; private set; }

    public InventoryBlockStack? SelectedStack => _slots[BackpackSlots + SelectedSlot];

    public InventoryBlockStack? SlotAt(int index)
    {
        CheckIndex(index);
        return _slots[index];
    }

    public PlayerInventorySnapshot Capture() =>
        new(
            SelectedSlot,
            _slots.Take(BackpackSlots).ToArray(),
            _slots.Skip(BackpackSlots).ToArray(),
            Cursor);

    public bool SelectHotbar(int index)
    {
        if (index is < 0 or >= HotbarSlots)
            return false;
        if (index == SelectedSlot)
            return false;
        SelectedSlot = index;
        Revision++;
        return true;
    }

    public bool TryInsert(BlockStateSnapshot block) =>
        TryInsert(new InventoryBlockStack(block));

    public bool TryInsert(InventoryBlockStack incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        // A whole dropped stack must fit, or no item is collected.
        if (AvailableCapacity(incoming.Block) < incoming.Quantity)
            return false;
        var remaining = incoming.Quantity;
        for (var phase = 0; phase < 2; phase++)
        {
            foreach (var index in InsertionOrder())
            {
                var slot = _slots[index];
                if (phase == 0 && (slot is null ||
                                   !slot.CanStackWith(incoming)))
                    continue;
                if (phase == 1 && slot is not null)
                    continue;
                var movable = Math.Min(
                    InventoryBlockStack.MaxQuantity - (slot?.Quantity ?? 0),
                    remaining);
                if (movable == 0) continue;
                _slots[index] = new InventoryBlockStack(
                    incoming.Block, (slot?.Quantity ?? 0) + movable);
                remaining -= movable;
                if (remaining == 0)
                {
                    Revision++;
                    return true;
                }
            }
        }
        throw new InvalidOperationException(
            "Capacity was reserved but the stack could not be inserted.");
    }

    public bool ClickSlot(int index)
    {
        if (index is < 0 or >= TotalSlots)
            return false;
        var slot = _slots[index];
        if (slot is null && Cursor is null)
            return false;

        if (slot is not null && Cursor is not null &&
            slot.CanStackWith(Cursor))
        {
            var move = Math.Min(
                InventoryBlockStack.MaxQuantity - slot.Quantity,
                Cursor.Quantity);
            if (move == 0) return false;
            _slots[index] = slot.WithQuantity(slot.Quantity + move);
            Cursor = move == Cursor.Quantity
                ? null : Cursor.WithQuantity(Cursor.Quantity - move);
        }
        else
        {
            _slots[index] = Cursor;
            Cursor = slot;
        }
        Revision++;
        return true;
    }

    public bool TryCreativePick(BlockStateSnapshot block, int quantity)
    {
        var picked = new InventoryBlockStack(block, quantity);
        if (Cursor is null)
        {
            Cursor = picked;
        }
        else
        {
            if (!Cursor.CanStackWith(picked) ||
                Cursor.Quantity + quantity > InventoryBlockStack.MaxQuantity)
                return false;
            Cursor = Cursor.WithQuantity(Cursor.Quantity + quantity);
        }
        Revision++;
        return true;
    }

    public bool DiscardCursor()
    {
        if (Cursor is null) return false;
        Cursor = null;
        Revision++;
        return true;
    }

    public bool TryReturnCursor()
    {
        if (Cursor is null) return true;
        if (!TryInsert(Cursor)) return false;
        Cursor = null;
        Revision++;
        return true;
    }

    public bool TryConsumeSelected()
    {
        var index = BackpackSlots + SelectedSlot;
        var stack = _slots[index];
        if (stack is null) return false;
        _slots[index] = stack.Quantity == 1
            ? null : stack.WithQuantity(stack.Quantity - 1);
        Revision++;
        return true;
    }

    public bool TryDropSelected(out BlockStateSnapshot? block)
    {
        block = SelectedStack?.Block;
        return block is not null && TryConsumeSelected();
    }

    public bool SortBackpack()
    {
        var sorted = _slots.Take(BackpackSlots)
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderBy(s => s.Block.Cell.Block.Value)
            .ThenBy(s => s.Block.Cell.Orientation)
            .ThenBy(s => s.Block.Cell.Facing)
            .ToArray();
        var changed = false;
        for (var i = 0; i < BackpackSlots; i++)
        {
            var value = i < sorted.Length ? sorted[i] : null;
            if (_slots[i] == value) continue;
            _slots[i] = value;
            changed = true;
        }
        if (changed) Revision++;
        return changed;
    }

    private int AvailableCapacity(BlockStateSnapshot block)
    {
        var capacity = 0;
        foreach (var slot in _slots)
        {
            if (slot is null) capacity += InventoryBlockStack.MaxQuantity;
            else if (slot.Block == block)
                capacity += InventoryBlockStack.MaxQuantity - slot.Quantity;
        }
        return capacity;
    }

    private static IEnumerable<int> InsertionOrder()
    {
        for (var i = BackpackSlots; i < TotalSlots; i++) yield return i;
        for (var i = 0; i < BackpackSlots; i++) yield return i;
    }

    private static void CheckIndex(int index)
    {
        if (index is < 0 or >= TotalSlots)
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
