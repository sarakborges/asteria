using Asteria.Core.Content;

namespace Asteria.Client.Content;

public static class ItemContentLoader
{
    public static PackContentRegistry<ItemDefinition> LoadProjectItems(PackSelection selection) =>
        PackContentRegistry<ItemDefinition>.FromJson(
            ProjectDataDocuments.Load(selection, "items"),
            ItemDefinition.Parse,
            item => item.Id);
}
