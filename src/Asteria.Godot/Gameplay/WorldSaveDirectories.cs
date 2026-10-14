using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Gameplay;

/// <summary>
/// One authoritative world-save directory. The editor uses the project
/// checkout; exported desktop builds use the executable's folder.
/// </summary>
internal static class WorldSaveDirectories
{
    public static string Root { get; } = Path.GetFullPath(Path.Combine(
        OS.HasFeature("editor")
            ? ProjectSettings.GlobalizePath("res://")
            : Path.GetDirectoryName(OS.GetExecutablePath())
                ?? throw new InvalidOperationException("Cannot resolve executable directory."),
        "worlds"));

    public static string ForWorld(string name)
    {
        var validated = new WorldCreationOptions(name, 0);
        if (validated.Name != name)
            throw new ArgumentException("World name must be canonical.", nameof(name));
        return Path.Combine(Root, validated.Name);
    }
}
