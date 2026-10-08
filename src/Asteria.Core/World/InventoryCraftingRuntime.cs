using Asteria.Core.Content;

namespace Asteria.Core.World;

public enum InventoryCraftingResult : byte
{
    Crafted,
    UnknownRecipe,
    MissingIngredients,
    InventoryFull,
}

/// <summary>
/// Immutable inventory-recipe bindings. The player's PlayerInventory remains
/// the sole mutable owner of ingredients, output and transaction revisions.
/// </summary>
public sealed class InventoryCraftingRuntime
{
    private sealed record RecipeBinding(
        CraftingRecipeDefinition Recipe, InventoryEntry Result);

    private readonly Dictionary<string, RecipeBinding> _recipes =
        new(StringComparer.Ordinal);
    private readonly CraftingRecipeDefinition[] _inventoryRecipes;

    public InventoryCraftingRuntime(
        PackContentRegistry<CraftingRecipeDefinition> recipes,
        InventoryContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(catalog);

        foreach (var recipe in recipes.Definitions)
        {
            if (recipe.Environment != "inventory") continue;

            foreach (var ingredient in recipe.Ingredients)
            {
                if (!catalog.TryResolve(
                    InventoryEntryKind.Item, ingredient.Item,
                    null, null, out _))
                    throw new InvalidOperationException(
                        $"Recipe {recipe.Id} references unknown item {ingredient.Item}.");
            }

            // Only plain authored identities are legal output; metadata
            // variants must never be fabricated by a crafting recipe.
            var matches = catalog.Choices
                .Select(choice => choice.Entry)
                .Where(entry => entry.Id == recipe.Result.Item &&
                    entry.Metadata.Count == 0)
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Recipe {recipe.Id} requires one unambiguous result {recipe.Result.Item}.");

            _recipes.Add(recipe.Id, new RecipeBinding(recipe, matches[0]));
        }

        _inventoryRecipes = _recipes.Values
            .Select(binding => binding.Recipe)
            .OrderBy(recipe => recipe.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<CraftingRecipeDefinition> Recipes => _inventoryRecipes;

    public bool CanCraft(PlayerInventory inventory, string recipeId)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return _recipes.TryGetValue(recipeId, out var binding) &&
            inventory.CanCraft(binding.Recipe, binding.Result);
    }

    public bool TryCraft(PlayerInventory inventory, string recipeId) =>
        Craft(inventory, recipeId) == InventoryCraftingResult.Crafted;

    public InventoryCraftingResult Craft(PlayerInventory inventory, string recipeId)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!_recipes.TryGetValue(recipeId, out var binding))
            return InventoryCraftingResult.UnknownRecipe;

        foreach (var ingredient in binding.Recipe.Ingredients)
        {
            if (inventory.ItemQuantity(ingredient.Item) < ingredient.Quantity)
                return InventoryCraftingResult.MissingIngredients;
        }

        return inventory.TryCraft(binding.Recipe, binding.Result)
            ? InventoryCraftingResult.Crafted
            : InventoryCraftingResult.InventoryFull;
    }
}
