using System.Text.Json;
using Asteria.Core.Content;

namespace Asteria.Core.World;

public enum LayerRemovalScope : byte
{
    Face,
    Voxel,
}

/// <summary>Guaranteed drop emitted once when an authored layer is mined.</summary>
public sealed record LayerDropDefinition
{
    public LayerDropDefinition(InventoryEntryKind kind, string id, int quantity = 1)
    {
        if (kind is not (InventoryEntryKind.Layer or InventoryEntryKind.Item))
            throw new ArgumentOutOfRangeException(nameof(kind));
        BlockDefinition.ValidateId(id);
        if (quantity is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Kind = kind;
        Id = id;
        Quantity = quantity;
    }

    public InventoryEntryKind Kind { get; }
    public string Id { get; }
    public int Quantity { get; }

    public InventoryStack ToStack() => new(
        Kind == InventoryEntryKind.Layer
            ? InventoryEntry.FromLayer(Id)
            : InventoryEntry.FromItem(Id),
        Quantity);
}

/// <summary>Optional deterministic 3D vein generation for an interactive layer.</summary>
public sealed class LayerVeinDefinition
{
    public LayerVeinDefinition(
        string dimension, IEnumerable<string> hostBlocks,
        int minY, int maxY, int spacing, int radius, double chance)
    {
        BlockDefinition.ValidateId(dimension);
        var hosts = hostBlocks?.ToArray() ?? throw new ArgumentNullException(nameof(hostBlocks));
        if (hosts.Length == 0 || hosts.Length > 32 || hosts.Distinct(StringComparer.Ordinal).Count() != hosts.Length)
            throw new ArgumentException("Vein generation requires distinct host blocks.", nameof(hostBlocks));
        foreach (var host in hosts) BlockDefinition.ValidateId(host);
        if (minY < 0 || maxY < minY || maxY > 4095)
            throw new ArgumentOutOfRangeException(nameof(minY));
        if (spacing is < 8 or > 64 || radius is < 1 or > 5 || radius * 2 >= spacing)
            throw new ArgumentOutOfRangeException(nameof(spacing), "Radius must fit a generation grid.");
        if (!double.IsFinite(chance) || chance is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(chance));
        Dimension = dimension;
        HostBlocks = Array.AsReadOnly(hosts);
        MinY = minY; MaxY = maxY; Spacing = spacing; Radius = radius; Chance = chance;
    }

    public string Dimension { get; }
    public IReadOnlyList<string> HostBlocks { get; }
    public int MinY { get; }
    public int MaxY { get; }
    public int Spacing { get; }
    public int Radius { get; }
    public double Chance { get; }
}

/// <summary>Authored per-face removable overlay with its own mining and drops.</summary>
public sealed class AttachedLayerDefinition
{
    private readonly HashSet<BlockFace> _faces;

    public AttachedLayerDefinition(
        string id, string texture, IEnumerable<BlockFace>? faces = null,
        string category = "natural_blocks",
        BlockTint tint = BlockTint.None, float offset = 1f / 1024f,
        float? alphaCutoff = 0.5f, bool alphaBlend = false, bool castsShadow = false,
        BlockMiningDefinition? mining = null, LayerRemovalScope scope = LayerRemovalScope.Face,
        IEnumerable<LayerDropDefinition>? drops = null,
        LayerVeinDefinition? generation = null)
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
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        var authoredDrops = drops?.ToArray() ??
            [new LayerDropDefinition(InventoryEntryKind.Layer, id)];
        if (authoredDrops.Length > 8 || authoredDrops.Any(drop => drop is null))
            throw new ArgumentException("Layers support at most eight valid drops.", nameof(drops));
        var allowed = (faces ?? Enum.GetValues<BlockFace>()).ToArray();
        if (allowed.Length == 0 || allowed.Any(face => !Enum.IsDefined(face)) ||
            allowed.Distinct().Count() != allowed.Length)
            throw new ArgumentException("Attached layer faces must be nonempty and distinct.", nameof(faces));

        if (generation is not null && (scope != LayerRemovalScope.Voxel ||
            allowed.Length != 6))
            throw new ArgumentException("Generated veins must cover the whole voxel.", nameof(generation));

        Id = id; Texture = texture; Category = category; Tint = tint; Offset = offset;
        AlphaCutoff = alphaCutoff; AlphaBlend = alphaBlend; CastsShadow = castsShadow;
        Mining = mining ?? new BlockMiningDefinition();
        Scope = scope;
        Drops = Array.AsReadOnly(authoredDrops);
        Generation = generation;
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
    public BlockMiningDefinition Mining { get; }
    public LayerRemovalScope Scope { get; }
    public IReadOnlyList<LayerDropDefinition> Drops { get; }
    public LayerVeinDefinition? Generation { get; }
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
        var scope = root.TryGetProperty("scope", out var scopeValue)
            ? scopeValue.GetString() switch
            {
                "face" => LayerRemovalScope.Face,
                "voxel" => LayerRemovalScope.Voxel,
                _ => throw new FormatException("Unknown layer scope.")
            } : LayerRemovalScope.Face;
        return new AttachedLayerDefinition(
            PackContentFields.Id(root),
            PackContentFields.ResourcePath(PackContentFields.RequiredString(root, "texture"), "texture"),
            faces, PackContentFields.OptionalString(root, "category") ?? "natural_blocks",
            tint, offset, cutoff, blend, shadows,
            ParseMining(root), scope, ParseDrops(root), ParseGeneration(root));
    }

    private static LayerVeinDefinition? ParseGeneration(JsonElement root)
    {
        if (!root.TryGetProperty("generation", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new FormatException("layer generation must be an object.");
        string[] hosts = ParseToolTags(value, "hostBlocks");
        static int RequiredInt(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Number
                ? child.GetInt32()
                : throw new FormatException($"generation.{name} must be an integer.");
        var chance = value.TryGetProperty("chance", out var frequency) &&
            frequency.ValueKind == JsonValueKind.Number
            ? frequency.GetDouble()
            : throw new FormatException("generation.chance must be a number.");
        return new LayerVeinDefinition(
            PackContentFields.RequiredString(value, "dimension"), hosts,
            RequiredInt(value, "minY"), RequiredInt(value, "maxY"),
            RequiredInt(value, "spacing"), RequiredInt(value, "radius"), chance);
    }

    private static BlockMiningDefinition ParseMining(JsonElement root)
    {
        if (!root.TryGetProperty("mining", out var value))
            return new BlockMiningDefinition();
        if (value.ValueKind != JsonValueKind.Object)
            throw new FormatException("layer mining must be an object.");
        var hardness = value.TryGetProperty("hardness", out var number)
            ? number.GetSingle() : 1f;
        var unbreakable = value.TryGetProperty("unbreakable", out var flag)
            && flag.GetBoolean();
        return new BlockMiningDefinition(hardness,
            ParseToolTags(value, "requiredTools"),
            ParseToolTags(value, "preferredTools"), unbreakable);
    }

    private static string[] ParseToolTags(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{key} must be an array.");
        return value.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new FormatException($"{key} must contain strings.")).ToArray();
    }

    private static IEnumerable<LayerDropDefinition>? ParseDrops(JsonElement root)
    {
        if (!root.TryGetProperty("drops", out var drops)) return null;
        if (drops.ValueKind != JsonValueKind.Array)
            throw new FormatException("layer drops must be an array.");
        return drops.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new FormatException("layer drop must be an object.");
            var kind = PackContentFields.RequiredString(item, "kind") switch
            {
                "layer" => InventoryEntryKind.Layer,
                "item" => InventoryEntryKind.Item,
                _ => throw new FormatException("Unsupported layer drop kind.")
            };
            var count = item.TryGetProperty("quantity", out var quantity)
                ? quantity.GetInt32() : 1;
            return new LayerDropDefinition(kind,
                PackContentFields.RequiredString(item, "id"), count);
        }).ToArray();
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
