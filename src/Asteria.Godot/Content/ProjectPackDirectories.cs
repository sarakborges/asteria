using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

/// <summary>Shared filesystem search policy for pack selection and all loaders.</summary>
internal static class ProjectPackDirectories
{
    public static PackDirectoryCatalog Catalog { get; } = new(
        Path.Combine(OS.GetUserDataDir(), "packs"),
        ProjectSettings.GlobalizePath("res://packs"));
}
