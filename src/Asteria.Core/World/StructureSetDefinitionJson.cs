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
                "StructureSet definition root must be an object.");
        }

        EnsureKnownProperties(
            root,
            "StructureSet",
            "id",
            "locatable",
            "priority",
            "conflictGroups",
            "reserveSpace",
            "elements");

        return new StructureSetDefinition(
            RequiredString(
                root,
                "id"),
            ParseElements(
                root),
            OptionalBoolean(
                root,
                "locatable") ??
            true,
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
            false);
    }

    private static IReadOnlyList<StructureSetElementDefinition>
        ParseElements(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "elements",
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Array)
        {
            throw new FormatException(
                "StructureSet elements must be an array.");
        }

        return value
            .EnumerateArray()
            .Select(ParseElement)
            .ToArray();
    }

    private static StructureSetElementDefinition
        ParseElement(
            JsonElement value)
    {
        if (value.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "StructureSet elements must be objects.");
        }

        EnsureKnownProperties(
            value,
            "StructureSet element",
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

    private static StructureSetCount
        ParseCount(
            JsonElement element)
    {
        if (!element.TryGetProperty(
                "count",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return StructureSetCount.One;
        }

        if (value.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "StructureSet element count must be an object.");
        }

        EnsureKnownProperties(
            value,
            "StructureSet count",
            "min",
            "max");
        return new StructureSetCount(
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

        if (value.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "StructureSet element placement must be an object.");
        }

        EnsureKnownProperties(
            value,
            "StructureSet element placement",
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
            StructureSetElementPlacementDefinition.DefaultAttempts,
            OptionalBoolean(
                value,
                "allowOverlap") ??
            false);
    }

    private static string RequiredString(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.String)
        {
            throw new FormatException(
                $"{name} must be a string.");
        }

        return value.GetString()!;
    }

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
                out var result))
        {
            throw new FormatException(
                $"{name} must be a number.");
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
            .Select(item =>
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
        params string[] allowed)
    {
        var known =
            new HashSet<string>(
                allowed,
                StringComparer.Ordinal);

        foreach (var property in
                 value.EnumerateObject())
        {
            if (!known.Contains(
                    property.Name))
            {
                throw new FormatException(
                    $"Unsupported {context} field: {property.Name}.");
            }
        }
    }
}
