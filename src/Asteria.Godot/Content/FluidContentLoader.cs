using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Content;

public static class FluidContentLoader
{
    private const string FluidDirectory =
        "res://content/fluids";

    public static FluidRegistry LoadProjectFluids()
    {
        var absoluteDirectory =
            ProjectSettings.GlobalizePath(
                FluidDirectory);

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
                $"No fluid definitions were found in {FluidDirectory}.");
        }

        return FluidRegistry.FromJson(
            files.Select(File.ReadAllText));
    }
}
