using System.Text.Json;

namespace Asteria.Core.Content;

public sealed record CraftingIngredientDefinition(string Item, int Quantity);
public sealed record CraftingResultDefinition(string Item, int Quantity);

/// <summary>
/// Validated pack-owned crafting recipe. The environment identifies the
/// crafting surface; only "inventory" is executable until station owners exist.
/// World interaction recipes are a separate concept and are not loaded here.
/// </summary>
public sealed class CraftingRecipeDefinition
{
    private CraftingRecipeDefinition(
        string id,
        string environment,
        IReadOnlyList<CraftingIngredientDefinition> ingredients,
        CraftingResultDefinition result)
    {
        Id = id;
        Environment = environment;
        Ingredients = ingredients;
        Result = result;
    }

    public string Id { get; }
    public string Environment { get; }
    public IReadOnlyList<CraftingIngredientDefinition> Ingredients { get; }
    public CraftingResultDefinition Result { get; }

    public static CraftingRecipeDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("Crafting recipe must be an object.");

        var id = PackContentFields.Id(root);
        var environment = PackContentFields.RequiredString(root, "environment");
        if (environment != environment.Trim())
            throw new FormatException("Crafting environment must be trimmed.");

        if (!root.TryGetProperty("ingredients", out var list) ||
            list.ValueKind != JsonValueKind.Array ||
            list.GetArrayLength() == 0)
            throw new FormatException("Crafting ingredients must be a non-empty array.");

        var ingredients = new List<CraftingIngredientDefinition>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new FormatException("Crafting ingredient must be an object.");
            var item = PackContentFields.Namespaced(
                PackContentFields.RequiredString(entry, "item"), "ingredients.item");
            if (!unique.Add(item))
                throw new FormatException($"Duplicate crafting ingredient: {item}");
            ingredients.Add(new CraftingIngredientDefinition(
                item, PackContentFields.PositiveInt(entry, "quantity")));
        }

        var output = PackContentFields.RequiredObject(root, "result");
        var result = new CraftingResultDefinition(
            PackContentFields.Namespaced(
                PackContentFields.RequiredString(output, "item"), "result.item"),
            PackContentFields.PositiveInt(output, "quantity"));

        return new CraftingRecipeDefinition(
            id, environment, ingredients.AsReadOnly(), result);
    }
}
