using System.Text.Json;

namespace Asteria.Core.World;

public static class DimensionDefinitionJson
{
    public static DimensionDefinition Parse(
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
                "Dimension definition root must be an object.");
        }

        var spawn =
            RequiredObject(
                root,
                "spawn");
        var environment =
            RequiredObject(
                root,
                "environment");

        return new DimensionDefinition(
            new DimensionId(
                RequiredString(
                    root,
                    "id")),
            RequiredStringArray(
                root,
                "surfaceBiomes"),
            RequiredInt32(
                root,
                "seaLevel"),
            RequiredSingle(
                root,
                "gravityStrength"),
            new DimensionSpawnDefinition(
                RequiredInt32(
                    spawn,
                    "x"),
                RequiredInt32(
                    spawn,
                    "z")),
            new DimensionEnvironmentDefinition(
                DimensionColor.ParseHex(
                    RequiredString(
                        environment,
                        "backgroundColor"),
                    "environment.backgroundColor"),
                DimensionColor.ParseHex(
                    RequiredString(
                        environment,
                        "ambientColor"),
                    "environment.ambientColor"),
                RequiredSingle(
                    environment,
                    "ambientEnergy"),
                DimensionColor.ParseHex(
                    RequiredString(
                        environment,
                        "fogColor"),
                    "environment.fogColor"),
                RequiredSingle(
                    environment,
                    "fogDensity"),
                ParseWind(environment),
                ParseSkyLayers(environment)),
            ParseShell(root),
            ParseCaves(root),
            ParseGeneratedOcean(root),
            OptionalStringArray(
                root,
                "volumeBiomes"),
            ParseGeneratedSurfaceStructures(
                root),
            ParseGeneratedSurfaceFluids(
                root),
            OptionalString(root, "dayNightCycle"),
            ParseBiomeBlending(root));
    }

    private static DimensionSkyLayersDefinition? ParseSkyLayers(JsonElement environment)
    {
        if (!environment.TryGetProperty("skyLayers", out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;

        var sky = RequiredObject(environment, "skyLayers");
        var stars = RequiredObject(sky, "stars");
        var clouds = RequiredObject(sky, "clouds");
        return new DimensionSkyLayersDefinition(
            RequiredSingle(stars, "density"),
            DimensionColor.ParseHex(RequiredString(stars, "color"),
                "environment.skyLayers.stars.color"),
            RequiredSingle(clouds, "density"),
            DimensionColor.ParseHex(RequiredString(clouds, "color"),
                "environment.skyLayers.clouds.color"));
    }

    private static DimensionWindDefinition? ParseWind(JsonElement environment)
    {
        if (!environment.TryGetProperty("wind", out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;

        value = RequiredObject(environment, "wind");
        if (!value.TryGetProperty("direction", out var direction) ||
            direction.ValueKind != JsonValueKind.Array ||
            direction.GetArrayLength() != 2)
            throw new FormatException("environment.wind.direction must contain two numbers.");

        var components = direction.EnumerateArray().ToArray();
        if (components.Any(component => component.ValueKind != JsonValueKind.Number ||
                                        !component.TryGetSingle(out _)))
            throw new FormatException("environment.wind.direction must contain two numbers.");

        return new DimensionWindDefinition(
            components[0].GetSingle(),
            components[1].GetSingle(),
            RequiredSingle(value, "strength"));
    }

    private static IReadOnlyList<DimensionGeneratedSurfaceFluidDefinition>
        ParseGeneratedSurfaceFluids(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "generatedSurfaceFluids",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return Array.Empty<DimensionGeneratedSurfaceFluidDefinition>();
        }

        if (value.ValueKind !=
            JsonValueKind.Array)
        {
            throw new FormatException(
                "generatedSurfaceFluids must be an array.");
        }

        return value
            .EnumerateArray()
            .Select(entry =>
            {
                if (entry.ValueKind !=
                    JsonValueKind.Object)
                {
                    throw new FormatException(
                        "generatedSurfaceFluids entries must be objects.");
                }

                return new DimensionGeneratedSurfaceFluidDefinition(
                    RequiredString(
                        entry,
                        "biome"),
                    RequiredString(
                        entry,
                        "fluid"),
                    RequiredInt32(
                        entry,
                        "spacing"),
                    RequiredInt32(
                        entry,
                        "radius"),
                    OptionalInt32(
                        entry,
                        "jitter") ??
                    0,
                    OptionalSingle(
                        entry,
                        "chance") ??
                    1f,
                    OptionalInt32(
                        entry,
                        "depth") ??
                    1);
            })
            .ToArray();
    }

    private static IReadOnlyList<DimensionGeneratedSurfaceStructureDefinition>
        ParseGeneratedSurfaceStructures(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "generatedSurfaceStructures",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return Array.Empty<DimensionGeneratedSurfaceStructureDefinition>();
        }

        if (value.ValueKind !=
            JsonValueKind.Array)
        {
            throw new FormatException(
                "generatedSurfaceStructures must be an array.");
        }

        return value
            .EnumerateArray()
            .Select(entry =>
            {
                if (entry.ValueKind !=
                    JsonValueKind.Object)
                {
                    throw new FormatException(
                        "generatedSurfaceStructures entries must be objects.");
                }

                return new DimensionGeneratedSurfaceStructureDefinition(
                    RequiredString(
                        entry,
                        "biome"),
                    RequiredString(
                        entry,
                        "structure"),
                    RequiredInt32(
                        entry,
                        "spacing"),
                    RequiredSingle(
                        entry,
                        "chance"),
                    OptionalInt32(
                        entry,
                        "jitter") ??
                    0,
                    OptionalString(
                        entry,
                        "placement") switch
                    {
                        null or "biomeInterior" =>
                            DimensionGeneratedSurfaceStructurePlacement.BiomeInterior,
                        "biomeMargin" =>
                            DimensionGeneratedSurfaceStructurePlacement.BiomeMargin,
                        var authored =>
                            throw new FormatException(
                                $"Unsupported generatedSurfaceStructures placement: {authored}."),
                    },
                    SurfaceHabitatDefinitionJson.ParseWeights(entry));
            })
            .ToArray();
    }

    private static BiomeBlendingDefinition? ParseBiomeBlending(
        JsonElement root)
    {
        if (!root.TryGetProperty("biomeBlending", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        value = RequiredObject(root, "biomeBlending");
        var curve = OptionalString(value, "influenceCurve") switch
        {
            null or "smoothStep" => BiomeInfluenceCurve.SmoothStep,
            "linear" => BiomeInfluenceCurve.Linear,
            "smootherStep" => BiomeInfluenceCurve.SmootherStep,
            var authored => throw new FormatException(
                $"Unsupported biome influenceCurve: {authored}."),
        };

        BiomeContourHarmonicDefinition[]? harmonics = null;
        if (value.TryGetProperty("contourHarmonics", out var entries))
        {
            if (entries.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException(
                    "biomeBlending.contourHarmonics must be an array.");
            }

            harmonics = entries.EnumerateArray()
                .Select(entry =>
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        throw new FormatException(
                            "Contour harmonics must be objects.");
                    }

                    return new BiomeContourHarmonicDefinition(
                        RequiredInt32(entry, "lobes"),
                        RequiredSingle(entry, "amplitude"));
                })
                .ToArray();
        }

        return new BiomeBlendingDefinition(
            OptionalSingle(value, "scoreBand") ?? 0.50d,
            OptionalSingle(value, "jitterFraction") ?? 0.32d,
            OptionalSingle(value, "coarseWarpPeriod") ?? 4d,
            OptionalSingle(value, "fineWarpPeriod") ?? 1.35d,
            OptionalSingle(value, "coarseWarpStrength") ?? 0.42d,
            OptionalSingle(value, "fineWarpStrength") ?? 0.16d,
            curve,
            harmonics,
            OptionalSingle(value, "sizeExponent") ?? 0.12d,
            OptionalSingle(value, "seedBiasAmplitude") ?? 0.045d,
            OptionalSingle(value, "continuationBonus") ?? 0.055d);
    }

    private static DimensionGeneratedOceanDefinition? ParseGeneratedOcean(
        JsonElement root)
    {
        if (!root.TryGetProperty("generatedOcean", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        value = RequiredObject(root, "generatedOcean");
        return new DimensionGeneratedOceanDefinition(
            RequiredString(value, "biome"),
            RequiredString(value, "fluid"),
            ParseOceanShore(value));
    }

    private static DimensionShoreProfileDefinition? ParseOceanShore(
        JsonElement ocean)
    {
        if (!ocean.TryGetProperty("shore", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        value = RequiredObject(ocean, "shore");
        if (!value.TryGetProperty("samples", out var samples) ||
            samples.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("shore.samples must be an array.");
        }

        return new DimensionShoreProfileDefinition(
            samples.EnumerateArray().Select(sample =>
            {
                if (sample.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException(
                        "Each shore sample must be an object.");
                }

                return new DimensionShoreSampleDefinition(
                    RequiredSingle(sample, "dominance"),
                    RequiredSingle(sample, "minimumHeight"),
                    RequiredSingle(sample, "strength"));
            }));
    }

    private static DimensionCaveDefinition? ParseCaves(
        JsonElement root)
    {
        if (!root.TryGetProperty("caves", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        value = RequiredObject(root, "caves");
        if (!value.TryGetProperty("layers", out var layers) ||
            layers.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("caves.layers must be an array.");
        }

        return new DimensionCaveDefinition(
            layers.EnumerateArray().Select(layer =>
            {
                if (layer.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException(
                        "Each caves.layers entry must be an object.");
                }

                if (!layer.TryGetProperty("channels", out var channels) ||
                    channels.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException(
                        "caves.layers.channels must be an array.");
                }

                var combination = OptionalString(layer, "combination") switch
                {
                    null or "intersection" => CaveNoiseCombination.Intersection,
                    "union" => CaveNoiseCombination.Union,
                    _ => throw new FormatException(
                        "Cave combination must be intersection or union."),
                };
                var noiseChannels = channels.EnumerateArray().Select(channel =>
                {
                    if (channel.ValueKind != JsonValueKind.Object)
                    {
                        throw new FormatException(
                            "Each cave noise channel must be an object.");
                    }

                    return new DimensionCaveNoiseChannelDefinition(
                        RequiredUInt32(channel, "horizontalScale"),
                        RequiredUInt32(channel, "verticalScale"));
                });

                return new DimensionCaveLayerDefinition(
                    RequiredUInt32(layer, "minDepth"),
                    RequiredUInt32(layer, "maxDepth"),
                    noiseChannels,
                    RequiredSingle(layer, "noiseHalfWidth"),
                    RequiredSingle(layer, "densityScale"),
                    RequiredUInt32(layer, "boundaryFade"),
                    combination,
                    ParseCaveChambers(layer));
            }));
    }

    private static DimensionCaveChamberDefinition? ParseCaveChambers(
        JsonElement layer)
    {
        if (!layer.TryGetProperty("chambers", out _))
            return null;

        var chambers = RequiredObject(layer, "chambers");
        return new DimensionCaveChamberDefinition(
            RequiredUInt32(chambers, "horizontalScale"),
            RequiredUInt32(chambers, "verticalScale"),
            RequiredSingle(chambers, "activationStart"),
            RequiredSingle(chambers, "activationFull"),
            RequiredSingle(chambers, "maxNoiseHalfWidth"));
    }

    private static uint RequiredUInt32(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetUInt32(out var result))
        {
            throw new FormatException(
                $"{name} must be a non-negative 32-bit integer.");
        }

        return result;
    }

    private static DimensionShellDefinition? ParseShell(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "shell",
                out var shell) ||
            shell.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (shell.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "shell must be an object.");
        }

        return new DimensionShellDefinition(
            RequiredString(
                shell,
                "block"),
            OptionalInt32(
                shell,
                "floorY"),
            OptionalInt32(
                shell,
                "roofY"));
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
                        $"{name} may contain only strings."))
            .ToArray();
    }

    private static float RequiredSingle(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Number ||
            !value.TryGetSingle(
                out var result))
        {
            throw new FormatException(
                $"{name} must be a number.");
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
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Number ||
            !value.TryGetInt32(
                out var result))
        {
            throw new FormatException(
                $"{name} must be an integer.");
        }

        return result;
    }
}
