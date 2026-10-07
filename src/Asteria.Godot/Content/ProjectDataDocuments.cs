using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

internal static class ProjectDataDocuments
{
    public static IReadOnlyList<string> Load(
        PackSelection selection,
        string category)
    {
        var resourceDirectory =
            ProjectPackPaths.DataCategory(
                selection,
                category);
        var absoluteDirectory =
            ProjectSettings.GlobalizePath(
                resourceDirectory);

        if (!Directory.Exists(
                absoluteDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Data content directory does not exist: {absoluteDirectory}");
        }

        var files =
            Directory
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
                $"No {category} definitions were found in {resourceDirectory}.");
        }

        return files
            .Select(
                File.ReadAllText)
            .ToArray();
    } 
    public static IReadOnlyList<string> LoadOptional(
        PackSelection selection,
        string category)
    {
        var resourceDirectory =
            ProjectPackPaths.DataCategory(
                selection,
                category);
        var absoluteDirectory =
            ProjectSettings.GlobalizePath(
                resourceDirectory);

        if (!Directory.Exists(
                absoluteDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(
                absoluteDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly)
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .Select(
                File.ReadAllText)
            .ToArray();
    }

}
