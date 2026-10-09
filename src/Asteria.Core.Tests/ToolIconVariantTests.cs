using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ToolIconVariantTests
{
    private const string Bucket = """
      {"id":"asteria:bucket","category":"tools",
       "icon":"textures/tools/iron_bucket_empty.png",
       "leftBehavior":"asteria:none","rightBehavior":"asteria:bucket/use",
       "iconVariants":[
        {"metadataKey":"contained_fluid","metadataValue":"asteria:water",
         "icon":"textures/tools/iron_bucket_water.png"}]}
      """;

    [Fact]
    public void AuthoredBucketVisualChangesWithPortableMetadata()
    {
        var tool = ToolDefinition.Parse(Bucket);
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            [Bucket], ToolDefinition.Parse, x => x.Id);
        var resolver = new HeldVisualResolver(
            new BlockRegistry([]),
            PackContentRegistry<ItemDefinition>.FromJson(
                [], ItemDefinition.Parse, x => x.Id),
            tools, new AttachedLayerRegistry([]));
        Assert.Equal("textures/tools/iron_bucket_water.png",
            resolver.Resolve(new InventoryStack(InventoryEntry.FromTool(
                tool.Id, new Dictionary<string,string> {
                    ["contained_fluid"] = "asteria:water"
                })))!.FrontTexture);
        Assert.Equal("textures/tools/iron_bucket_empty.png",
            resolver.Resolve(new InventoryStack(InventoryEntry.FromTool(
                tool.Id)))!.FrontTexture);
    }

    [Fact]
    public void DuplicateVariantSelectorsAreRejected()
    {
        var invalid = Bucket.Replace(
          "}]}", "},{\"metadataKey\":\"contained_fluid\"," +
          "\"metadataValue\":\"asteria:water\",\"icon\":\"textures/other.png\"}]}",
          StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => ToolDefinition.Parse(invalid));
    }
}
