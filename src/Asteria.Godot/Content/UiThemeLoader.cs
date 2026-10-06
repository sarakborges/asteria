using System.Text.Json;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

internal static class UiThemeLoader
{
    public static JsonElement LoadProjectTheme(
        PackSelection selection)
    {
        var resourcePath =
            ProjectPackPaths.Ui(
                selection,
                "theme.json");
        var absolutePath =
            ProjectSettings.GlobalizePath(
                resourcePath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException(
                $"UI theme does not exist: {resourcePath}",
                absolutePath);
        }

        using var document =
            JsonDocument.Parse(
                File.ReadAllText(
                    absolutePath));

        if (document.RootElement.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"UI theme root must be an object: {resourcePath}");
        }

        return document.RootElement.Clone();
    }
}
