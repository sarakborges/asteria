using System.Text.Json;

namespace Asteria.Core.World;

public static class SurfaceHabitatDefinitionJson
{
    public static SurfaceHabitatDefinition? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("surfaceHabitats", out var value) ||
            value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new FormatException("surfaceHabitats must be an object.");
        if (!value.TryGetProperty("bands", out var bands) ||
            bands.ValueKind != JsonValueKind.Array)
            throw new FormatException("surfaceHabitats.bands must be an array.");
        return new SurfaceHabitatDefinition(
            RequiredInt(value, "scale"),
            OptionalFloat(value, "transitionWidth") ?? 0f,
            bands.EnumerateArray().Select(band =>
            {
                if (band.ValueKind != JsonValueKind.Object)
                    throw new FormatException("surfaceHabitats bands must be objects.");
                return new SurfaceHabitatBand(
                    RequiredString(band, "id"), RequiredFloat(band, "maximum"));
            }));
    }

    public static SurfaceHabitatWeights? ParseWeights(JsonElement root)
    {
        if (!root.TryGetProperty("habitatWeights", out var value) ||
            value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new FormatException("habitatWeights must be an object.");
        return new SurfaceHabitatWeights(value.EnumerateObject()
            .Select(entry => new KeyValuePair<string, float>(
                entry.Name,
                entry.Value.ValueKind == JsonValueKind.Number &&
                entry.Value.TryGetSingle(out var weight)
                    ? weight
                    : throw new FormatException(
                        $"habitatWeights.{entry.Name} must be a number."))));
    }

    private static string RequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var member) &&
        member.ValueKind == JsonValueKind.String
            ? member.GetString()!
            : throw new FormatException($"{name} must be a string.");

    private static float RequiredFloat(JsonElement value, string name) =>
        OptionalFloat(value, name) ??
        throw new FormatException($"{name} must be a number.");

    private static float? OptionalFloat(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var member)) return null;
        if (member.ValueKind != JsonValueKind.Number ||
            !member.TryGetSingle(out var number))
            throw new FormatException($"{name} must be a number.");
        return number;
    }

    private static int RequiredInt(JsonElement value, string name) =>
        value.TryGetProperty(name, out var member) &&
        member.ValueKind == JsonValueKind.Number &&
        member.TryGetInt32(out var number)
            ? number
            : throw new FormatException($"{name} must be an integer.");
}
