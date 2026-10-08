using Asteria.Core.Content;

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

    /// <summary>
    /// Replaces all authoritative slots/cursor in a fresh restored session.
    /// Validates the entire snapshot before changing any live inventory slot.
    /// </summary>
    internal void Restore(PlayerInventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SelectedSlot is < 0 or >= HotbarSlots ||
            snapshot.Backpack is null || snapshot.Backpack.Length != BackpackSlots ||
            snapshot.Hotbar is null || snapshot.Hotbar.Length != HotbarSlots)
        {
            throw new InvalidDataException("Invalid saved player inventory shape.");
        }

        var restored = new InventoryStack?[TotalSlots];
        Array.Copy(snapshot.Backpack, 0, restored, 0, BackpackSlots);
        Array.Copy(snapshot.Hotbar, 0, restored, BackpackSlots, HotbarSlots);
        // InventoryStack is validated and immutable; all arrays are detached.
        Array.Copy(restored, _slots, TotalSlots);
        SelectedSlot = snapshot.SelectedSlot;
        Cursor = snapshot.Cursor;
        Revision++;
    }

    public bool SelectHotbar(int index)
    {
        if (index is < 0 or >= HotbarSlots || index == SelectedSlot)
            return false;
        SelectedSlot = index;
        Revision++;
        return true;
    }

    public bool CanInsert(InventoryStack incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        return AvailableCapacity(incoming) >= incoming.Quantity;
    }

    public bool TryInsert(InventoryStack incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (!TryInsertInto(_slots, incoming)) return false;
        Revision++;
        return true;
    }

    /// <summary>
    /// Inventory crafting is an atomic player-inventory mutation. Preflight
    /// uses a private slot array; failed crafts leave slots, cursor and
    /// revision unchanged. Input quantities count portable item identities,
    /// not placeable blocks that happen to share the same namespaced ID.
    /// </summary>
    public bool CanCraft(CraftingRecipeDefinition recipe, InventoryEntry result) =>
        TryPrepareCraft(recipe, result, out _);

    public bool TryCraft(CraftingRecipeDefinition recipe, InventoryEntry result)
    {
        if (!TryPrepareCraft(recipe, result, out var slots))
            return false;

        Array.Copy(slots, _slots, TotalSlots);
        Revision++;
        return true;
    }

    public int ItemQuantity(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var count = 0;
        foreach (var slot in _slots)
            if (slot is { Kind: InventoryEntryKind.Item } &&
                string.Equals(slot.Id, id, StringComparison.Ordinal))
                count += slot.Quantity;
        return count;
    }

    private bool TryPrepareCraft(
        CraftingRecipeDefinition recipe,
        InventoryEntry result,
        out InventoryStack?[] slots)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(result);
        slots = (InventoryStack?[])_slots.Clone();
        if (recipe.Environment != "inventory" ||
            result.Id != recipe.Result.Item ||
            result.Metadata.Count != 0)
            return false;

        foreach (var ingredient in recipe.Ingredients)
        {
            var required = ingredient.Quantity;
            foreach (var index in ConsumptionOrder())
            {
                var current = slots[index];
                if (current is not { Kind: InventoryEntryKind.Item } ||
                    current.Id != ingredient.Item)
                    continue;

                var taken = Math.Min(current.Quantity, required);
                required -= taken;
                slots[index] = current.Quantity == taken
                    ? null : current.WithQuantity(current.Quantity - taken);
                if (required == 0) break;
            }
            if (required != 0) return false;
        }

        var remaining = recipe.Result.Quantity;
        while (remaining > 0)
        {
            var quantity = Math.Min(result.MaxStackSize, remaining);
            if (!TryInsertInto(slots, new InventoryStack(result, quantity)))
                return false;
            remaining -= quantity;
        }
        return true;
    }

    // Selected hotbar slot first, then the other hotbar slots and backpack.
    private IEnumerable<int> ConsumptionOrder()
    {
        yield return BackpackSlots + SelectedSlot;
        for (var i = BackpackSlots; i < TotalSlots; i++)
            if (i != BackpackSlots + SelectedSlot)
                yield return i;
        for (var i = 0; i < BackpackSlots; i++)
            yield return i;
    }

    private static bool TryInsertInto(
        InventoryStack?[] slots, InventoryStack incoming)
    {
        if (AvailableCapacity(slots, incoming) < incoming.Quantity)
            return false;

        var remaining = incoming.Quantity;
        for (var phase = 0; phase < 2; phase++)
        {
            foreach (var index in InsertionOrder())
            {
                var slot = slots[index];
                if (phase == 0 && (slot is null || !slot.CanStackWith(incoming)))
                    continue;
                if (phase == 1 && slot is not null)
                    continue;

                var moved = Math.Min(
                    incoming.MaxStackSize - (slot?.Quantity ?? 0), remaining);
                if (moved == 0) continue;
                slots[index] = incoming.WithQuantity(
                    (slot?.Quantity ?? 0) + moved);
                remaining -= moved;
                if (remaining == 0) return true;
            }
        }
        throw new InvalidOperationException(
            "Reserved inventory capacity could not accommodate a stack.");
    }

    public bool ClickSlot(int index)
    {
        if (index is < 0 or >= TotalSlots) return false;
        if (!ClickExternalSlot(_slots[index], out var replacement))
            return false;
        _slots[index] = replacement;
        return true;
    }

    /// <summary>
    /// Transfers the player's cursor against one externally owned slot.
    /// The caller must validate that slot and synchronously publish the
    /// returned replacement. No UI or container can change Cursor directly.
    /// </summary>
    public bool ClickExternalSlot(
        InventoryStack? slot, out InventoryStack? replacement)
    {
        replacement = slot;
        if (slot is null && Cursor is null) return false;
        if (slot is not null && Cursor is not null &&
            slot.CanStackWith(Cursor))
        {
            var move = Math.Min(
                slot.MaxStackSize - slot.Quantity, Cursor.Quantity);
            if (move == 0) return false;
            replacement = slot.WithQuantity(slot.Quantity + move);
            Cursor = move == Cursor.Quantity
                ? null : Cursor.WithQuantity(Cursor.Quantity - move);
        }
        else
        {
            replacement = Cursor;
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
        var compacted = InventoryStackSorter.SortAndCompact(
            _slots.Take(BackpackSlots));
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

    private int AvailableCapacity(InventoryStack incoming) =>
        AvailableCapacity(_slots, incoming);

    private static int AvailableCapacity(
        InventoryStack?[] slots, InventoryStack incoming)
    {
        var capacity = 0;
        foreach (var slot in slots)
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
