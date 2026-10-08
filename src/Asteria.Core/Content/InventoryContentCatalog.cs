using Asteria.Core.World;

namespace Asteria.Core.Content;

/// <summary>
/// Immutable authored creative choices and the only conversion of loaded
/// definitions to player inventory identities. UI cannot fabricate metadata.
/// </summary>
public sealed class InventoryContentCatalog
{
    private readonly BlockRegistry _blocks;
    private readonly InventoryCatalogChoice[] _choices;

    public InventoryContentCatalog(
        BlockRegistry blocks,
        PackContentRegistry<ItemDefinition> items,
        PackContentRegistry<ToolDefinition> tools)
    {
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(tools);

        var choices = new List<InventoryCatalogChoice>();
        foreach (var (runtimeId, definition) in blocks.AuthoredDefinitions())
        {
            choices.Add(new InventoryCatalogChoice(
                InventoryEntry.FromBlock(
                    definition.Id,
                    BlockStateSnapshot.FromCell(new VoxelCell(runtimeId))),
                definition.Category));
        }

        foreach (var definition in items.Definitions)
        {
            choices.Add(new InventoryCatalogChoice(
                InventoryEntry.FromItem(
                    definition.Id, maxStackSize: definition.MaxStackSize),
                definition.Category));
            foreach (var variant in definition.IconVariants)
            {
                choices.Add(new InventoryCatalogChoice(
                    InventoryEntry.FromItem(definition.Id,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [variant.MetadataKey] = variant.MetadataValue,
                        }, definition.MaxStackSize),
                    definition.Category));
            }
        }

        foreach (var definition in tools.Definitions)
        {
            choices.Add(new InventoryCatalogChoice(
                InventoryEntry.FromTool(
                    definition.Id, maxStackSize: definition.MaxStackSize),
                definition.Category));
        }

        _choices = choices
            .OrderBy(choice => choice.Entry.Kind)
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
    }

    public IReadOnlyList<InventoryCatalogChoice> Choices => _choices;

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
    string Category);
