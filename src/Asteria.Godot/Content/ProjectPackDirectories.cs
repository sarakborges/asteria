using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

/// <summary>Shared filesystem search policy for portable and authored packs.</summary>
internal static class ProjectPackDirectories
{
    public static PackDirectoryCatalog Catalog { get; } = new(
        GameDirectories.Packs,
        ProjectSettings.GlobalizePath("res://packs"));
}
