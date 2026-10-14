using System.Text.Json;
using Asteria.Core.Content;

namespace Asteria.Client.Content;

internal static class UiThemeLoader
{
    public static JsonElement LoadProjectTheme(PackSelection selection)
    {
        using var document = JsonDocument.Parse(
            ProjectPackFiles.ReadUiText(selection, "theme.json"));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("UI theme root must be an object.");
        return document.RootElement.Clone();
    }
}
