namespace Asteria.Core.World;

/// <summary>Stable, lossless stack compaction shared by player and container
/// inventories. Sort keys preserve authored metadata and portable block state.</summary>
internal static class InventoryStackSorter
{
    public static List<InventoryStack> SortAndCompact(
        IEnumerable<InventoryStack?> source)
    {
        var sorted = source
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

        return compacted;
    }
}
