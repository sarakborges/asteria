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
                "biomes"),
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
            ParseShell(
                root));
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
