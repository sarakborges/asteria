using System.Collections.ObjectModel;

namespace Asteria.Core.World;

public enum InventoryEntryKind : byte
{
    Block,
    Item,
    Tool,
}

/// <summary>
/// Immutable identity of a portable inventory entry. Content identity,
/// complete portable block state and sorted metadata all affect stacking.
/// Never expose mutable metadata owned by the caller.
/// </summary>
public sealed class InventoryEntry : IEquatable<InventoryEntry>
{
    private readonly IReadOnlyDictionary<string, string> _metadata;
    private readonly KeyValuePair<string, string>[] _orderedMetadata;

    private InventoryEntry(
        InventoryEntryKind kind,
        string id,
        BlockStateSnapshot? block,
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        BlockDefinition.ValidateId(id);
        if (kind == InventoryEntryKind.Block != (block is not null))
            throw new ArgumentException(
                "Only block entries carry a portable voxel state.",
                nameof(block));

        Kind = kind;
        Id = id;
        Block = block;
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                if (string.IsNullOrWhiteSpace(key) ||
                    string.IsNullOrWhiteSpace(value) ||
                    key != key.Trim() || value != value.Trim())
                    throw new ArgumentException(
                        "Metadata keys and values must be nonempty and trimmed.",
                        nameof(metadata));
                sorted.Add(key, value);
            }
        }

        _orderedMetadata = sorted.ToArray();
        _metadata = new ReadOnlyDictionary<string, string>(sorted);
    }

    public InventoryEntryKind Kind { get; }
    public string Id { get; }
    public BlockStateSnapshot? Block { get; }
    public IReadOnlyDictionary<string, string> Metadata => _metadata;
    public int MaxStackSize => Kind == InventoryEntryKind.Tool ? 1 : 64;

    public static InventoryEntry FromBlock(
        string id,
        BlockStateSnapshot block) =>
        new(InventoryEntryKind.Block, id, block, null);

    public static InventoryEntry FromItem(
        string id,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(InventoryEntryKind.Item, id, null, metadata);

    public static InventoryEntry FromTool(
        string id,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(InventoryEntryKind.Tool, id, null, metadata);

    public bool Equals(InventoryEntry? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || Kind != other.Kind || Id != other.Id ||
            Block != other.Block ||
            _orderedMetadata.Length != other._orderedMetadata.Length)
            return false;
        return _orderedMetadata.AsSpan().SequenceEqual(other._orderedMetadata);
    }

    public override bool Equals(object? obj) =>
        obj is InventoryEntry other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Block);
        foreach (var (key, value) in _orderedMetadata)
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}

public sealed record InventoryStack
{
    public InventoryStack(InventoryEntry entry, int quantity = 1)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        if (quantity < 1 || quantity > entry.MaxStackSize)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Quantity = quantity;
    }

    public InventoryEntry Entry { get; }
    public int Quantity { get; }
    public string Id => Entry.Id;
    public InventoryEntryKind Kind => Entry.Kind;
    public BlockStateSnapshot? Block => Entry.Block;
    public int MaxStackSize => Entry.MaxStackSize;

    public bool CanStackWith(InventoryStack other) =>
        Entry.Equals(other.Entry);

    public InventoryStack WithQuantity(int quantity) => new(Entry, quantity);
}
