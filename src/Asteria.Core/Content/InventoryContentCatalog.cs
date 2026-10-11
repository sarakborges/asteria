using Asteria.Core.World;

namespace Asteria.Core.Content;

/// <summary>
/// Immutable authored creative choices and the only conversion of loaded
/// definitions to player inventory identities. UI cannot fabricate metadata.
/// </summary>
public sealed class InventoryContentCatalog
{
    private readonly BlockRegistry _blocks;
    private readonly PackContentRegistry<ItemDefinition> _items;
    private readonly InventoryCatalogChoice[] _choices;
    private readonly IReadOnlyList<InventoryCategoryDefinition> _categories;

    public InventoryContentCatalog(
        BlockRegistry blocks,
        PackContentRegistry<ItemDefinition> items,
        PackContentRegistry<ToolDefinition> tools,
        AttachedLayerRegistry? layers = null,
        InventoryCategoryRegistry? categories = null)
    {
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        ArgumentNullException.ThrowIfNull(tools);

        foreach (var (_, block) in blocks.AuthoredDefinitions())
        {
            if (block.Interaction == BlockInteractionKind.Pickup &&
                !items.TryGet(block.PickupItemId!, out _))
            {
                throw new InvalidOperationException(
                    $"Pickup block {block.Id} references unknown item {block.PickupItemId}.");
            }
        }

        var choices = new List<InventoryCatalogChoice>();
        foreach (var (runtimeId, definition) in blocks.AuthoredDefinitions())
        {
            if (definition.HasTag("creative_hidden")) continue;
            choices.Add(new InventoryCatalogChoice(
                InventoryEntry.FromBlock(
                    definition.Id,
                    BlockStateSnapshot.FromCell(new VoxelCell(runtimeId))),
                definition.Category, null));
        }

        foreach (var definition in items.Definitions)
        {
            choices.Add(new InventoryCatalogChoice(
                InventoryEntry.FromItem(
                    definition.Id, maxStackSize: definition.MaxStackSize),
                definition.Category, definition.Icon));
            foreach (var variant in definition.IconVariants)
            {
                choices.Add(new InventoryCatalogChoice(
                    InventoryEntry.FromItem(definition.Id,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [variant.MetadataKey] = variant.MetadataValue,
                        }, definition.MaxStackSize),
                    definition.Category, variant.Icon));
            }
        }

        foreach (var definition in tools.Definitions)
        {
            choices.Add(new InventoryCatalogChoice(
                InventoryEntry.FromTool(
                    definition.Id, maxStackSize: definition.MaxStackSize),
                definition.Category, definition.Icon));
            foreach (var variant in definition.IconVariants ?? [])
            {
                choices.Add(new InventoryCatalogChoice(
                    InventoryEntry.FromTool(definition.Id,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [variant.MetadataKey] = variant.MetadataValue,
                        }, definition.MaxStackSize),
                    definition.Category, variant.Icon));
            }
        }

        if (layers is not null)
        {
            foreach (var layer in layers.Definitions)
            {
                choices.Add(new InventoryCatalogChoice(
                    InventoryEntry.FromLayer(layer.Id),
                    layer.Category, layer.Texture));
            }
        }

        if (categories is not null)
            foreach (var choice in choices)
                _ = categories.Get(choice.Category);

        _categories = categories?.Definitions ?? Array.Empty<InventoryCategoryDefinition>();
        _choices = choices
            .OrderBy(choice => categories?.Get(choice.Category).Order ?? 0)
            .ThenBy(choice => choice.Category, StringComparer.Ordinal)
            .ThenBy(choice => choice.Entry.Kind)
            .ThenBy(choice => choice.Entry.Id, StringComparer.Ordinal)
            .ThenBy(choice => choice.Entry.Metadata.Count == 0
                ? "" : string.Join("|", choice.Entry.Metadata.Select(
                    pair => $"{pair.Key.Length}:{pair.Key}{pair.Value.Length}:{pair.Value}")),
                StringComparer.Ordinal)
            .ToArray();

        for (var index = 1; index < _choices.Length; index++)
        {
            if (_choices[index].Entry.Equals(_choices[index - 1].Entry))
                throw new InvalidOperationException(
                    $"Duplicate creative inventory identity: {_choices[index].Entry.Id}");
        }

        // Validate every authored drop at content load, not after a voxel
        // has been irreversibly removed by mining.
        foreach (var (_, block) in blocks.AuthoredDefinitions())
            if (block.LootTable is { } table)
                foreach (var drop in table.Entries)
                    if (!TryResolveLoot(drop.Item, out _))
                        throw new InvalidOperationException(
                            $"Block {block.Id} loot references unknown content {drop.Item}.");
        if (layers is not null)
            foreach (var layer in layers.Definitions)
                foreach (var drop in layer.Drops)
                    if (!TryResolve(drop.Kind, drop.Id, null, null, out _))
                        throw new InvalidOperationException(
                            $"Layer {layer.Id} drop references unknown {drop.Kind}: {drop.Id}.");
    }

    /// <summary>Resolves MineClone-style untyped loot IDs using an explicit,
    /// stable priority. Runtime drops reuse the canonical authored stack
    /// limits and never infer identity from a string prefix.</summary>
    public bool TryResolveLoot(string id, out InventoryEntry? entry)
    {
        foreach (var kind in new[] {
                     InventoryEntryKind.Item, InventoryEntryKind.Block,
                     InventoryEntryKind.Layer, InventoryEntryKind.Tool })
            if (TryResolve(kind, id, null, null, out entry))
                return true;
        entry = null;
        return false;
    }

    public bool CanEquip(InventoryEntry entry, EquipmentSlot slot) =>
        entry.Kind == InventoryEntryKind.Item &&
        Enum.IsDefined(slot) &&
        _items.TryGet(entry.Id, out var authored) &&
        authored!.EquipmentSlot == slot &&
        entry.MaxStackSize == 1;

    public IReadOnlyList<InventoryCatalogChoice> Choices => _choices;
    public IReadOnlyList<InventoryCategoryDefinition> Categories => _categories;

    public InventoryEntry ForDroppedBlock(BlockStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var definition = _blocks.GetDefinition(snapshot.Cell.Block);
        return InventoryEntry.FromBlock(definition.Id, snapshot);
    }

    public bool TryResolve(
        InventoryEntryKind kind,
        string id,
        string? metadataKey,
        string? metadataValue,
        out InventoryEntry? entry)
    {
        entry = null;
        if (!Enum.IsDefined(kind) || id is null) return false;
        if ((metadataKey is null) != (metadataValue is null)) return false;
        foreach (var choice in _choices)
        {
            var identity = choice.Entry;
            if (identity.Kind != kind ||
                !string.Equals(identity.Id, id, StringComparison.Ordinal))
                continue;
            if (metadataKey is null)
            {
                if (identity.Metadata.Count != 0) continue;
            }
            else if (identity.Metadata.Count != 1 ||
                !identity.Metadata.TryGetValue(metadataKey, out var value) ||
                !string.Equals(value, metadataValue, StringComparison.Ordinal))
                continue;

            entry = identity;
            return true;
        }
        return false;
    }
}

public sealed record InventoryCatalogChoice(
    InventoryEntry Entry,
    string Category,
    string? IconResourcePath);
