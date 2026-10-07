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
            ParseSurfaceLayout(root),
            ParseTerrain(root),
            ParseLayers(root),
            ParseDecorations(root),
            ParseTints(root),
            ParseTerrain3d(root),
            ParseVolumeLayout(root),
            ParseUndergroundLayout(root),
            ParseSurfaceFluid(root));
    }

    private static PlacementLayoutValues?
        ParsePlacementLayout(
            JsonElement root,
            string propertyName)
    {
        if (!root.TryGetProperty(
                propertyName,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        value =
            EnsureObject(
                value,
                propertyName);
        var region =
            value.TryGetProperty(
                "regionSize",
                out var authoredRegion)
                ? EnsureObject(
                    authoredRegion,
                    "regionSize")
                : default;

        return new PlacementLayoutValues(
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

    private static BiomeSurfaceLayoutDefinition?
        ParseSurfaceLayout(
            JsonElement root)
    {
        var layout =
            ParsePlacementLayout(
                root,
                "surfaceLayout");

        return layout is
            { } values
            ? new BiomeSurfaceLayoutDefinition(
                values.Weight,
                values.RegionMin,
                values.RegionMax,
                values.CannotBorder)
            : null;
    }

    private static BiomeTerrainDefinition? ParseTerrain(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "surfaceTerrain",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        value =
            EnsureObject(
                value,
                "surfaceTerrain");

        if (!value.TryGetProperty(
                "type",
                out var typeValue) ||
            typeValue.ValueKind ==
                JsonValueKind.Null)
        {
            return new BiomeTerrainDefinition(
                RequiredSingle(value, "baseHeightOffset"),
                RequiredSingle(value, "macroAmplitude"),
                RequiredUInt32(value, "macroScale"),
                RequiredSingle(value, "detailAmplitude"),
                RequiredUInt32(value, "detailScale"));
        }

        if (typeValue.ValueKind !=
            JsonValueKind.String)
        {
            throw new FormatException(
                "surfaceTerrain.type must be a string.");
        }

        var type =
            typeValue.GetString()!;
        BiomeTerrainShapeDefinition shape =
            type switch
            {
                "noise" =>
                    new BiomeNoiseTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeightOffset"),
                        RequiredSingle(value, "macroAmplitude"),
                        RequiredUInt32(value, "macroScale"),
                        RequiredSingle(value, "detailAmplitude"),
                        RequiredUInt32(value, "detailScale")),
                "rolling" =>
                    new BiomeRollingTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "amplitude"),
                        RequiredSingle(value, "scale"),
                        RequiredSingle(value, "detailAmplitude"),
                        RequiredSingle(value, "detailScale")),
                "dunes" =>
                    new BiomeDunesTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "amplitude"),
                        RequiredSingle(value, "scale"),
                        RequiredSingle(value, "sharpness"),
                        RequiredSingle(value, "warpScale"),
                        RequiredSingle(value, "warpStrength"),
                        RequiredSingle(value, "detailAmplitude"),
                        RequiredSingle(value, "detailScale")),
                "ocean" =>
                    new BiomeOceanTerrainShapeDefinition(
                        RequiredSingle(value, "depth"),
                        RequiredSingle(value, "amplitude"),
                        RequiredSingle(value, "scale"),
                        RequiredSingle(value, "detailAmplitude"),
                        RequiredSingle(value, "detailScale")),
                "swamp" =>
                    new BiomeSwampTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "depth"),
                        RequiredSingle(value, "amplitude"),
                        RequiredSingle(value, "scale"),
                        RequiredSingle(value, "detailAmplitude"),
                        RequiredSingle(value, "detailScale")),
                "mountains" =>
                    new BiomeMountainsTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "amplitude"),
                        RequiredSingle(value, "scale"),
                        RequiredSingle(value, "sharpness")),
                "gorge" =>
                    new BiomeGorgeTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "depth"),
                        RequiredSingle(value, "wallHeight"),
                        RequiredSingle(value, "topAmplitude"),
                        RequiredSingle(value, "topScale"),
                        RequiredSingle(value, "floorAmplitude"),
                        RequiredSingle(value, "floorScale")),
                "alps" =>
                    new BiomeAlpsTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "amplitude"),
                        RequiredSingle(value, "scale"),
                        RequiredSingle(value, "sharpness"),
                        RequiredSingle(value, "detailAmplitude"),
                        RequiredSingle(value, "detailScale")),
                "mountain_belt" =>
                    new BiomeMountainBeltTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "amplitude"),
                        RequiredSingle(value, "scale"),
                        RequiredSingle(value, "sharpness"),
                        RequiredSingle(value, "detailAmplitude"),
                        RequiredSingle(value, "detailScale")),
                "volcano" =>
                    new BiomeVolcanoTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "height"),
                        RequiredSingle(value, "craterDepth"),
                        RequiredSingle(value, "craterRadius"),
                        RequiredSingle(value, "irregularity"),
                        RequiredSingle(value, "irregularityScale"),
                        RequiredSingle(value, "detailIrregularity"),
                        RequiredSingle(value, "detailScale"),
                        RequiredSingle(value, "craterIrregularity")),
                _ =>
                    throw new FormatException(
                        $"Unknown surfaceTerrain.type: {type}"),
            };

        return new BiomeTerrainDefinition(
            shape,
            ParseTerrainModifiers(value));
    }

    private static IReadOnlyList<BiomeTerrainModifierDefinition>
        ParseTerrainModifiers(
            JsonElement terrain)
    {
        if (!terrain.TryGetProperty(
                "modifiers",
                out var array) ||
            array.ValueKind ==
                JsonValueKind.Null)
        {
            return Array.Empty<BiomeTerrainModifierDefinition>();
        }

        array =
            EnsureArray(
                array,
                "surfaceTerrain.modifiers");
        var modifiers =
            new List<BiomeTerrainModifierDefinition>();

        foreach (var value in
                 array.EnumerateArray())
        {
            var modifier =
                EnsureObject(
                    value,
                    "surfaceTerrain.modifiers entry");
            var type =
                RequiredString(
                    modifier,
                    "type");

            modifiers.Add(
                type switch
                {
                    "height_offset" =>
                        new BiomeHeightOffsetTerrainModifierDefinition(
                            RequiredSingle(modifier, "height")),
                    "cliffs" =>
                        new BiomeCliffsTerrainModifierDefinition(
                            RequiredSingle(modifier, "scale"),
                            RequiredSingle(modifier, "threshold"),
                            RequiredSingle(modifier, "height"),
                            RequiredSingle(modifier, "edgeWidth"),
                            RequiredSingle(modifier, "warpScale"),
                            RequiredSingle(modifier, "warpStrength")),
                    _ =>
                        throw new FormatException(
                            $"Unknown surface terrain modifier type: {type}"),
                });
        }

        return modifiers;
    }

    private static BiomeVolumeLayoutDefinition?
        ParseVolumeLayout(
            JsonElement root)
    {
        var layout =
            ParsePlacementLayout(
                root,
                "volumeLayout");

        return layout is
            { } values
            ? new BiomeVolumeLayoutDefinition(
                values.Weight,
                values.RegionMin,
                values.RegionMax,
                values.CannotBorder)
            : null;
    }

    private static BiomeUndergroundLayoutDefinition?
        ParseUndergroundLayout(
            JsonElement root)
    {
        var layout =
            ParsePlacementLayout(
                root,
                "undergroundLayout");

        return layout is
            { } values
            ? new BiomeUndergroundLayoutDefinition(
                values.Weight,
                values.RegionMin,
                values.RegionMax,
                values.CannotBorder)
            : null;
    }

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

    private static BiomeSurfaceFluidDefinition?
        ParseSurfaceFluid(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "surfaceFluid",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        value =
            EnsureObject(
                value,
                "surfaceFluid");
        var type =
            RequiredString(
                value,
                "type");

        return type switch
        {
            "volcano_crater" =>
                new BiomeVolcanoCraterFluidDefinition(
                    RequiredString(value, "fluid"),
                    RequiredSingle(value, "minimumStrength"),
                    RequiredSingle(value, "levelOffset"),
                    RequiredSingle(value, "spillMinimumStrength"),
                    RequiredSingle(value, "spillMaximumStrength"),
                    RequiredSingle(value, "spillScale"),
                    RequiredSingle(value, "spillWidth"),
                    checked((byte)RequiredUInt32(value, "spillLevel"))),
            _ =>
                throw new FormatException(
                    $"Unknown surfaceFluid.type: {type}"),
        };
    }

    private static IReadOnlyList<BiomeSurfaceLayerDefinition>
        ParseLayers(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "surfaceLayers",
                out var array) ||
            array.ValueKind ==
                JsonValueKind.Null)
        {
            return Array.Empty<BiomeSurfaceLayerDefinition>();
        }

        array =
            EnsureArray(
                array,
                "surfaceLayers");
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
                "scale"),
            RequiredSingle(
                patch,
                "coverage"),
            OptionalSingle(
                patch,
                "roughness") ??
            0.25f,
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
    private sealed record PlacementLayoutValues(
        float Weight,
        uint RegionMin,
        uint RegionMax,
        IReadOnlyList<string> CannotBorder);

}
