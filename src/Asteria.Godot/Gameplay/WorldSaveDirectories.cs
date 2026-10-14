using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

/// <summary>
/// One authoritative world-save directory. The editor uses the project
/// checkout; exported desktop builds use the executable's folder.
/// </summary>
internal static class WorldSaveDirectories
{
    public static string Root => GameDirectories.Worlds;

    public static string ForWorld(string name)
    {
        var validated = new WorldCreationOptions(name, 0);
        if (validated.Name != name)
            throw new ArgumentException("World name must be canonical.", nameof(name));
        return Path.Combine(Root, validated.Name);
    }
}
