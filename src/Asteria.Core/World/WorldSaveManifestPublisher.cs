using System.Text.Json;

namespace Asteria.Core.World;

/// <summary>
/// Small, derived catalog metadata, never the authoritative save payload.
/// Written only after a fully published gameplay session generation.
/// </summary>
public static class WorldSaveManifestPublisher
{
    private const int MaximumManifestBytes = 32 * 1024;

    public static void Publish(
        string directory, GameplaySessionSnapshot snapshot, ulong generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (generation == 0 || snapshot.ActiveSphere is not { } active)
            throw new InvalidDataException("Catalog manifest requires a committed active Sphere.");

        var creation = new WorldCreationOptions(snapshot.Name, snapshot.Spatial.WorldSeed);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!StringComparer.Ordinal.Equals(Path.GetFileName(normalized), creation.Name))
            throw new InvalidDataException("World save directory must match the authored world name.");

        var sphere = snapshot.Spheres.SingleOrDefault(state => state.Dimension == active)
            ?? throw new InvalidDataException("Active Sphere is missing from saved state.");
        var position = sphere.Position;
        var document = new
        {
            formatVersion = WorldSaveCatalog.ManifestVersion,
            name = snapshot.Name,
            seed = WorldCreationSeed.Format(snapshot.Spatial.WorldSeed),
            dimensionId = active.Value,
            day = sphere.DayNight?.Day ?? 1UL,
            lastSavedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            playerPosition = position is null ? null : new[]
            {
                position.Value.X,
                position.Value.Y,
                position.Value.Z
            },
            generation
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(document);
        if (payload.Length is 0 or > MaximumManifestBytes)
            throw new InvalidDataException("Saved world manifest is too large.");

        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, WorldSaveCatalog.ManifestFileName);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(staging, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                file.Write(payload);
                file.Flush(flushToDisk: true);
            }

            File.Move(staging, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }
    }
}
