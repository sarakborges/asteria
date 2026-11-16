using System.Numerics;
using System.Text.Json;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>Immutable pack-authored HSI dye, converted to sRGB once.</summary>
public sealed class DyeDefinition
{
    public DyeDefinition(string id, float hue, float saturation, float intensity)
    {
        BlockDefinition.ValidateId(id);
        if (!float.IsFinite(hue) || !float.IsFinite(saturation) ||
            !float.IsFinite(intensity) || saturation < 0 || saturation > 1 ||
            intensity < 0 || intensity > 1)
            throw new ArgumentOutOfRangeException(nameof(hue), "Invalid HSI dye.");
        Id = id;
        Hue = ((hue % 360f) + 360f) % 360f;
        Saturation = saturation;
        Intensity = intensity;
        Rgb = ToRgb(Hue, Saturation, Intensity);
    }

    public string Id { get; }
    public float Hue { get; }
    public float Saturation { get; }
    public float Intensity { get; }
    public Vector3 Rgb { get; }

    public static DyeDefinition Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var color = PackContentFields.RequiredObject(root, "color");
        return new DyeDefinition(PackContentFields.Id(root),
            color.GetProperty("hue").GetSingle(),
            color.GetProperty("saturation").GetSingle(),
            color.GetProperty("intensity").GetSingle());
    }

    private static Vector3 ToRgb(float hue, float saturation, float intensity)
    {
        if (intensity == 0f) return Vector3.Zero;
        if (saturation == 0f) return new Vector3(intensity);
        const float sector = MathF.Tau / 3f;
        float red, green, blue;
        var rad = hue * MathF.PI / 180f;
        if (hue < 120f)
        {
            blue = intensity * (1f - saturation);
            red = intensity * (1f + saturation * MathF.Cos(rad) /
                MathF.Max(MathF.Cos(MathF.PI / 3f - rad), float.Epsilon));
            green = 3f * intensity - red - blue;
        }
        else if (hue < 240f)
        {
            var shifted = rad - sector;
            red = intensity * (1f - saturation);
            green = intensity * (1f + saturation * MathF.Cos(shifted) /
                MathF.Max(MathF.Cos(MathF.PI / 3f - shifted), float.Epsilon));
            blue = 3f * intensity - red - green;
        }
        else
        {
            var shifted = rad - 2f * sector;
            green = intensity * (1f - saturation);
            blue = intensity * (1f + saturation * MathF.Cos(shifted) /
                MathF.Max(MathF.Cos(MathF.PI / 3f - shifted), float.Epsilon));
            red = 3f * intensity - green - blue;
        }
        return Vector3.Clamp(new Vector3(red, green, blue), Vector3.Zero, Vector3.One);
    }
}

public sealed class DyeRegistry
{
    private readonly Dictionary<string, DyeDefinition> _definitions;

    public DyeRegistry(IEnumerable<DyeDefinition> definitions)
    {
        _definitions = new(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!_definitions.TryAdd(definition.Id, definition))
                throw new ArgumentException($"Duplicate dye: {definition.Id}", nameof(definitions));
        }
    }

    public static DyeRegistry FromJson(IEnumerable<string> documents) =>
        new(documents.Select(DyeDefinition.Parse));

    public DyeDefinition Get(string id) =>
        _definitions.TryGetValue(id, out var dye) ? dye :
            throw new KeyNotFoundException($"Unknown dye: {id}");

    public bool TryGet(string id, out DyeDefinition? dye) =>
        _definitions.TryGetValue(id, out dye);

    public IReadOnlyList<DyeDefinition> Palette =>
        _definitions.Values.OrderBy(dye => dye.Saturation <= .001f)
            .ThenBy(dye => dye.Saturation <= .001f ? -dye.Intensity : dye.Hue)
            .ThenByDescending(dye => dye.Saturation)
            .ThenByDescending(dye => dye.Intensity)
            .ThenBy(dye => dye.Id, StringComparer.Ordinal).ToArray();
}
