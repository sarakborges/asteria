namespace Asteria.Core.World;

/// <summary>
/// New worlds must never silently reuse a saved world's directory. This
/// allocator chooses a canonical free name without modifying the filesystem.
/// Only new-world creation uses it; restore always keeps the saved identity.
/// </summary>
public static class WorldSaveNameAllocator
{
    public static string Allocate(string worldsDirectory, string requestedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);
        var validated = new WorldCreationOptions(requestedName, 0);
        if (validated.Name != requestedName)
            throw new ArgumentException("World name must already be normalized.",
                nameof(requestedName));

        if (!Exists(requestedName))
            return requestedName;

        for (var ordinal = 1; ordinal <= 256; ordinal++)
        {
            var prefix = ordinal == 1 ? "Copy of " : $"Copy ({ordinal}) of ";
            var availableLength = WorldCreationOptions.MaximumNameLength - prefix.Length;
            if (availableLength <= 0)
                break;
            var candidate = prefix + requestedName[..Math.Min(requestedName.Length, availableLength)];
            candidate = candidate.TrimEnd(' ', '.');
            var canonical = new WorldCreationOptions(candidate, 0).Name;
            if (!Exists(canonical))
                return canonical;
        }

        throw new IOException("Could not allocate a unique world save name.");

        bool Exists(string name)
        {
            var path = Path.Combine(worldsDirectory, name);
            return Directory.Exists(path) || File.Exists(path);
        }
    }
}
