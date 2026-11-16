using System.Text.Json;

namespace Asteria.Core.Content;

/// <summary>Validation shared by pack-owned entity definitions.</summary>
internal static class PackContentFields
{
    public static string RequiredString(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Missing non-empty string: {field}");
        }

        return value.GetString()!;
    }

    public static string? OptionalString(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Invalid string: {field}");
        }

        return value.GetString();
    }

    public static JsonElement RequiredObject(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Missing object: {field}");
        }

        return value;
    }

    public static IReadOnlyDictionary<string, string> Strings(JsonElement root, string field)
    {
        var values = RequiredObject(root, field);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in values.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) ||
                property.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                throw new FormatException($"Invalid {field}.{property.Name}");
            }

            result.Add(property.Name, property.Value.GetString()!);
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(result);
    }

    public static string Id(JsonElement root) =>
        Namespaced(RequiredString(root, "id"), "id");

    public static string Namespaced(string id, string field)
    {
        var separator = id.IndexOf(':');
        if (separator <= 0 ||
            separator == id.Length - 1 ||
            id.IndexOf(':', separator + 1) >= 0 ||
            id != id.Trim())
        {
            throw new FormatException($"{field} requires a namespaced id: {id}");
        }

        return id;
    }

    public static string ResourcePath(string path, string field)
    {
        if (path.StartsWith('/') ||
            path.Contains('\\') ||
            path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new FormatException($"Invalid resource path {field}: {path}");
        }

        return path;
    }

    public static int PositiveInt(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result) ||
            result <= 0)
        {
            throw new FormatException($"{field} must be a positive integer");
        }

        return result;
    }

    public static float PositiveFloat(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetSingle(out var result) ||
            !float.IsFinite(result) ||
            result <= 0f)
        {
            throw new FormatException($"{field} must be a positive number");
        }

        return result;
    }

    public static float OptionalPositiveFloat(JsonElement root, string field, float fallback) =>
        root.TryGetProperty(field, out var value)
            ? PositiveFloat(root, field)
            : fallback;

    public static float NonNegativeFloat(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetSingle(out var result) ||
            !float.IsFinite(result) ||
            result < 0f)
        {
            throw new FormatException($"{field} must be a non-negative number");
        }

        return result;
    }
}
