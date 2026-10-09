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
            null,
            ParseDecorations(root),
            ParseTints(root),
            ParseTerrain3d(root),
            ParseVolumeLayout(root),
            SurfaceHabitatDefinitionJson.Parse(root),
            ParseCaveSpikes(root),
            ParsePalette(root));
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
                values.CannotBorder,
                ParseVolumePlacement(root))
            : null;
    }

    private static VolumeBiomePlacement ParseVolumePlacement(JsonElement root)
    {
        if (!root.TryGetProperty("volumeLayout", out var layout) ||
            !layout.TryGetProperty("placement", out var placement))
            return VolumeBiomePlacement.Additive;

        if (placement.ValueKind != JsonValueKind.String)
            throw new FormatException("volumeLayout.placement must be a string.");

        return placement.GetString() switch
        {
            "additive" => VolumeBiomePlacement.Additive,
            "carvedVoid" => VolumeBiomePlacement.CarvedVoid,
            _ => throw new FormatException("Unknown volumeLayout.placement."),
        };
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

    private static BiomePaletteDefinition ParsePalette(JsonElement root)
    {
        if (root.TryGetProperty("surfaceLayers", out _))
            throw new FormatException(
                "surfaceLayers is obsolete; author palette.default.");

        var palette = EnsureObject(
            root.TryGetProperty("palette", out var value)
                ? value : default,
            "palette");
        if (!palette.TryGetProperty("default", out var defaults))
            throw new FormatException("palette.default is required.");

        return new BiomePaletteDefinition(
            ParseLayers(defaults, "palette.default"),
            OptionalPaletteLayers(palette, "floor"),
            OptionalPaletteLayers(palette, "walls"),
            OptionalPaletteLayers(palette, "ceiling"),
            ParseSurfaceMosaic(palette));
    }

    private static BiomeSurfaceMosaicDefinition? ParseSurfaceMosaic(
        JsonElement palette)
    {
        if (!palette.TryGetProperty("surfaceMosaic", out var mosaic))
            return null;
        mosaic = EnsureObject(mosaic, "palette.surfaceMosaic");
        var entries = EnsureArray(
            mosaic.GetProperty("entries"), "palette.surfaceMosaic.entries");
        return new BiomeSurfaceMosaicDefinition(
            RequiredUInt32(mosaic, "scale"),
            OptionalUInt32(mosaic, "detailScale") ?? 12,
            OptionalSingle(mosaic, "detailStrength") ?? 0.2f,
            entries.EnumerateArray().Select(entry =>
            {
                entry = EnsureObject(entry, "palette.surfaceMosaic.entries[]");
                return new BiomeSurfaceMosaicEntryDefinition(
                    entry.TryGetProperty("block", out var block) ? block.GetString() : null,
                    entry.TryGetProperty("fluid", out var fluid) ? fluid.GetString() : null,
                    RequiredSingle(entry, "weight"));
            }));
    }

    private static IReadOnlyList<BiomeSurfaceLayerDefinition>? OptionalPaletteLayers(
        JsonElement palette, string face)
    {
        if (!palette.TryGetProperty(face, out var layers))
            return null;
        return ParseLayers(layers, $"palette.{face}");
    }

    private static IReadOnlyList<BiomeSurfaceLayerDefinition> ParseLayers(
        JsonElement array, string path)
    {
        array = EnsureArray(array, path);
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
                "blocks"),
            ParsePlacementConditions(patch),
            OptionalUInt32(patch, "detailScale"),
            OptionalUInt32(patch, "selectionScale"),
            OptionalUInt32(patch, "warpScale"),
            OptionalSingle(patch, "warpStrength") ?? 0d,
            OptionalSingle(patch, "stretchZ") ?? 1d,
            ParsePatchWeights(patch));
    }

    private static IReadOnlyDictionary<string, double>? ParsePatchWeights(
        JsonElement patch)
    {
        if (!patch.TryGetProperty("weights", out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Patch weights must be an object.");
        }

        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var entry in value.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Number ||
                !entry.Value.TryGetDouble(out var weight))
            {
                throw new FormatException(
                    $"Patch weight for {entry.Name} must be a number.");
            }

            if (!weights.TryAdd(entry.Name, weight))
            {
                throw new FormatException(
                    $"Duplicate patch weight for {entry.Name}.");
            }
        }

        return weights;
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

    private static IReadOnlyList<BiomeCaveSpikeDefinition> ParseCaveSpikes(
        JsonElement root)
    {
        if (!root.TryGetProperty("caveSpikes", out var array))
            return Array.Empty<BiomeCaveSpikeDefinition>();

        array = EnsureArray(array, "caveSpikes");
        return array.EnumerateArray().Select((entry, index) =>
        {
            entry = EnsureObject(entry, $"caveSpikes[{index}]");
            var directions = RequiredStringArray(entry, "directions")
                .Select(value => value switch
                {
                    "up" => CaveSpikeDirection.Up,
                    "down" => CaveSpikeDirection.Down,
                    _ => throw new FormatException(
                        $"Unknown cave spike direction: {value}"),
                });
            return new BiomeCaveSpikeDefinition(
                RequiredString(entry, "block"),
                RequiredSingle(entry, "chance"),
                RequiredInt32(entry, "minHeight"),
                RequiredInt32(entry, "maxHeight"),
                RequiredInt32(entry, "minClearance"),
                directions,
                OptionalStringArray(entry, "surfaceBiomes"),
                ParseCaveSpikeCluster(entry),
                OptionalInt32(entry, "minSpacing") ?? 0);
        }).ToArray();
    }

    private static CaveSpikeClusterDefinition? ParseCaveSpikeCluster(
        JsonElement entry)
    {
        if (!entry.TryGetProperty("cluster", out var cluster) ||
            cluster.ValueKind == JsonValueKind.Null)
            return null;

        cluster = EnsureObject(cluster, "caveSpikes.cluster");
        return new CaveSpikeClusterDefinition(
            RequiredInt32(cluster, "horizontalScale"),
            RequiredInt32(cluster, "verticalScale"),
            RequiredSingle(cluster, "threshold"),
            OptionalSingle(cluster, "transitionWidth") ?? 0f);
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
                    ParseDecorationCluster(value),
                    ParsePlacementConditions(value),
                    SurfaceHabitatDefinitionJson.ParseWeights(value),
                    ParseDecorationFluidPlacement(value),
                    ParseDecorationFluidRequirement(value)));
        }

        return decorations;
    }

    private static BiomeDecorationFluidRequirement? ParseDecorationFluidRequirement(
        JsonElement decoration)
    {
        if (!decoration.TryGetProperty("fluidRequirement", out var raw))
            return null;
        var value = EnsureObject(raw, "decorations.fluidRequirement");
        var relation = RequiredString(value, "relation") switch
        {
            "nearby" => DecorationFluidRelation.Nearby,
            "below" => DecorationFluidRelation.Below,
            var text => throw new FormatException(
                $"Unknown decorations.fluidRequirement.relation: {text}."),
        };
        return new BiomeDecorationFluidRequirement(
            RequiredString(value, "fluid"),
            relation,
            OptionalInt32(value, "maxDistance") ??
                (relation == DecorationFluidRelation.Nearby ? 2 : 0));
    }

    private static DecorationFluidPlacement ParseDecorationFluidPlacement(JsonElement decoration)
    {
        if (!decoration.TryGetProperty("fluidPlacement", out var value))
            return DecorationFluidPlacement.Dry;
        if (value.ValueKind != JsonValueKind.String)
            throw new FormatException("decorations.fluidPlacement must be a string.");

        return value.GetString() switch
        {
            "dry" => DecorationFluidPlacement.Dry,
            "submerged" => DecorationFluidPlacement.Submerged,
            "any" => DecorationFluidPlacement.Any,
            _ => throw new FormatException(
                "Unknown decorations.fluidPlacement (expected dry, submerged or any)."),
        };
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
            RequiredSingle(cluster, "threshold"),
            OptionalInt32(cluster, "octaves") ?? 3,
            OptionalSingle(cluster, "transitionWidth") ?? 0f);
    }

    private static SurfacePlacementConditions? ParsePlacementConditions(
        JsonElement authored)
    {
        if (!authored.TryGetProperty("conditions", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        value = EnsureObject(value, "conditions");
        return new SurfacePlacementConditions(
            OptionalInt32(value, "minY"),
            OptionalInt32(value, "maxY"),
            OptionalSingle(value, "minSlope"),
            OptionalSingle(value, "maxSlope"));
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
                $"{name} must be a signed 32-bit integer.");
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
