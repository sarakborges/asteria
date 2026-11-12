using Godot;

namespace Asteria.Client;

/// <summary>
/// Owns the portable installation layout. Development uses the project
/// checkout; desktop launchers provide the distribution root explicitly.
/// </summary>
internal static class GameDirectories
{
    public static string Root { get; } = ResolveRoot();

    public static string Worlds => Path.Combine(Root, "worlds");
    public static string Mods => Path.Combine(Root, "mods");
    public static string Packs => Path.Combine(Root, "packs");
    public static string Logs => Path.Combine(Root, "logs");

    private static string ResolveRoot()
    {
        if (OS.HasFeature("editor"))
            return Path.GetFullPath(ProjectSettings.GlobalizePath("res://"));

        var portableRoot = OS.GetEnvironment("ASTERIA_PORTABLE_ROOT");
        if (!string.IsNullOrWhiteSpace(portableRoot))
            return Path.GetFullPath(portableRoot);

        return Path.GetFullPath(
            Path.GetDirectoryName(OS.GetExecutablePath())
                ?? throw new InvalidOperationException("Cannot resolve executable directory."));
    }
}
