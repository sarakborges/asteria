using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class InventoryCraftingRuntimeTests
{
    private static IReadOnlyList<string> Documents(string category) =>
        Directory.EnumerateFiles(
                Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", category),
                "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();

    private static InventoryCraftingRuntime CreateRuntime()
    {
        var recipes = PackContentRegistry<CraftingRecipeDefinition>.FromJson(
            Documents("crafting_recipes"), CraftingRecipeDefinition.Parse,
            recipe => recipe.Id);
        var items = PackContentRegistry<ItemDefinition>.FromJson(
            Documents("items"), ItemDefinition.Parse, item => item.Id);
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            Documents("tools"), ToolDefinition.Parse, tool => tool.Id);
        return new InventoryCraftingRuntime(
            recipes, new InventoryContentCatalog(new BlockRegistry(Array.Empty<BlockDefinition>()), items, tools));
    }

    private static void AddMaterials(PlayerInventory inventory)
    {
        Assert.True(inventory.TryInsert(
            new InventoryStack(InventoryEntry.FromItem("asteria:pebble"), 2)));
        Assert.True(inventory.TryInsert(
            new InventoryStack(InventoryEntry.FromItem("asteria:stick"), 3)));
        Assert.True(inventory.TryInsert(
            new InventoryStack(InventoryEntry.FromItem("asteria:plant_fiber"), 2)));
    }

    [Fact]
    public void ImportsMineCloneRusticHatchetRecipe()
    {
        var recipe = Assert.Single(CreateRuntime().Recipes);
        Assert.Equal("asteria:rustic_hatchet", recipe.Id);
        Assert.Equal("inventory", recipe.Environment);
        Assert.Equal("asteria:hatchet_rustic", recipe.Result.Item);
        Assert.Equal(1, recipe.Result.Quantity);
        Assert.Equal(new[] { 2, 3, 2 },
            recipe.Ingredients.Select(ingredient => ingredient.Quantity));
    }

    [Fact]
    public void CraftConsumesOnceAndInsertsRealTool()
    {
        var crafting = CreateRuntime();
        var inventory = new PlayerInventory();
        AddMaterials(inventory);
        var revision = inventory.Revision;
        Assert.True(crafting.CanCraft(inventory, "asteria:rustic_hatchet"));
        Assert.True(crafting.TryCraft(inventory, "asteria:rustic_hatchet"));
        Assert.Equal(revision + 1, inventory.Revision);
        Assert.Equal(0, inventory.ItemQuantity("asteria:pebble"));
        Assert.Equal(0, inventory.ItemQuantity("asteria:stick"));
        Assert.Equal(0, inventory.ItemQuantity("asteria:plant_fiber"));
        Assert.Contains(inventory.Capture().Hotbar,
            stack => stack is { Id: "asteria:hatchet_rustic", Kind: InventoryEntryKind.Tool });
        Assert.False(crafting.TryCraft(inventory, "asteria:rustic_hatchet"));
    }

    [Fact]
    public void MissingIngredientsNeverAlterInventoryOrCursor()
    {
        var crafting = CreateRuntime();
        var inventory = new PlayerInventory();
        AddMaterials(inventory);
        Assert.True(inventory.TryConsumeSelected());
        Assert.True(inventory.TryCreativePick(
            InventoryEntry.FromItem("asteria:essence_aqua")));
        var original = inventory.Capture();
        var revision = inventory.Revision;

        Assert.False(crafting.CanCraft(inventory, "asteria:rustic_hatchet"));
        Assert.False(crafting.TryCraft(inventory, "asteria:rustic_hatchet"));
        Assert.Equal(revision, inventory.Revision);
        var unchanged = inventory.Capture();
        Assert.Equal(original.Backpack, unchanged.Backpack);
        Assert.Equal(original.Hotbar, unchanged.Hotbar);
        Assert.Equal(original.Cursor, unchanged.Cursor);
    }

    [Fact]
    public void FailedOutputCapacityRollsBackAllInputConsumption()
    {
        var crafting = CreateRuntime();
        var inventory = new PlayerInventory();
        AddMaterials(inventory);
        // One output tool cannot fit when all remaining slots are full.
        for (var i = 0; i < 33; i++)
            Assert.True(inventory.TryInsert(new InventoryStack(
                InventoryEntry.FromItem($"asteria:filler_{i}", maxStackSize: 1))));
        // The requested input consumes only part of a pebble stack,
        // so no slot is freed for the larger tool output.
        var large = CraftingRecipeDefinition.Parse("""
            {
              "id": "asteria:large_batch",
              "environment": "inventory",
              "ingredients": [{"item": "asteria:pebble", "quantity": 1}],
              "result": {"item": "asteria:hatchet_rustic", "quantity": 3}
            }
            """);
        var revision = inventory.Revision;
        var before = inventory.Capture();
        Assert.False(inventory.TryCraft(large,
            InventoryEntry.FromTool("asteria:hatchet_rustic")));
        Assert.Equal(revision, inventory.Revision);
        var unchanged = inventory.Capture();
        Assert.Equal(before.Backpack, unchanged.Backpack);
        Assert.Equal(before.Hotbar, unchanged.Hotbar);
        Assert.Equal(before.Cursor, unchanged.Cursor);
    }

    [Fact]
    public void RejectsInvalidRecipesAndUnknownReferences()
    {
        const string json = """
            {
              "id": "asteria:test",
              "environment": "inventory",
              "ingredients": [{"item": "asteria:pebble", "quantity": 1}],
              "result": {"item": "asteria:hatchet_rustic", "quantity": 1}
            }
            """;
        Assert.Throws<FormatException>(() =>
            CraftingRecipeDefinition.Parse(json.Replace(
                "\"quantity\": 1", "\"quantity\": 0")));
        Assert.Throws<FormatException>(() =>
            CraftingRecipeDefinition.Parse(json.Replace(
                "asteria:hatchet_rustic", "bad_id")));
        Assert.Throws<FormatException>(() =>
            CraftingRecipeDefinition.Parse(json.Replace(
                "{\"item\": \"asteria:pebble\", \"quantity\": 1}",
                "{\"item\": \"asteria:pebble\", \"quantity\": 1}, {\"item\": \"asteria:pebble\", \"quantity\": 1}")));
        var bad = PackContentRegistry<CraftingRecipeDefinition>.FromJson(
            [json.Replace("asteria:pebble", "asteria:nonexistent")],
            CraftingRecipeDefinition.Parse, recipe => recipe.Id);
        var items = PackContentRegistry<ItemDefinition>.FromJson(
            Documents("items"), ItemDefinition.Parse, item => item.Id);
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            Documents("tools"), ToolDefinition.Parse, tool => tool.Id);
        Assert.Throws<InvalidOperationException>(() => new InventoryCraftingRuntime(
            bad, new InventoryContentCatalog(new BlockRegistry(Array.Empty<BlockDefinition>()), items, tools)));
    }

    [Fact]
    public void CraftOutcomesDistinguishMissingIngredientsAndUnknownRecipes()
    {
        var crafting = CreateRuntime();
        var inventory = new PlayerInventory();
        Assert.Equal(InventoryCraftingResult.UnknownRecipe,
            crafting.Craft(inventory, "asteria:unrecognized"));
        Assert.Equal(InventoryCraftingResult.MissingIngredients,
            crafting.Craft(inventory, "asteria:rustic_hatchet"));
        Assert.Equal(0UL, inventory.Revision);

        AddMaterials(inventory);
        Assert.Equal(InventoryCraftingResult.Crafted,
            crafting.Craft(inventory, "asteria:rustic_hatchet"));
    }

    [Fact]
    public void UnknownRecipesDoNotMutateInventory()
    {
        var crafting = CreateRuntime();
        var inventory = new PlayerInventory();
        AddMaterials(inventory);
        var revision = inventory.Revision;
        Assert.False(crafting.TryCraft(inventory, "asteria:unknown"));
        Assert.Equal(revision, inventory.Revision);
    }
}
