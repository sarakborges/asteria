using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Content;

public static class FluidContentLoader
{
    public static FluidRegistry LoadProjectFluids(
        PackSelection selection)
    {
        var fluidDirectory =
            ProjectPackPaths.DataCategory(
                selection,
                "fluids");
        var absoluteDirectory =
            ProjectSettings.GlobalizePath(
                fluidDirectory);

        if (!Directory.Exists(absoluteDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Fluid content directory does not exist: {absoluteDirectory}");
        }

        var files = Directory
            .EnumerateFiles(
                absoluteDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly)
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                $"No fluid definitions were found in {fluidDirectory}.");
        }

        return FluidRegistry.FromJson(
            files.Select(File.ReadAllText));
    }
}
