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
                    "fogDensity")),
            ParseShell(root),
            ParseCaves(root),
            ParseGeneratedOcean(root),
            OptionalStringArray(
                root,
                "volumeBiomes"),
            OptionalStringArray(
                root,
                "undergroundBiomes"),
            ParseGeneratedSurfaceStructures(
                root),
            ParseGeneratedSurfaceFluids(
                root));
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
                    });
            })
            .ToArray();
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

    private static DimensionOceanShoreDefinition? ParseOceanShore(
        JsonElement ocean)
    {
        if (!ocean.TryGetProperty("shore", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        value = RequiredObject(ocean, "shore");
        return new DimensionOceanShoreDefinition(
            RequiredInt32(value, "shelfDepth"),
            RequiredInt32(value, "beachHeight"),
            RequiredSingle(value, "beachStartDominance"),
            RequiredSingle(value, "shelfStartDominance"),
            RequiredSingle(value, "deepWaterStartDominance"));
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
        return new DimensionCaveDefinition(
            RequiredUInt32(value, "minDepth"),
            RequiredUInt32(value, "maxDepth"),
            RequiredUInt32(value, "horizontalScale"),
            RequiredUInt32(value, "verticalScale"),
            RequiredSingle(value, "noiseHalfWidth"),
            RequiredSingle(value, "densityScale"),
            RequiredUInt32(value, "boundaryFade"));
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
