using System.Text.Json;

namespace Asteria.Core.World;

public static class BiomeDefinitionJson
{
    public static BiomeDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document =
            JsonDocument.Parse(json);
        var root =
            document.RootElement;

        if (root.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "Biome definition root must be an object.");
        }

        return new BiomeDefinition(
            RequiredString(root, "id"),
            ParseSurfaceLayout(
                GetRequiredObject(
                    root,
                    "surfaceLayout")),
            ParseTerrain(
                GetRequiredObject(
                    root,
                    "surfaceTerrain")),
            ParseLayers(
                GetRequiredArray(
                    root,
                    "surfaceLayers")),
            ParseDecorations(root),
            ParseTints(root),
            ParseTerrain3d(root));
    }

    private static BiomeSurfaceLayoutDefinition
        ParseSurfaceLayout(
            JsonElement value)
    {
        var region =
            value.TryGetProperty(
                "regionSize",
                out var authoredRegion)
                ? EnsureObject(
                    authoredRegion,
                    "regionSize")
                : default;

        return new BiomeSurfaceLayoutDefinition(
            OptionalSingle(
                value,
                "weight") ??
            1f,
            region.ValueKind ==
                JsonValueKind.Object
                ? RequiredUInt32(
                    region,
                    "min")
                : 192,
            region.ValueKind ==
                JsonValueKind.Object
                ? RequiredUInt32(
                    region,
                    "max")
                : 384,
            OptionalStringArray(
                value,
                "cannotBorder"));
    }

    private static BiomeTerrainDefinition ParseTerrain(
        JsonElement value) =>
        new(
            RequiredSingle(
                value,
                "baseHeightOffset"),
            RequiredSingle(
                value,
                "macroAmplitude"),
            RequiredUInt32(
                value,
                "macroScale"),
            RequiredSingle(
                value,
                "detailAmplitude"),
            RequiredUInt32(
                value,
                "detailScale"));

    private static BiomeTerrain3dDefinition? ParseTerrain3d(
        JsonElement root)
    {
        if (!root.TryGetProperty("terrain3d", out var terrain) ||
            terrain.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        terrain = EnsureObject(terrain, "terrain3d");
        if (!terrain.TryGetProperty("floatingFormation", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return new BiomeTerrain3dDefinition();
        }

        value = EnsureObject(value, "terrain3d.floatingFormation");
        return new BiomeTerrain3dDefinition(
            new BiomeFloatingFormationDefinition(
                RequiredInt32(value, "minY"),
                RequiredInt32(value, "maxY"),
                RequiredUInt32(value, "horizontalScale"),
                RequiredUInt32(value, "detailScale"),
                RequiredSingle(value, "coverage"),
                RequiredSingle(value, "roughness"),
                RequiredSingle(value, "densityScale")));
    }

    private static IReadOnlyList<BiomeSurfaceLayerDefinition>
        ParseLayers(
            JsonElement array)
    {
        var layers =
            new List<BiomeSurfaceLayerDefinition>();

        foreach (var value in
                 array.EnumerateArray())
        {
            if (value.ValueKind !=
                JsonValueKind.Object)
            {
                throw new FormatException(
                    "surfaceLayers entries must be objects.");
            }

            layers.Add(
                new BiomeSurfaceLayerDefinition(
                    RequiredString(
                        value,
                        "block"),
                    OptionalUInt32(
                        value,
                        "depth"),
                    ParsePatch(value)));
        }

        return layers;
    }

    private static BiomeSurfacePatchDefinition?
        ParsePatch(
            JsonElement layer)
    {
        if (!layer.TryGetProperty(
                "patch",
                out var patch) ||
            patch.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        patch =
            EnsureObject(
                patch,
                "patch");

        return new BiomeSurfacePatchDefinition(
            RequiredUInt32(
                patch,
                "spacing"),
            RequiredUInt32(
                patch,
                "radius"),
            OptionalUInt32(
                patch,
                "jitter") ??
            0,
            OptionalSingle(
                patch,
                "chance") ??
            1f,
            RequiredStringArray(
                patch,
                "blocks"));
    }

    private static BiomeTintPaletteDefinition
        ParseTints(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "tints",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return BiomeTintPaletteDefinition.Empty;
        }

        value =
            EnsureObject(
                value,
                "tints");

        return new BiomeTintPaletteDefinition(
            OptionalTintColor(
                value,
                "grass"),
            OptionalTintColor(
                value,
                "leaf"),
            OptionalTintColor(
                value,
                "foliage"));
    }

    private static BiomeTintColor? OptionalTintColor(
        JsonElement value,
        string name)
    {
        if (!value.TryGetProperty(
                name,
                out var property) ||
            property.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind !=
            JsonValueKind.String)
        {
            throw new FormatException(
                $"tints.{name} must be a six-digit RGB hex string.");
        }

        return BiomeTintColor.Parse(
            property.GetString()!);
    }

    private static IReadOnlyList<BiomeDecorationDefinition>
        ParseDecorations(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "decorations",
                out var array))
        {
            return Array.Empty<BiomeDecorationDefinition>();
        }

        array =
            EnsureArray(
                array,
                "decorations");

        var decorations =
            new List<BiomeDecorationDefinition>();

        foreach (var value in
                 array.EnumerateArray())
        {
            if (value.ValueKind !=
                JsonValueKind.Object)
            {
                throw new FormatException(
                    "decorations entries must be objects.");
            }

            decorations.Add(
                new BiomeDecorationDefinition(
                    RequiredString(
                        value,
                        "block"),
                    RequiredSingle(
                        value,
                        "chance"),
                    RequiredStringArray(
                        value,
                        "surfaceBlocks")));
        }

        return decorations;
    }

    private static JsonElement GetRequiredObject(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value))
        {
            throw new FormatException(
                $"Missing required property: {name}");
        }

        return EnsureObject(
            value,
            name);
    }

    private static JsonElement EnsureObject(
        JsonElement value,
        string label)
    {
        if (value.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                $"{label} must be an object.");
        }

        return value;
    }

    private static JsonElement GetRequiredArray(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value))
        {
            throw new FormatException(
                $"Missing required property: {name}");
        }

        return EnsureArray(
            value,
            name);
    }

    private static JsonElement EnsureArray(
        JsonElement value,
        string label)
    {
        if (value.ValueKind !=
            JsonValueKind.Array)
        {
            throw new FormatException(
                $"{label} must be an array.");
        }

        return value;
    }

    private static string RequiredString(
        JsonElement value,
        string name)
    {
        if (!value.TryGetProperty(
                name,
                out var property) ||
            property.ValueKind !=
                JsonValueKind.String)
        {
            throw new FormatException(
                $"{name} must be a string.");
        }

        return property.GetString()!;
    }

    private static float RequiredSingle(
        JsonElement value,
        string name) =>
        OptionalSingle(
            value,
            name) ??
        throw new FormatException(
            $"{name} must be a number.");

    private static float? OptionalSingle(
        JsonElement value,
        string name)
    {
        if (!value.TryGetProperty(
                name,
                out var property) ||
            property.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind !=
                JsonValueKind.Number ||
            !property.TryGetSingle(
                out var result))
        {
            throw new FormatException(
                $"{name} must be a number.");
        }

        return result;
    }

    private static int RequiredInt32(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
        {
            throw new FormatException(
                $"{name} must be a signed 32-bit integer.");
        }

        return result;
    }

    private static uint RequiredUInt32(
        JsonElement value,
        string name) =>
        OptionalUInt32(
            value,
            name) ??
        throw new FormatException(
            $"{name} must be an unsigned integer.");

    private static uint? OptionalUInt32(
        JsonElement value,
        string name)
    {
        if (!value.TryGetProperty(
                name,
                out var property) ||
            property.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind !=
                JsonValueKind.Number ||
            !property.TryGetUInt32(
                out var result))
        {
            throw new FormatException(
                $"{name} must be an unsigned integer.");
        }

        return result;
    }

    private static IReadOnlyList<string>
        OptionalStringArray(
            JsonElement value,
            string name)
    {
        if (!value.TryGetProperty(
                name,
                out var property))
        {
            return Array.Empty<string>();
        }

        return RequiredStringArray(
            property,
            name);
    }

    private static IReadOnlyList<string>
        RequiredStringArray(
            JsonElement value,
            string name)
    {
        var array =
            value.ValueKind ==
                JsonValueKind.Array
                ? value
                : value.TryGetProperty(
                    name,
                    out var nested)
                    ? nested
                    : default;

        if (array.ValueKind !=
            JsonValueKind.Array)
        {
            throw new FormatException(
                $"{name} must be an array.");
        }

        return array
            .EnumerateArray()
            .Select(item =>
                item.ValueKind ==
                    JsonValueKind.String
                    ? item.GetString()!
                    : throw new FormatException(
                        $"{name} can contain only strings."))
            .ToArray();
    }
}
