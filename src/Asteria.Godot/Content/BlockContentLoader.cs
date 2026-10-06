using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Content;

public static class BlockContentLoader
{
    public static BlockRegistry LoadProjectBlocks(
        PackSelection selection)
    {
        var blockDirectory =
            ProjectPackPaths.DataCategory(
                selection,
                "blocks");
        var absoluteDirectory =
            ProjectSettings.GlobalizePath(
                blockDirectory);
        if (!Directory.Exists(absoluteDirectory))
        {
            throw new DirectoryNotFoundException($"Block content directory does not exist: {absoluteDirectory}");
        }

        var files = Directory
            .EnumerateFiles(absoluteDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException($"No block definitions were found in {blockDirectory}.");
        }

        return BlockRegistry.FromJson(files.Select(File.ReadAllText));
    }
}
