using Godot;

namespace Asteria.Client;

/// <summary>
/// Portable runtime data resides in the source project while developing,
/// and beside the executable in desktop exports.
/// </summary>
internal static class GameDirectories
{
    public static string Root { get; } = Path.GetFullPath(
        OS.HasFeature("editor")
            ? ProjectSettings.GlobalizePath("res://")
            : Path.GetDirectoryName(OS.GetExecutablePath())
                ?? throw new InvalidOperationException("Cannot resolve executable directory."));

    public static string Worlds => Path.Combine(Root, "worlds");
    public static string Mods => Path.Combine(Root, "mods");
}
