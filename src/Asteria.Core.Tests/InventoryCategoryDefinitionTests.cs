using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class InventoryCategoryDefinitionTests
{
    [Fact]
    public void AuthoredCategoryOrderBreaksTiesByStableId()
    {
        var categories = InventoryCategoryRegistry.FromJson([
            """{"id":"tools","order":70,"icon":"textures/creative_categories/tools.png"}""",
            """{"id":"crafting_materials","order":60,"icon":"textures/creative_categories/crafting_materials.png"}""",
            """{"id":"light_sources","order":60,"icon":"textures/creative_categories/light_sources.png"}"""
        ]);
        Assert.Equal(["crafting_materials", "light_sources", "tools"],
            categories.Definitions.Select(x => x.Id).ToArray());
        Assert.Equal((ushort)70, categories.Get("tools").Order);
        Assert.False(categories.TryGet("unknown", out _));
    }

    [Theory]
    [InlineData("""{"id":"../tools","order":1,"icon":"textures/tools.png"}""")]
    [InlineData("""{"id":"tools","order":-1,"icon":"textures/tools.png"}""")]
    [InlineData("""{"id":"tools","order":1,"icon":"../tools.png"}""")]
    [InlineData("""{"id":"tools","order":1,"icon":"textures/tools.svg"}""")]
    public void InvalidAuthoredCategoriesAreRejected(string json) =>
        Assert.ThrowsAny<Exception>(() => InventoryCategoryDefinition.Parse(json));

    [Fact]
    public void DuplicateCategoryIsRejected() =>
        Assert.Throws<ArgumentException>(() => InventoryCategoryRegistry.FromJson([
            """{"id":"tools","order":20,"icon":"textures/tools.png"}""",
            """{"id":"tools","order":40,"icon":"textures/tools.png"}"""
        ]));
}
