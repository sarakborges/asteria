using Asteria.Core.Content;

namespace Asteria.Client.Content;

public static class InventoryCategoryContentLoader
{
    public static InventoryCategoryRegistry LoadProjectCategories(PackSelection selection) =>
        InventoryCategoryRegistry.FromJson(
            ProjectDataDocuments.Load(selection, "inventory_categories"));
}
