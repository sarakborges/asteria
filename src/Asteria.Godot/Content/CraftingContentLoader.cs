using Asteria.Core.Content;

namespace Asteria.Client.Content;

/// <summary>Loads validated, pack-owned inventory crafting recipes.</summary>
public static class CraftingContentLoader
{
    public static PackContentRegistry<CraftingRecipeDefinition> LoadProjectRecipes(
        PackSelection selection) =>
        PackContentRegistry<CraftingRecipeDefinition>.FromJson(
            ProjectDataDocuments.LoadOptional(selection, "crafting_recipes"),
            CraftingRecipeDefinition.Parse,
            recipe => recipe.Id);
}
