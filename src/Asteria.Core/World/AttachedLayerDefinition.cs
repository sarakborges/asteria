using System.Text.Json;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>Authored per-face removable overlay, independent of intrinsic block textures.</summary>
public sealed class AttachedLayerDefinition
{
    private readonly HashSet<BlockFace> _faces;

    public AttachedLayerDefinition(
        string id, string texture, IEnumerable<BlockFace>? faces = null,
        string category = "natural_blocks",
        BlockTint tint = BlockTint.None, float offset = 1f / 1024f,
        float? alphaCutoff = 0.5f, bool alphaBlend = false, bool castsShadow = false)
    {
        BlockDefinition.ValidateId(id);
        PackContentFields.ResourcePath(texture, nameof(texture));
        if (string.IsNullOrWhiteSpace(category) || category != category.Trim())
            throw new ArgumentException("Layer category must be nonempty and trimmed.", nameof(category));
        if (!Enum.IsDefined(tint)) throw new ArgumentOutOfRangeException(nameof(tint));
        if (!float.IsFinite(offset) || offset < 0f || offset > 1f / 8f)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (alphaCutoff is { } cutoff && (!float.IsFinite(cutoff) || cutoff < 0f || cutoff > 1f))
            throw new ArgumentOutOfRangeException(nameof(alphaCutoff));
        if (alphaBlend && alphaCutoff is not null)
            throw new ArgumentException("Alpha blending and alpha cutoff are mutually exclusive.");
        var allowed = (faces ?? Enum.GetValues<BlockFace>()).ToArray();
        if (allowed.Length == 0 || allowed.Any(face => !Enum.IsDefined(face)) ||
            allowed.Distinct().Count() != allowed.Length)
            throw new ArgumentException("Attached layer faces must be nonempty and distinct.", nameof(faces));

        Id = id; Texture = texture; Category = category; Tint = tint; Offset = offset;
        AlphaCutoff = alphaCutoff; AlphaBlend = alphaBlend; CastsShadow = castsShadow;
        _faces = allowed.ToHashSet();
    }

    public string Id { get; }
    public string Texture { get; }
    public string Category { get; }
    public BlockTint Tint { get; }
    public float Offset { get; }
    public float? AlphaCutoff { get; }
    public bool AlphaBlend { get; }
    public bool CastsShadow { get; }
    public BlockRenderMode RenderMode => AlphaBlend ? BlockRenderMode.Translucent :
        AlphaCutoff is null ? BlockRenderMode.Opaque : BlockRenderMode.Cutout;
    public bool Supports(BlockFace face) => _faces.Contains(face);

    public static AttachedLayerDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var faces = root.TryGetProperty("faces", out var authoredFaces)
            ? ParseFaces(authoredFaces) : null;
        var tint = root.TryGetProperty("tint", out var tintValue)
            ? tintValue.GetString() switch
            {
                "none" => BlockTint.None, "grass" => BlockTint.Grass,
                "leaf" => BlockTint.Leaf, "foliage" => BlockTint.Foliage,
                _ => throw new FormatException("Unknown attached layer tint.")
            } : BlockTint.None;
        var offset = root.TryGetProperty("offset", out var offsetValue)
            ? offsetValue.GetSingle() : 1f / 1024f;
        var blend = root.TryGetProperty("alphaBlend", out var blendValue) &&
            blendValue.GetBoolean();
        var cutoff = root.TryGetProperty("alphaCutoff", out var cutoffValue)
            ? cutoffValue.ValueKind == JsonValueKind.Null ? (float?)null : cutoffValue.GetSingle()
            : blend ? null : 0.5f;
        var shadows = root.TryGetProperty("castsShadow", out var shadowValue) &&
            shadowValue.GetBoolean();
        return new AttachedLayerDefinition(
            PackContentFields.Id(root),
            PackContentFields.ResourcePath(PackContentFields.RequiredString(root, "texture"), "texture"),
            faces, PackContentFields.OptionalString(root, "category") ?? "natural_blocks",
            tint, offset, cutoff, blend, shadows);
    }

    private static BlockFace[] ParseFaces(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new FormatException("faces must be an array.");
        return value.EnumerateArray().Select(entry =>
        {
            if (entry.ValueKind != JsonValueKind.String)
                throw new FormatException("faces must contain strings.");
            return entry.GetString() switch
            {
                "top" => BlockFace.Top, "bottom" => BlockFace.Bottom,
                "left" => BlockFace.Left, "right" => BlockFace.Right,
                "front" => BlockFace.Front, "back" => BlockFace.Back,
                _ => throw new FormatException("Unknown attached layer face.")
            };
        }).ToArray();
    }
}

public sealed class AttachedLayerRegistry
{
    private readonly Dictionary<string, AttachedLayerDefinition> _items;

    public AttachedLayerRegistry(IEnumerable<AttachedLayerDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _items = new(StringComparer.Ordinal);
        foreach (var item in definitions)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!_items.TryAdd(item.Id, item))
                throw new ArgumentException($"Duplicate attached layer: {item.Id}", nameof(definitions));
        }
    }

    public static AttachedLayerRegistry FromJson(IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return new(documents.Select(AttachedLayerDefinition.Parse));
    }

    public IReadOnlyList<AttachedLayerDefinition> Definitions =>
        _items.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();

    public AttachedLayerDefinition Get(string id) =>
        _items.TryGetValue(id, out var result) ? result :
            throw new KeyNotFoundException($"Unknown attached layer: {id}");

    public bool TryGet(string id, out AttachedLayerDefinition? definition) =>
        _items.TryGetValue(id, out definition);
}
