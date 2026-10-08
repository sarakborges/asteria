using System.Globalization;
using System.Text.Json;

namespace Asteria.Core.World;

/// <summary>
/// Read-only discovery of complete on-disk world candidates. Discovery does
/// not imply that the current runtime can restore a save.
/// </summary>
public static class WorldSaveCatalog
{
    public const string ManifestFileName = "world.json";
    public const int ManifestVersion = 1;
    private const int MaxWorlds = 256;
    private const long MaxManifestBytes = 32 * 1024;

    public static IReadOnlyList<WorldSaveSummary> Scan(string worldsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);
        if (!Directory.Exists(worldsDirectory))
            return Array.Empty<WorldSaveSummary>();

        var directories = Directory.EnumerateDirectories(worldsDirectory)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Take(MaxWorlds + 1)
            .ToArray();

        if (directories.Length > MaxWorlds)
            throw new IOException("World catalog exceeds the supported entry limit.");

        var entries = new List<WorldSaveSummary>(directories.Length);
        foreach (var directory in directories)
        {
            var attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                (attributes & FileAttributes.Directory) == 0)
                continue;

            var path = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(path))
                continue;

            // Corrupted or incomplete saves remain visible as incompatible.
            // A missing serializer/loader is never treated as compatibility.
            var id = Path.GetFileName(directory);
            var entry = new WorldSaveSummary(
                id, "", "", "", "", "", Compatible: false);
            try
            {
                var file = new FileInfo(path);
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    file.Length > MaxManifestBytes || file.Length == 0)
                {
                    entries.Add(entry);
                    continue;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("formatVersion", out var version) ||
                    !version.TryGetInt32(out var formatVersion) ||
                    formatVersion != ManifestVersion ||
                    !root.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String ||
                    !string.Equals(name.GetString(), id, StringComparison.Ordinal) ||
                    !root.TryGetProperty("seed", out var seed) ||
                    seed.ValueKind != JsonValueKind.String ||
                    !WorldCreationSeed.TryParse(seed.GetString() ?? "", out _) ||
                    !root.TryGetProperty("dimensionId", out var dimension) ||
                    dimension.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(dimension.GetString()) ||
                    !root.TryGetProperty("day", out var day) ||
                    !day.TryGetUInt64(out var worldDay) ||
                    !root.TryGetProperty("lastSavedUnixMs", out var modified) ||
                    !modified.TryGetInt64(out var timestamp) || timestamp <= 0)
                {
                    entries.Add(entry);
                    continue;
                }

                var lastSaved = DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                    .UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
                var position = ReadCoordinates(root);
                entry = new WorldSaveSummary(
                    id, lastSaved, seed.GetString()!,
                    worldDay > 0 ? (worldDay - 1).ToString(CultureInfo.InvariantCulture) : "0",
                    dimension.GetString()!, position,
                    Compatible: false);
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or JsonException
                    or ArgumentException or InvalidOperationException)
            {
                // Keep invalid manifests discoverable, never pretend they can load.
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static string ReadCoordinates(JsonElement root)
    {
        if (!root.TryGetProperty("playerPosition", out var values) ||
            values.ValueKind != JsonValueKind.Array ||
            values.GetArrayLength() != 3)
            return "—";

        var coords = new float[3];
        for (var i = 0; i < coords.Length; i++)
        {
            if (!values[i].TryGetSingle(out coords[i]) ||
                !float.IsFinite(coords[i]))
                return "—";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"X: {MathF.Floor(coords[0])} · Z: {MathF.Floor(coords[2])} · Y: {MathF.Floor(coords[1])}");
    }
}

public sealed record WorldSaveSummary(
    string Id,
    string LastSaved,
    string Seed,
    string DaysPassed,
    string Sphere,
    string Coordinates,
    bool Compatible);
