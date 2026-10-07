using System.Text.Json;

namespace Asteria.Core.World;

public static class StructureDefinitionJson
{
    public static StructureDefinition Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException(
                "Structure definition root must be an object.");
        }

        var palette = ParsePalette(root);
        return new StructureDefinition(
            RequiredString(root, "id"),
            OptionalString(root, "group_id"),
            OptionalBoolean(root, "rotation") ?? false,
            OptionalInt32(root, "priority") ?? 0,
            OptionalStringArray(root, "conflictGroups"),
            ParseRestrictions(root),
            ParseGeneration(root),
            ParseLayers(
                root,
                ParseAnchor(root),
                palette));
    }

    private static StructureRestrictions ParseRestrictions(
        JsonElement root)
    {
        if (!root.TryGetProperty("restrictions", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return new StructureRestrictions();
        }

        value = Object(value, "restrictions");
        return new StructureRestrictions(
            OptionalStringArray(value, "groundBlocks"),
            OptionalInt32(value, "minSlope") ?? 0,
            OptionalInt32(value, "maxSlope") ?? 1,
            OptionalBoolean(value, "requiresDryGround") ?? true,
            OptionalSingle(value, "requiredBiomeCoverage") ?? 0f);
    }

    private static StructureGenerationDefinition ParseGeneration(
        JsonElement root)
    {
        if (!root.TryGetProperty("generation", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return StructureGenerationDefinition.Default;
        }

        value = Object(value, "generation");

        var replace =
            OptionalString(value, "replacePolicy") switch
            {
                null or "any" => StructureReplacePolicy.Any,
                "air_only" => StructureReplacePolicy.AirOnly,
                "terrain" => StructureReplacePolicy.Terrain,
                var authored => throw new FormatException(
                    $"Unsupported structure replacePolicy: {authored}."),
            };

        var fluid =
            OptionalString(value, "fluidPolicy") switch
            {
                null or "displace" => StructureFluidPolicy.Displace,
                "preserve" => StructureFluidPolicy.Preserve,
                "forbid" => StructureFluidPolicy.Forbid,
                var authored => throw new FormatException(
                    $"Unsupported structure fluidPolicy: {authored}."),
            };

        return new StructureGenerationDefinition(
            replace,
            fluid,
            OptionalBoolean(value, "reserveSpace") ?? false);
    }

    private static StructureOffset ParseAnchor(
        JsonElement root)
    {
        if (!root.TryGetProperty("anchor", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        value = Object(value, "anchor");
        return new StructureOffset(
            OptionalInt32(value, "x") ?? 0,
            OptionalInt32(value, "y") ?? 0,
            OptionalInt32(value, "z") ?? 0);
    }

    private static IReadOnlyDictionary<char, PaletteEntry>
        ParsePalette(JsonElement root)
    {
        if (!root.TryGetProperty("palette", out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException(
                "Structure palette must be an object.");
        }

        var result = new Dictionary<char, PaletteEntry>();

        foreach (var property in value.EnumerateObject())
        {
            if (property.Name.Length != 1 ||
                property.Name[0] == '.')
            {
                throw new FormatException(
                    $"Structure palette key must be one non-dot character: {property.Name}.");
            }

            var entry = Object(
                property.Value,
                $"palette.{property.Name}");
            var orientation =
                OptionalString(entry, "orientation") switch
                {
                    null or "y" => BlockOrientation.Y,
                    "x" => BlockOrientation.X,
                    "z" => BlockOrientation.Z,
                    var authored => throw new FormatException(
                        $"Unsupported structure block orientation: {authored}."),
                };

            result.Add(
                property.Name[0],
                new PaletteEntry(
                    OptionalString(entry, "block"),
                    orientation));
        }

        return result;
    }

    private static IReadOnlyList<StructureVoxel>
        ParseLayers(
            JsonElement root,
            StructureOffset anchor,
            IReadOnlyDictionary<char, PaletteEntry> palette)
    {
        if (!root.TryGetProperty("layers", out var layers) ||
            layers.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException(
                "Structure layers must be an array.");
        }

        var voxels = new List<StructureVoxel>();

        foreach (var layer in layers.EnumerateArray())
        {
            var value = Object(layer, "layers[]");
            var y = RequiredInt32(value, "y");

            if (!value.TryGetProperty("rows", out var rows) ||
                rows.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException(
                    "Structure layer rows must be an array.");
            }

            var z = 0;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException(
                        "Structure layer row must be a string.");
                }

                var text = row.GetString()!;

                for (var x = 0; x < text.Length; x++)
                {
                    var symbol = text[x];
                    if (symbol == '.')
                    {
                        continue;
                    }

                    if (!palette.TryGetValue(symbol, out var entry))
                    {
                        throw new FormatException(
                            $"Structure layer uses missing palette symbol {symbol}.");
                    }

                    // Object/layer/fluid-only symbols remain authored in the file but
                    // do not become block voxels until their owning systems are ported.
                    if (entry.Block is null)
                    {
                        continue;
                    }

                    voxels.Add(
                        new StructureVoxel(
                            new StructureOffset(
                                x - anchor.X,
                                y - anchor.Y,
                                z - anchor.Z),
                            entry.Block,
                            entry.Orientation));
                }

                z++;
            }
        }

        return voxels;
    }

    private static JsonElement Object(
        JsonElement value,
        string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException(
                $"{name} must be an object.");
        }

        return value;
    }

    private static string RequiredString(
        JsonElement parent,
        string name) =>
        OptionalString(parent, name) ??
        throw new FormatException(
            $"{name} is required.");

    private static string? OptionalString(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException(
                $"{name} must be a string.");
        }

        return value.GetString();
    }

    private static bool? OptionalBoolean(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is not
            (JsonValueKind.True or JsonValueKind.False))
        {
            throw new FormatException(
                $"{name} must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static int RequiredInt32(
        JsonElement parent,
        string name) =>
        OptionalInt32(parent, name) ??
        throw new FormatException(
            $"{name} is required.");

    private static int? OptionalInt32(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
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
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetSingle(out var result) ||
            !float.IsFinite(result))
        {
            throw new FormatException(
                $"{name} must be a finite number.");
        }

        return result;
    }

    private static IReadOnlyList<string>
        OptionalStringArray(
            JsonElement parent,
            string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException(
                $"{name} must be an array.");
        }

        return value
            .EnumerateArray()
            .Select(item =>
                item.ValueKind == JsonValueKind.String
                    ? item.GetString()!
                    : throw new FormatException(
                        $"{name} may contain only strings."))
            .ToArray();
    }

    private sealed record PaletteEntry(
        string? Block,
        BlockOrientation Orientation);
}
