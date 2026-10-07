using System.Text.Json;

namespace Asteria.Core.World;

public static class StructureDefinitionJson
{
    private const int MaximumLayerCount = 256;
    private const int MaximumGridWidth = 128;
    private const int MaximumGridDepth = 128;
    private const int MaximumPaletteEntries = 128;

    public static StructureDefinition Parse(
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
                "Structure definition root must be an object.");
        }

        EnsureKnownProperties(
            root,
            "structure",
            "id",
            "groupId",
            "rotation",
            "restrictions",
            "anchor",
            "palette",
            "layers");

        var id =
            RequiredString(
                root,
                "id");
        var anchor =
            ParseAnchor(
                root);
        var palette =
            ParsePalette(
                root);
        var voxels =
            ParseLayers(
                root,
                anchor,
                palette);

        return new StructureDefinition(
            id,
            OptionalBoolean(
                root,
                "rotation") ??
            false,
            anchor,
            voxels,
            ParseRestrictions(
                root),
            OptionalString(
                root,
                "groupId"));
    }

    private static StructureAnchor ParseAnchor(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "anchor",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return default;
        }

        value =
            RequiredObject(
                root,
                "anchor");
        EnsureKnownProperties(
            value,
            "anchor",
            "x",
            "y",
            "z");
        return new StructureAnchor(
            OptionalInt32(
                value,
                "x") ??
            0,
            OptionalInt32(
                value,
                "y") ??
            0,
            OptionalInt32(
                value,
                "z") ??
            0);
    }

    private static StructureRestrictionsDefinition
        ParseRestrictions(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "restrictions",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return new StructureRestrictionsDefinition();
        }

        value =
            RequiredObject(
                root,
                "restrictions");
        EnsureKnownProperties(
            value,
            "restrictions",
            "maxSlope",
            "requiresDryGround",
            "requiredBiomeCoverage");
        return new StructureRestrictionsDefinition(
            OptionalInt32(
                value,
                "maxSlope") ??
            1,
            OptionalBoolean(
                value,
                "requiresDryGround") ??
            true,
            OptionalSingle(
                value,
                "requiredBiomeCoverage") ??
            0f);
    }

    private static IReadOnlyDictionary<
        char,
        PaletteEntry> ParsePalette(
            JsonElement root)
    {
        var value =
            RequiredObject(
                root,
                "palette");
        if (value.EnumerateObject().Count() >
            MaximumPaletteEntries)
        {
            throw new FormatException(
                $"Structure palette may contain at most {MaximumPaletteEntries} entries.");
        }

        var entries =
            new Dictionary<
                char,
                PaletteEntry>();

        foreach (var property in
                 value.EnumerateObject())
        {
            if (property.Name.Length != 1 ||
                property.Name[0] == '.')
            {
                throw new FormatException(
                    "Structure palette keys must be one non-dot character.");
            }

            if (property.Value.ValueKind !=
                JsonValueKind.Object)
            {
                throw new FormatException(
                    $"Structure palette {property.Name} must be an object.");
            }

            foreach (var field in
                     property.Value.EnumerateObject())
            {
                if (field.Name is not
                    ("block" or "orientation"))
                {
                    throw new FormatException(
                        $"Unsupported block-template palette field: {field.Name}.");
                }
            }

            var block =
                RequiredString(
                    property.Value,
                    "block");
            var orientation =
                OptionalString(
                    property.Value,
                    "orientation") switch
                {
                    null or "y" =>
                        BlockOrientation.Y,
                    "x" =>
                        BlockOrientation.X,
                    "z" =>
                        BlockOrientation.Z,
                    var authored =>
                        throw new FormatException(
                            $"Unknown structure block orientation: {authored}"),
                };

            entries.Add(
                property.Name[0],
                new PaletteEntry(
                    block,
                    orientation));
        }

        if (entries.Count == 0)
        {
            throw new FormatException(
                "Structure palette cannot be empty.");
        }

        return entries;
    }

    private static IReadOnlyList<StructureVoxelDefinition>
        ParseLayers(
            JsonElement root,
            StructureAnchor anchor,
            IReadOnlyDictionary<
                char,
                PaletteEntry> palette)
    {
        if (!root.TryGetProperty(
                "layers",
                out var array) ||
            array.ValueKind !=
                JsonValueKind.Array)
        {
            throw new FormatException(
                "Structure layers must be an array.");
        }

        if (array.GetArrayLength() >
            MaximumLayerCount)
        {
            throw new FormatException(
                $"Structure may contain at most {MaximumLayerCount} layers.");
        }

        var voxels =
            new List<StructureVoxelDefinition>();
        int? width = null;
        int? depth = null;
        var authoredY =
            new HashSet<int>();

        foreach (var layer in
                 array.EnumerateArray())
        {
            if (layer.ValueKind !=
                JsonValueKind.Object)
            {
                throw new FormatException(
                    "Structure layer entries must be objects.");
            }

            EnsureKnownProperties(
                layer,
                "structure layer",
                "y",
                "rows");

            var y =
                RequiredInt32(
                    layer,
                    "y");
            if (!authoredY.Add(
                    y))
            {
                throw new FormatException(
                    $"Structure repeats layer Y={y}.");
            }

            var rows =
                RequiredStringArray(
                    layer,
                    "rows");
            if (rows.Count == 0)
            {
                throw new FormatException(
                    "Structure layer rows cannot be empty.");
            }

            if (rows.Count >
                MaximumGridDepth)
            {
                throw new FormatException(
                    $"Structure layer depth may not exceed {MaximumGridDepth} rows.");
            }

            depth ??=
                rows.Count;
            if (depth !=
                rows.Count)
            {
                throw new FormatException(
                    "Structure layers must have the same row count.");
            }

            foreach (var row in rows)
            {
                width ??=
                    row.Length;
                if (row.Length == 0 ||
                    row.Length !=
                        width)
                {
                    throw new FormatException(
                        "Structure rows must be non-empty and share one width.");
                }

                if (row.Length >
                    MaximumGridWidth)
                {
                    throw new FormatException(
                        $"Structure row width may not exceed {MaximumGridWidth} symbols.");
                }
            }

            for (var z = 0;
                 z < rows.Count;
                 z++)
            {
                var row =
                    rows[z];

                for (var x = 0;
                     x < row.Length;
                     x++)
                {
                    var symbol =
                        row[x];
                    if (symbol == '.')
                    {
                        continue;
                    }

                    if (!palette.TryGetValue(
                            symbol,
                            out var entry))
                    {
                        throw new FormatException(
                            $"Structure layer uses undefined palette symbol: {symbol}.");
                    }

                    voxels.Add(
                        new StructureVoxelDefinition(
                            checked(
                                x -
                                anchor.X),
                            checked(
                                y -
                                anchor.Y),
                            checked(
                                z -
                                anchor.Z),
                            entry.Block,
                            entry.Orientation));
                }
            }
        }

        return voxels;
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

    private static IReadOnlyList<string>
        RequiredStringArray(
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

        return value
            .EnumerateArray()
            .Select(item =>
                item.ValueKind ==
                    JsonValueKind.String
                    ? item.GetString()!
                    : throw new FormatException(
                        $"{name} can contain only strings."))
            .ToArray();
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

        return value.ValueKind ==
            JsonValueKind.String
            ? value.GetString()
            : throw new FormatException(
                $"{name} must be a string.");
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

        return value.ValueKind is
            JsonValueKind.True or
            JsonValueKind.False
            ? value.GetBoolean()
            : throw new FormatException(
                $"{name} must be a boolean.");
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

    private static int RequiredInt32(
        JsonElement parent,
        string name) =>
        OptionalInt32(
            parent,
            name) ??
        throw new FormatException(
            $"{name} must be an integer.");

    private sealed record PaletteEntry(
        string Block,
        BlockOrientation Orientation);
}
