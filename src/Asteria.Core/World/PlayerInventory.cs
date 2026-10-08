namespace Asteria.Core.World;

public sealed record PlayerInventorySnapshot(
    int SelectedSlot,
    InventoryStack?[] Backpack,
    InventoryStack?[] Hotbar,
    InventoryStack? Cursor);

/// <summary>
/// The player owns one inventory across Spheres. Insertions and cursor
/// transfers are transactional and deterministic, independent of the UI.
/// </summary>
public sealed class PlayerInventory
{
    public const int BackpackSlots = 27;
    public const int HotbarSlots = 9;
    public const int TotalSlots = BackpackSlots + HotbarSlots;

    private readonly InventoryStack?[] _slots = new InventoryStack?[TotalSlots];

    public int SelectedSlot { get; private set; }
    public InventoryStack? Cursor { get; private set; }
    public ulong Revision { get; private set; }

    public InventoryStack? SelectedStack => _slots[BackpackSlots + SelectedSlot];

    public InventoryStack? SlotAt(int index)
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
        if (index is < 0 or >= HotbarSlots || index == SelectedSlot)
            return false;
        SelectedSlot = index;
        Revision++;
        return true;
    }

    public bool TryInsert(InventoryStack incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (AvailableCapacity(incoming) < incoming.Quantity)
            return false;
        var remaining = incoming.Quantity;
        for (var phase = 0; phase < 2; phase++)
        {
            foreach (var index in InsertionOrder())
            {
                var slot = _slots[index];
                if (phase == 0 && (slot is null ||
                                   !slot.CanStackWith(incoming))) continue;
                if (phase == 1 && slot is not null) continue;
                var move = Math.Min(
                    incoming.MaxStackSize - (slot?.Quantity ?? 0), remaining);
                if (move == 0) continue;
                _slots[index] = incoming.WithQuantity(
                    (slot?.Quantity ?? 0) + move);
                remaining -= move;
                if (remaining == 0)
                {
                    Revision++;
                    return true;
                }
            }
        }
        throw new InvalidOperationException(
            "Reserved inventory capacity could not accommodate a stack.");
    }

    public bool ClickSlot(int index)
    {
        if (index is < 0 or >= TotalSlots) return false;
        var slot = _slots[index];
        if (slot is null && Cursor is null) return false;

        if (slot is not null && Cursor is not null &&
            slot.CanStackWith(Cursor))
        {
            var move = Math.Min(
                slot.MaxStackSize - slot.Quantity, Cursor.Quantity);
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

    public bool TryCreativePick(InventoryEntry entry, int quantity = 1)
    {
        var picked = new InventoryStack(entry, quantity);
        if (Cursor is null)
            Cursor = picked;
        else
        {
            if (!Cursor.CanStackWith(picked) ||
                Cursor.Quantity + quantity > Cursor.MaxStackSize)
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

    /// <summary>
    /// Changes authored metadata on a single selected item without permitting
    /// stale selections, item type substitution or stack-size duplication.
    /// </summary>
    public bool TryReplaceSelected(InventoryStack expected, InventoryEntry replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        var index = BackpackSlots + SelectedSlot;
        if (_slots[index] != expected || expected.Quantity != 1 ||
            expected.Kind != replacement.Kind || expected.Id != replacement.Id ||
            expected.MaxStackSize != replacement.MaxStackSize)
            return false;
        _slots[index] = new InventoryStack(replacement);
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

    public bool TryDropSelectedBlock(out BlockStateSnapshot? block)
    {
        // Physical dropped-entity runtime currently only presents blocks.
        block = SelectedStack?.Block;
        return block is not null && TryConsumeSelected();
    }

    public bool SortBackpack()
    {
        var sorted = _slots.Take(BackpackSlots)
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ThenBy(s => string.Join("", s.Entry.Metadata.Select(
                pair => $"{pair.Key.Length}:{pair.Key}{pair.Value.Length}:{pair.Value}")),
                StringComparer.Ordinal)
            .ThenBy(s => s.Block?.Cell.Block.Value ?? 0)
            .ThenBy(s => s.Block?.Cell.TextureRotation ?? TextureRotation.Degrees0)
            .ThenBy(s => s.Block?.Cell.Orientation ?? BlockOrientation.Y)
            .ThenBy(s => s.Block?.Cell.Facing ?? HorizontalFacing.South)
            .ThenBy(s => s.Block?.Cell.State ?? 0)
            .ToArray();
        var compacted = new List<InventoryStack>(sorted.Length);
        foreach (var original in sorted)
        {
            var incoming = original;
            if (compacted.Count > 0)
            {
                var previous = compacted[^1];
                if (previous.CanStackWith(incoming) &&
                    previous.Quantity < previous.MaxStackSize)
                {
                    var merged = Math.Min(
                        previous.MaxStackSize - previous.Quantity,
                        incoming.Quantity);
                    compacted[^1] = previous.WithQuantity(
                        previous.Quantity + merged);
                    if (merged == incoming.Quantity) continue;
                    incoming = incoming.WithQuantity(incoming.Quantity - merged);
                }
            }
            compacted.Add(incoming);
        }

        var changed = false;
        for (var i = 0; i < BackpackSlots; i++)
        {
            var value = i < compacted.Count ? compacted[i] : null;
            if (_slots[i] == value) continue;
            _slots[i] = value;
            changed = true;
        }
        if (changed) Revision++;
        return changed;
    }

    private int AvailableCapacity(InventoryStack incoming)
    {
        var capacity = 0;
        foreach (var slot in _slots)
        {
            if (slot is null) capacity += incoming.MaxStackSize;
            else if (slot.CanStackWith(incoming))
                capacity += slot.MaxStackSize - slot.Quantity;
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
