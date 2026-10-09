namespace Asteria.Core.World;

/// <summary>
/// Deletes only a canonical, catalog-shaped world directory beneath the
/// configured saves root. Browser input is an ID, never a filesystem path.
/// The caller must reject deletion while a world is active or being loaded.
/// </summary>
public static class WorldSaveDeletion
{
    private const int MaximumEntries = 500_000;
    private const int MaximumDepth = 128;

    public static void Delete(string worldsDirectory, string worldId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);
        var validated = new WorldCreationOptions(worldId, 0);
        if (!string.Equals(validated.Name, worldId, StringComparison.Ordinal))
            throw new ArgumentException("World ID is not canonical.", nameof(worldId));

        var root = new DirectoryInfo(Path.GetFullPath(worldsDirectory));
        if (!root.Exists || IsReparsePoint(root.Attributes))
            throw new IOException("World saves root is missing or is a filesystem link.");

        var world = new DirectoryInfo(Path.Combine(root.FullName, validated.Name));
        if (!world.Exists || IsReparsePoint(world.Attributes))
            throw new IOException("World directory is missing or is a filesystem link.");

        // Even an incompatible/corrupted save may be removed, but never
        // delete arbitrary unrelated folders next to the save catalog.
        var manifest = new FileInfo(Path.Combine(world.FullName, WorldSaveCatalog.ManifestFileName));
        if (!manifest.Exists || IsReparsePoint(manifest.Attributes))
            throw new IOException("Selected directory has no regular world manifest.");

        var remaining = MaximumEntries;
        ValidateTree(world, depth: 0, ref remaining);

        // Preflight is complete; failures are surfaced rather than claiming
        // success. The native caller refreshes the catalog after any outcome.
        world.Delete(recursive: true);
    }

    private static void ValidateTree(DirectoryInfo directory, int depth, ref int remaining)
    {
        if (depth > MaximumDepth || IsReparsePoint(directory.Attributes))
            throw new IOException("World directory contains an unsafe filesystem link or depth.");

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (--remaining < 0)
                throw new IOException("World directory exceeds the supported deletion limit.");
            entry.Refresh();
            if (IsReparsePoint(entry.Attributes))
                throw new IOException("World directory contains a filesystem link.");
            if ((entry.Attributes & FileAttributes.Directory) != 0)
                ValidateTree((DirectoryInfo)entry, depth + 1, ref remaining);
        }
    }

    private static bool IsReparsePoint(FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) != 0;
}
