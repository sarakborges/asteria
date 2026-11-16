using Asteria.Core.Content;

namespace Asteria.Client.Content;

internal static class ProjectPackPaths
{
    private const string PacksRoot = "res://packs";

    public static string DataCategory(
        PackSelection selection,
        string category) =>
        $"{PackRoot(selection)}/data/{ValidateRelativePath(category)}";

    public static string Resource(
        PackSelection selection,
        string relativePath) =>
        $"{PackRoot(selection)}/resources/{ValidateRelativePath(relativePath)}";

    public static string Ui(
        PackSelection selection,
        string relativePath) =>
        $"{PackRoot(selection)}/ui/{ValidateRelativePath(relativePath)}";

    private static string PackRoot(
        PackSelection selection) =>
        $"{PacksRoot}/{selection.Name}";

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
                "/",
                StringComparison.Ordinal) ||
            path.Contains('\\') ||
            path.Contains(':'))
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
