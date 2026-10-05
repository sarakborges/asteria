using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Content;

public static class BlockContentLoader
{
    private const string BlockDirectory = "res://content/blocks";

    public static BlockRegistry LoadProjectBlocks()
    {
        var absoluteDirectory = ProjectSettings.GlobalizePath(BlockDirectory);
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
            throw new InvalidOperationException($"No block definitions were found in {BlockDirectory}.");
        }

        return BlockRegistry.FromJson(files.Select(File.ReadAllText));
    }
}
