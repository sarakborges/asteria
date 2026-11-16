using System.Numerics;
using System.Text.Json;
using Asteria.Core.World;

namespace Asteria.Core.Content;

/// <summary>
/// A pack-authored blocky wearable part in the local [-.5, .5] coordinates
/// of an existing animated player cuboid. Visuals do not grant gameplay stats.
/// </summary>
public sealed record EquipmentVisualPart(
    string Mesh, Vector3 Size, Vector3 Offset, uint Rgb);

public sealed class EquipmentVisualDefinition
{
    private static readonly IReadOnlyDictionary<EquipmentSlot, string[]> AllowedParts =
        new Dictionary<EquipmentSlot, string[]>
        {
            [EquipmentSlot.Helmet] = ["HeadMesh"],
            [EquipmentSlot.Chest] = ["BodyMesh", "RightArmMesh", "LeftArmMesh"],
            [EquipmentSlot.Legs] = ["RightLegMesh", "LeftLegMesh"],
            [EquipmentSlot.Boots] = ["RightLegMesh", "LeftLegMesh"],
        };
    
    private EquipmentVisualDefinition(EquipmentVisualPart[] parts) =>
        Parts = Array.AsReadOnly(parts);

    public IReadOnlyList<EquipmentVisualPart> Parts { get; }

    public static EquipmentVisualDefinition Parse(JsonElement root, EquipmentSlot slot)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("parts", out var elements) ||
            elements.ValueKind != JsonValueKind.Array ||
            elements.GetArrayLength() is < 1 or > 8)
            throw new FormatException("equipmentVisual.parts must contain 1..8 cuboids.");

        var allowed = AllowedParts[slot];
        var parts = new List<EquipmentVisualPart>(elements.GetArrayLength());
        foreach (var element in elements.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new FormatException("Equipment cuboid must be an object.");

            var mesh = PackContentFields.RequiredString(element, "mesh");
            if (!allowed.Contains(mesh, StringComparer.Ordinal))
                throw new FormatException($"Equipment for {slot} cannot attach to '{mesh}'.");

            var size = ReadVector(element, "size", positive: true);
            var offset = ReadVector(element, "offset", positive: false);
            var color = PackContentFields.RequiredString(element, "color");
            if (color.Length != 7 || color[0] != '#' ||
                !uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var rgb))
                throw new FormatException("Equipment color must be #RRGGBB.");

            parts.Add(new EquipmentVisualPart(mesh, size, offset, rgb));
        }
        return new EquipmentVisualDefinition(parts.ToArray());
    }

    private static Vector3 ReadVector(JsonElement source, string property, bool positive)
    {
        if (!source.TryGetProperty(property, out var array) ||
            array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 3)
            throw new FormatException($"Equipment {property} must have exactly three numbers.");

        Span<float> xyz = stackalloc float[3];
        var i = 0;
        foreach (var number in array.EnumerateArray())
        {
            if (number.ValueKind != JsonValueKind.Number ||
                !number.TryGetSingle(out var value) || !float.IsFinite(value) ||
                (positive ? value is < 0.01f or > 1.35f : MathF.Abs(value) > 0.5f))
                throw new FormatException($"Equipment {property} is out of allowed bounds.");
            xyz[i++] = value;
        }
        return new Vector3(xyz[0], xyz[1], xyz[2]);
    }
}
