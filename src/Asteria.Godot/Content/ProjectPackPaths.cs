using Asteria.Core.Content;

namespace Asteria.Client.Content;

internal static class ProjectPackPaths
{
    public static string DataCategory(
        PackSelection selection,
        string category) =>
        $"res://data/{selection.DataPack}/{ValidateRelativePath(category)}";

    public static string Resource(
        PackSelection selection,
        string relativePath) =>
        $"res://resources/{selection.ResourcePack}/{ValidateRelativePath(relativePath)}";

    private static string ValidateRelativePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Pack-relative path cannot be empty.",
                nameof(path));
        }

        if (path.StartsWith(
                '/',
                StringComparison.Ordinal) ||
            path.Contains(
                '\',
                StringComparison.Ordinal) ||
            path.Contains(
                ':',
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Pack-relative path is invalid: {path}",
                nameof(path));
        }

        var segments =
            path.Split(
                '/',
                StringSplitOptions.None);

        if (segments.Any(
                segment =>
                    string.IsNullOrEmpty(segment) ||
                    segment is "." or ".."))
        {
            throw new ArgumentException(
                $"Pack-relative path is invalid: {path}",
                nameof(path));
        }

        return path;
    }
}
