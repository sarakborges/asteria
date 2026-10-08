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

        if (root.TryGetProperty("surfaceFluid", out _))
        {
            throw new FormatException(
                "Biome surfaceFluid is obsolete. Author surfaceTerrain.crater.fluidFill.");
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
            ParseUndergroundLayout(root));
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

        if (layout is not
            { } values)
        {
            return null;
        }

        var authored =
            EnsureObject(
                root.GetProperty(
                    "surfaceLayout"),
                "surfaceLayout");

        return new BiomeSurfaceLayoutDefinition(
            values.Weight,
            values.RegionMin,
            values.RegionMax,
            values.CannotBorder,
            OptionalSingle(
                authored,
                "spawnWeight") ??
            1f);
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

        var type = "noise";
        if (value.TryGetProperty("type", out var typeValue) &&
            typeValue.ValueKind != JsonValueKind.Null)
        {
            if (typeValue.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("surfaceTerrain.type must be a string.");
            }

            type = typeValue.GetString()!;
        }
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
                        RequiredSingle(value, "detailScale"),
                        OptionalSingle(value, "waveDirectionZ") ?? 0.35f,
                        OptionalSingle(value, "broadScaleMultiplier") ?? 0.55f,
                        OptionalSingle(value, "waveWeight") ?? 0.72f),
                "ridges" =>
                    new BiomeRidgesTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "amplitude"),
                        RequiredSingle(value, "scale"),
                        RequiredSingle(value, "sharpness"),
                        OptionalSingle(value, "detailAmplitude") ?? 0f,
                        OptionalSingle(value, "detailScale") ?? 0.02f,
                        ParseRidgeDetailMode(value),
                        OptionalSingle(value, "detailSharpness") ?? 1.35f),
                "valley" =>
                    new BiomeValleyTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "depth"),
                        RequiredSingle(value, "wallHeight"),
                        RequiredSingle(value, "topAmplitude"),
                        RequiredSingle(value, "topScale"),
                        RequiredSingle(value, "floorAmplitude"),
                        RequiredSingle(value, "floorScale"),
                        OptionalSingle(value, "rimFalloff") ?? 0.8f,
                        OptionalSingle(value, "floorFalloff") ?? 1.35f),
                "cone" =>
                    new BiomeConeTerrainShapeDefinition(
                        RequiredSingle(value, "baseHeight"),
                        RequiredSingle(value, "height"),
                        RequiredSingle(value, "irregularity"),
                        RequiredSingle(value, "irregularityScale"),
                        RequiredSingle(value, "detailIrregularity"),
                        RequiredSingle(value, "detailScale"),
                        OptionalSingle(value, "slopeNoiseGain") ?? 4f),
                _ =>
                    throw new FormatException(
                        $"Unknown surfaceTerrain.type: {type}"),
            };

        return new BiomeTerrainDefinition(
            shape,
            ParseTerrainModifiers(value),
            ParseInfluencePolicy(value),
            ParseCrater(value),
            ParseFillToSeaLevel(value));
    }

    private static BiomeRidgeDetailMode ParseRidgeDetailMode(JsonElement terrain)
    {
        if (!terrain.TryGetProperty("detailMode", out var value))
        {
            return BiomeRidgeDetailMode.None;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("surfaceTerrain.detailMode must be a string.");
        }

        return value.GetString() switch
        {
            "none" => BiomeRidgeDetailMode.None,
            "ridged" => BiomeRidgeDetailMode.Ridged,
            "modulated" => BiomeRidgeDetailMode.Modulated,
            _ => throw new FormatException("Unknown surfaceTerrain.detailMode."),
        };
    }

    private static SurfaceHeightInfluencePolicy ParseInfluencePolicy(
        JsonElement terrain)
    {
        if (!terrain.TryGetProperty("influenceMode", out var mode))
        {
            return SurfaceHeightInfluencePolicy.Blend;
        }

        if (mode.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("surfaceTerrain.influenceMode must be a string.");
        }

        return mode.GetString() switch
        {
            "blend" => SurfaceHeightInfluencePolicy.Blend,
            "lowerOnly" => SurfaceHeightInfluencePolicy.LowerOnly,
            "primary" => SurfaceHeightInfluencePolicy.Primary,
            _ => throw new FormatException("Unknown surfaceTerrain.influenceMode."),
        };
    }

    private static bool ParseFillToSeaLevel(JsonElement terrain)
    {
        if (!terrain.TryGetProperty("fillToSeaLevel", out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException("surfaceTerrain.fillToSeaLevel must be boolean."),
        };
    }

    private static BiomeCraterDefinition? ParseCrater(JsonElement terrain)
    {
        if (!terrain.TryGetProperty("crater", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        value = EnsureObject(value, "surfaceTerrain.crater");
        BiomeCraterFluidFillDefinition? fill = null;
        if (value.TryGetProperty("fluidFill", out var fluidValue) &&
            fluidValue.ValueKind != JsonValueKind.Null)
        {
            fluidValue = EnsureObject(fluidValue, "surfaceTerrain.crater.fluidFill");
            BiomeCraterSpillDefinition? spill = null;
            if (fluidValue.TryGetProperty("spill", out var spillValue) &&
                spillValue.ValueKind != JsonValueKind.Null)
            {
                spillValue = EnsureObject(spillValue, "surfaceTerrain.crater.fluidFill.spill");
                spill = new BiomeCraterSpillDefinition(
                    RequiredSingle(spillValue, "minimumStrength"),
                    RequiredSingle(spillValue, "maximumStrength"),
                    RequiredSingle(spillValue, "scale"),
                    RequiredSingle(spillValue, "width"),
                    checked((byte)RequiredUInt32(spillValue, "level")));
            }

            fill = new BiomeCraterFluidFillDefinition(
                RequiredString(fluidValue, "fluid"),
                RequiredSingle(fluidValue, "minimumStrength"),
                RequiredSingle(fluidValue, "topLevel"),
                spill);
        }

        return new BiomeCraterDefinition(
            RequiredSingle(value, "depth"),
            RequiredSingle(value, "radius"),
            RequiredSingle(value, "irregularity"),
            RequiredSingle(value, "noiseScale"),
            RequiredSingle(value, "transitionWidth"),
            fill);
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
                    "depressions" =>
                        new BiomeDepressionsTerrainModifierDefinition(
                            RequiredSingle(modifier, "depth"),
                            RequiredSingle(modifier, "broadScale"),
                            RequiredSingle(modifier, "detailScale"),
                            RequiredSingle(modifier, "broadWeight"),
                            RequiredSingle(modifier, "bias"),
                            RequiredSingle(modifier, "transitionWidth"),
                            RequiredSingle(modifier, "sharpness")),
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
        if (!terrain.TryGetProperty("additive", out var entries))
        {
            return new BiomeTerrain3dDefinition();
        }

        entries = EnsureArray(entries, "terrain3d.additive");
        var formations = entries.EnumerateArray()
            .Select((value, index) =>
            {
                value = EnsureObject(
                    value,
                    $"terrain3d.additive[{index}]");
                return new BiomeAdditiveDensityDefinition(
                    RequiredInt32(value, "minY"),
                    RequiredInt32(value, "maxY"),
                    RequiredUInt32(value, "horizontalScale"),
                    RequiredUInt32(value, "detailScale"),
                    RequiredSingle(value, "coverage"),
                    RequiredSingle(value, "roughness"),
                    RequiredSingle(value, "densityScale"),
                    OptionalSingle(value, "verticalFalloff") ?? 1f,
                    OptionalSingle(value, "horizontalFalloff") ?? 1f,
                    OptionalSingle(value, "densityBias") ?? 0f);
            });

        return new BiomeTerrain3dDefinition(formations);
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
                        "surfaceBlocks"),
                    ParseDecorationCluster(value)));
        }

        return decorations;
    }

    private static BiomeDecorationClusterDefinition? ParseDecorationCluster(
        JsonElement decoration)
    {
        if (!decoration.TryGetProperty("cluster", out var cluster) ||
            cluster.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        cluster = EnsureObject(cluster, "decorations.cluster");
        return new BiomeDecorationClusterDefinition(
            RequiredInt32(cluster, "scale"),
            RequiredSingle(cluster, "threshold"));
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
