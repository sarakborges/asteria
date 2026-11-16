using Asteria.Core.Content;

namespace Asteria.Client.Content;

/// <summary>
/// Object content is independent from blocks and attached texture layers.
/// An empty objects/ directory is valid for third-party packs.
/// </summary>
public static class ObjectContentLoader
{
    public static PackContentRegistry<ObjectDefinition> LoadProjectObjects(
        PackSelection selection) =>
        PackContentRegistry<ObjectDefinition>.FromJson(
            ProjectDataDocuments.LoadOptional(selection, "objects"),
            ObjectDefinition.Parse, definition => definition.Id);
}
