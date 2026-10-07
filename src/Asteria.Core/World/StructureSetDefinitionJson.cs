using System.Text.Json;

namespace Asteria.Core.World;

public static class StructureSetDefinitionJson
{
    public static StructureSetDefinition Parse(
        string json)
    {
        ArgumentNullException.ThrowIfNull(
            json);

        using var document =
            JsonDocument.Parse(
                json);
        var root =
            document.RootElement;

        if (root.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "Structure-set root must be an object.");
        }

        EnsureKnownProperties(
            root,
            "structure set",
            "id",
            "priority",
            "conflictGroups",
            "reserveSpace",
            "elements");

        var elements =
            RequiredArray(
                root,
                "elements")
                .EnumerateArray()
                .Select(
                    ParseElement)
                .ToArray();

        return new StructureSetDefinition(
            RequiredString(
                root,
                "id"),
            OptionalInt32(
                root,
                "priority") ??
            0,
            OptionalStringArray(
                root,
                "conflictGroups"),
            OptionalBoolean(
                root,
                "reserveSpace") ??
            false,
            elements);
    }

    private static StructureSetElementDefinition
        ParseElement(
            JsonElement value)
    {
        if (value.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "Structure-set element must be an object.");
        }

        EnsureKnownProperties(
            value,
            "structure-set element",
            "id",
            "structure",
            "count",
            "chance",
            "required",
            "placement");

        return new StructureSetElementDefinition(
            RequiredString(
                value,
                "id"),
            RequiredString(
                value,
                "structure"),
            ParseCount(
                value),
            OptionalSingle(
                value,
                "chance") ??
            1f,
            OptionalBoolean(
                value,
                "required") ??
            false,
            ParsePlacement(
                value));
    }

    private static StructureSetCountDefinition
        ParseCount(
            JsonElement element)
    {
        if (!element.TryGetProperty(
                "count",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return new StructureSetCountDefinition(
                minimum: 1,
                maximum: 1);
        }

        value =
            RequiredObject(
                element,
                "count");
        EnsureKnownProperties(
            value,
            "structure-set count",
            "min",
            "max");

        return new StructureSetCountDefinition(
            OptionalInt32(
                value,
                "min") ??
            1,
            OptionalInt32(
                value,
                "max") ??
            1);
    }

    private static StructureSetElementPlacementDefinition
        ParsePlacement(
            JsonElement element)
    {
        if (!element.TryGetProperty(
                "placement",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return new StructureSetElementPlacementDefinition();
        }

        value =
            RequiredObject(
                element,
                "placement");
        EnsureKnownProperties(
            value,
            "structure-set placement",
            "relativeTo",
            "minDistance",
            "maxDistance",
            "minSeparation",
            "attempts",
            "allowOverlap");

        return new StructureSetElementPlacementDefinition(
            OptionalString(
                value,
                "relativeTo") ??
            "origin",
            OptionalInt32(
                value,
                "minDistance") ??
            0,
            OptionalInt32(
                value,
                "maxDistance") ??
            0,
            OptionalInt32(
                value,
                "minSeparation") ??
            0,
            OptionalInt32(
                value,
                "attempts") ??
            24,
            OptionalBoolean(
                value,
                "allowOverlap") ??
            false);
    }

    private static JsonElement RequiredObject(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Object)
        {
            throw new FormatException(
                $"{name} must be an object.");
        }

        return value;
    }

    private static JsonElement RequiredArray(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Array)
        {
            throw new FormatException(
                $"{name} must be an array.");
        }

        return value;
    }

    private static string RequiredString(
        JsonElement parent,
        string name) =>
        OptionalString(
            parent,
            name) ??
        throw new FormatException(
            $"{name} is required.");

    private static string? OptionalString(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind !=
            JsonValueKind.String)
        {
            throw new FormatException(
                $"{name} must be a string.");
        }

        return value.GetString();
    }

    private static int? OptionalInt32(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind !=
                JsonValueKind.Number ||
            !value.TryGetInt32(
                out var result))
        {
            throw new FormatException(
                $"{name} must be an integer.");
        }

        return result;
    }

    private static float? OptionalSingle(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind !=
                JsonValueKind.Number ||
            !value.TryGetSingle(
                out var result) ||
            !float.IsFinite(
                result))
        {
            throw new FormatException(
                $"{name} must be a finite number.");
        }

        return result;
    }

    private static bool? OptionalBoolean(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is not
            (JsonValueKind.True or
             JsonValueKind.False))
        {
            throw new FormatException(
                $"{name} must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static IReadOnlyList<string>
        OptionalStringArray(
            JsonElement parent,
            string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind !=
            JsonValueKind.Array)
        {
            throw new FormatException(
                $"{name} must be an array.");
        }

        return value
            .EnumerateArray()
            .Select(
                item =>
                    item.ValueKind ==
                        JsonValueKind.String
                        ? item.GetString()!
                        : throw new FormatException(
                            $"{name} may contain only strings."))
            .ToArray();
    }

    private static void EnsureKnownProperties(
        JsonElement value,
        string context,
        params string[] known)
    {
        var allowed =
            known.ToHashSet(
                StringComparer.Ordinal);

        foreach (var property in
                 value.EnumerateObject())
        {
            if (!allowed.Contains(
                    property.Name))
            {
                throw new FormatException(
                    $"{context} contains unsupported property {property.Name}.");
            }
        }
    }
}
