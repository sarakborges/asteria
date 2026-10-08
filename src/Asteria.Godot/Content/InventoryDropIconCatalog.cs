using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Content;

/// <summary>
/// Lazy pack-scoped cache. Only real authored item/tool icons can become
/// sprites, and each decoded texture is shared across Sphere sessions.
/// </summary>
public sealed class InventoryDropIconCatalog
{
    private readonly PackSelection _pack;
    private readonly InventoryContentCatalog _catalog;
    private readonly Dictionary<string, Texture2D> _textures =
        new(StringComparer.Ordinal);

    public InventoryDropIconCatalog(
        PackSelection pack,
        InventoryContentCatalog catalog)
    {
        _pack = pack;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public Texture2D? Resolve(InventoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind == InventoryEntryKind.Block)
            return null;

        var choices = _catalog.Choices;
        var exact = choices.FirstOrDefault(choice =>
            choice.Entry.Equals(entry));
        var authored = exact ?? choices.FirstOrDefault(choice =>
            choice.Entry.Kind == entry.Kind &&
            choice.Entry.Id == entry.Id &&
            choice.Entry.Metadata.Count == 0);
        if (authored?.IconResourcePath is not { } path)
            return null;

        if (_textures.TryGetValue(path, out var cached))
            return cached;

        var texture = ProjectPackFiles.LoadTexture(_pack, path);
        _textures.Add(path, texture);
        return texture;
    }
}
