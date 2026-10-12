using System.Numerics;
using System.Text.Json;
using Asteria.Core.World;

namespace Asteria.Core.Content;

public enum ObjectInteraction : byte { Break, Pickup }

public enum ObjectVisualKind : byte
{
    CrossedSprite,
    ExtrudedSprite,
}

/// <summary>Portable geometric intent; Godot only creates meshes/materials.</summary>
public sealed record ObjectVisualDefinition(
    ObjectVisualKind Kind,
    string Texture,
    float Width,
    float Height,
    int Planes,
    float BaseOffset,
    float AlphaCutoff,
    float Depth = 0f);

public sealed record ObjectTargetDefinition(Vector3 Size, Vector3 CenterOffset);

/// <summary>
/// Independent, immutable content definition for attached world objects.
/// Objects are stored against the supporting block but never occupy its cell.
/// </summary>
public sealed class ObjectDefinition
{
    public ObjectDefinition(string id, string category, string icon,
        ObjectVisualDefinition visual, IReadOnlyList<BlockFace> placementFaces,
        ObjectTargetDefinition target, Vector2 positionJitter,
        bool dropSelf, bool windSway, bool unlit,
        ObjectInteraction interaction = ObjectInteraction.Break,
        BlockLightEmission lightEmission = default,
        string? pickupItem = null, string? supportFluid = null)
    {
        BlockDefinition.ValidateId(id);
        if (!Enum.IsDefined(interaction))
            throw new FormatException("Invalid Object interaction.");
        if (id.Length > ObjectCell.MaximumIdLength)
            throw new FormatException("Object ID exceeds portable chunk limit.");
        if (string.IsNullOrWhiteSpace(category))
            throw new FormatException("Object category is required.");
        ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(target);
        if (placementFaces.Count is 0 or > 6 ||
            placementFaces.Any(face => !Enum.IsDefined(face)) ||
            placementFaces.Distinct().Count() != placementFaces.Count)
            throw new FormatException("Object placement faces must be unique and valid.");
        if (!WithinRange(visual.Width, 0f, 4f, exclusiveMinimum: true) ||
            !WithinRange(visual.Height, 0f, 4f, exclusiveMinimum: true) ||
            !WithinRange(visual.BaseOffset, 0f, 2f) ||
            !WithinRange(visual.AlphaCutoff, 0f, 1f) ||
            (visual.Kind == ObjectVisualKind.CrossedSprite &&
             (visual.Planes is < 2 or > 8)) ||
            (visual.Kind == ObjectVisualKind.ExtrudedSprite &&
             (!WithinRange(visual.Depth, 0f, 4f, exclusiveMinimum: true) ||
              visual.Planes != 1)))
            throw new FormatException("Invalid object visual dimensions or alpha cutoff.");
        if (!PositiveTarget(target.Size) ||
            !Finite(target.CenterOffset) ||
            target.Size.X > 8f || target.Size.Y > 8f || target.Size.Z > 8f ||
            !WithinRange(positionJitter.X, 0f, .45f) ||
            !WithinRange(positionJitter.Y, 0f, .45f))
            throw new FormatException("Invalid object target or position jitter.");

        Id = id;
        Category = category;
        Icon = PackContentFields.ResourcePath(icon, "icon");
        Visual = visual with {
            Texture = PackContentFields.ResourcePath(visual.Texture, "visual.texture")
        };
        PlacementFaces = Array.AsReadOnly(placementFaces.ToArray());
        Target = target;
        PositionJitter = positionJitter;
        DropSelf = dropSelf;
        WindSway = windSway;
        Unlit = unlit;
        Interaction = interaction;
        LightEmission = lightEmission;
        if (pickupItem is not null) BlockDefinition.ValidateId(pickupItem);
        if (supportFluid is not null) BlockDefinition.ValidateId(supportFluid);
        PickupItem = pickupItem;
        SupportFluid = supportFluid;
    }

    public string Id { get; }
    public string Category { get; }
    public string Icon { get; }
    public ObjectVisualDefinition Visual { get; }
    public IReadOnlyList<BlockFace> PlacementFaces { get; }
    public ObjectTargetDefinition Target { get; }
    public Vector2 PositionJitter { get; }
    public bool DropSelf { get; }
    public bool WindSway { get; }
    public bool Unlit { get; }
    public ObjectInteraction Interaction { get; }
    public BlockLightEmission LightEmission { get; }
    public string? PickupItem { get; }
    public string? SupportFluid { get; }
    public InventoryEntry RewardEntry => PickupItem is { } item
        ? InventoryEntry.FromItem(item) : InventoryEntry.FromObject(Id);

    public bool Supports(BlockFace face) => PlacementFaces.Contains(face);

    public static ObjectDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var visual = PackContentFields.RequiredObject(root, "visual");
        var kind = PackContentFields.RequiredString(visual, "type") switch
        {
            "crossedSprite" => ObjectVisualKind.CrossedSprite,
            "extrudedSprite" => ObjectVisualKind.ExtrudedSprite,
            _ => throw new FormatException("Unsupported Object visual type."),
        };
        var texture = PackContentFields.RequiredString(visual, "texture");
        var width = kind == ObjectVisualKind.CrossedSprite
            ? ReadFloat(visual, "width", .75f)
            : ReadVector(visual, "size", new Vector2(.75f, .75f)).X;
        var height = kind == ObjectVisualKind.CrossedSprite
            ? ReadFloat(visual, "height", .75f)
            : ReadFloat(visual, "height", .012f);
        var planes = kind == ObjectVisualKind.CrossedSprite
            ? ReadInt(visual, "planes", 2) : 1;
        var depth = kind == ObjectVisualKind.ExtrudedSprite
            ? ReadVector(visual, "size", new Vector2(.75f, .75f)).Y : 0f;
        var baseOffset = ReadFloat(visual, "baseOffset", 0f);
        var alpha = ReadFloat(visual, "alphaCutoff", .5f);
        var placementFaces = ReadFaces(root);
        var target = root.TryGetProperty("target", out var targetValue)
            ? new ObjectTargetDefinition(
                ReadVector3(targetValue, "size", new Vector3(.75f)),
                ReadVector3(targetValue, "centerOffset", new Vector3(0f, .375f, 0f)))
            : kind == ObjectVisualKind.ExtrudedSprite
                ? new ObjectTargetDefinition(
                    new Vector3(width, MathF.Max(.14f, height), depth),
                    new Vector3(0f, MathF.Max(.14f, height) * .5f, 0f))
                : new ObjectTargetDefinition(new Vector3(.75f), new Vector3(0f, .375f, 0f));
        var jitter = ReadVector(root, "positionJitter", Vector2.Zero);
        return new ObjectDefinition(
            PackContentFields.Id(root),
            PackContentFields.RequiredString(root, "category"),
            PackContentFields.ResourcePath(
                PackContentFields.RequiredString(root, "icon"), "icon"),
            new ObjectVisualDefinition(kind, texture, width, height, planes, baseOffset, alpha, depth),
            placementFaces, target, jitter,
            ReadBool(root, "dropSelf", true),
            ReadBool(root, "windSway", false),
            ReadBool(root, "unlit", false),
            PackContentFields.OptionalString(root, "interaction") switch
            {
                null or "break" => ObjectInteraction.Break,
                "pickup" => ObjectInteraction.Pickup,
                _ => throw new FormatException("Unknown Object interaction."),
            },
            BlockLightEmissionJson.Parse(root, PackContentFields.Id(root)),
            PackContentFields.OptionalString(root, "pickupItem"),
            PackContentFields.OptionalString(root, "supportFluid"));
    }

    private static bool PositiveTarget(Vector3 value) =>
        Finite(value) && value.X > 0f && value.Y > 0f && value.Z > 0f;

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool WithinRange(float value, float min, float max,
        bool exclusiveMinimum = false) =>
        float.IsFinite(value) &&
        (exclusiveMinimum ? value > min : value >= min) && value <= max;

    private static bool ReadBool(JsonElement root, string key, bool fallback)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException($"Invalid Object boolean: {key}"),
        };
    }

    private static int ReadInt(JsonElement root, string key, int fallback)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
            return result;
        throw new FormatException($"Invalid Object integer: {key}");
    }

    private static float ReadFloat(JsonElement root, string key, float fallback)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var result) &&
            float.IsFinite(result))
            return result;
        throw new FormatException($"Invalid Object number: {key}");
    }

    private static Vector2 ReadVector(JsonElement root, string key, Vector2 fallback)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
            throw new FormatException($"Invalid Object vector: {key}");
        return new Vector2(ReadAt(value, 0), ReadAt(value, 1));
    }

    private static Vector3 ReadVector3(JsonElement root, string key, Vector3 fallback)
    {
        if (!root.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3)
            throw new FormatException($"Invalid Object vector: {key}");
        return new Vector3(ReadAt(value, 0), ReadAt(value, 1), ReadAt(value, 2));
    }

    private static float ReadAt(JsonElement array, int index)
    {
        var value = array[index];
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetSingle(out var result) || !float.IsFinite(result))
            throw new FormatException("Object geometry requires finite numeric vectors.");
        return result;
    }

    private static IReadOnlyList<BlockFace> ReadFaces(JsonElement root)
    {
        if (!root.TryGetProperty("placementFaces", out var array))
            return [BlockFace.Top];
        if (array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() is 0 or > 6)
            throw new FormatException("Invalid Object placementFaces.");
        var faces = new List<BlockFace>();
        foreach (var value in array.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
                throw new FormatException("Invalid Object placement face.");
            var face = value.GetString() switch
            {
                "top" => BlockFace.Top,
                "bottom" => BlockFace.Bottom,
                "left" => BlockFace.Left,
                "right" => BlockFace.Right,
                "front" => BlockFace.Front,
                "back" => BlockFace.Back,
                _ => throw new FormatException("Unknown Object placement face."),
            };
            if (faces.Contains(face))
                throw new FormatException("Duplicate Object placement face.");
            faces.Add(face);
        }
        return faces;
    }
}
