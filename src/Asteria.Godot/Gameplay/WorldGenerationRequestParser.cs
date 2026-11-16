using System.Text.Json;
using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

/// <summary>Validates untrusted world-generation choices at the IPC boundary.
/// Gameplay parameters always become a Core-owned immutable creation snapshot.</summary>
internal static class WorldGenerationRequestParser
{
    public static WorldGenerationOptions Parse(
        JsonElement creationPayload,
        DimensionDefinition initialSphere)
    {
        ArgumentNullException.ThrowIfNull(initialSphere);
        if (!creationPayload.TryGetProperty("generation", out var raw))
            return new WorldGenerationOptions();
        if (raw.ValueKind != JsonValueKind.Object)
            throw Invalid();

        var modeString = RequiredString(raw, "mode");
        if (!Enum.TryParse<WorldGenerationMode>(
                modeString, ignoreCase: false, out var mode) ||
            !Enum.IsDefined(mode))
            throw Invalid();

        string? biome = null;
        if (raw.TryGetProperty("spawnBiome", out var biomeValue))
        {
            if (biomeValue.ValueKind == JsonValueKind.String)
                biome = biomeValue.GetString();
            else if (biomeValue.ValueKind != JsonValueKind.Null)
                throw Invalid();
        }

        if (biome is not null &&
            (!initialSphere.SurfaceBiomes.Contains(biome, StringComparer.Ordinal) ||
             string.Equals(biome, initialSphere.GeneratedOcean?.Biome,
                 StringComparison.Ordinal)))
            throw Invalid();

        var size = RequiredInt(raw, "biomeSizeTenths");
        try
        {
            return new WorldGenerationOptions(
                mode,
                biome,
                size,
                RequiredBool(raw, "spawnStructures"),
                RequiredBool(raw, "singleBiome"),
                RequiredBool(raw, "spawnCaves"),
                RequiredBool(raw, "spawnOceans"));
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
    }

    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw Invalid();

    private static int RequiredInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed : throw Invalid();

    private static bool RequiredBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : throw Invalid();

    private static ArgumentException Invalid() =>
        new("Invalid World Generation options.", "generation");
}
