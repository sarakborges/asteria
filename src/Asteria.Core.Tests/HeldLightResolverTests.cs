using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class HeldLightResolverTests
{
    [Fact]
    public void SelectedAuthoredEmissiveBlockUsesExactRgbChannels()
    {
        var emission = new BlockLightEmission(3, 7, 12);
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:glowing", lightEmission: emission),
            new BlockDefinition("asteria:stone")]);
        var selected = Stack(blocks, "asteria:glowing");
        Assert.Equal(emission, HeldLightResolver.Resolve(selected, blocks));
        Assert.Equal(default, HeldLightResolver.Resolve(
            Stack(blocks, "asteria:stone"), blocks));
    }

    [Fact]
    public void NonBlockAndInvalidSelectedIdentityCannotEmit()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:glowing",
                lightEmission: new BlockLightEmission(15, 5, 0)),
            new BlockDefinition("asteria:stone")]);

        Assert.Equal(default, HeldLightResolver.Resolve(null, blocks));
        Assert.Equal(default, HeldLightResolver.Resolve(
            new InventoryStack(InventoryEntry.FromItem("asteria:glowing")),
            blocks));
        Assert.Equal(default, HeldLightResolver.Resolve(
            new InventoryStack(InventoryEntry.FromBlock(
                "asteria:unknown", Stack(blocks, "asteria:glowing").Block!)),
            blocks));
        Assert.Equal(default, HeldLightResolver.Resolve(
            new InventoryStack(InventoryEntry.FromBlock(
                "asteria:glowing", Stack(blocks, "asteria:stone").Block!)),
            blocks));
    }

    [Fact]
    public void ExistingPackEmissiveObjectNeedsNoExtraHeldLightData()
    {
        var data = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "packs", "default",
                "data", "objects", "mushroom_glowcap.json"));
        var definition = Asteria.Core.Content.ObjectDefinition.Parse(data);
        var blocks = new BlockRegistry([]);
        var objects = Asteria.Core.Content.PackContentRegistry<Asteria.Core.Content.ObjectDefinition>
            .FromJson([data], Asteria.Core.Content.ObjectDefinition.Parse,
                obj => obj.Id);
        Assert.Equal(new BlockLightEmission(3, 7, 10),
            HeldLightResolver.Resolve(
                new InventoryStack(InventoryEntry.FromObject(definition.Id)),
                blocks, objects));
        Assert.Equal(default, HeldLightResolver.Resolve(
            new InventoryStack(InventoryEntry.FromItem(definition.Id)),
            blocks, objects));
    }

    private static InventoryStack Stack(BlockRegistry blocks, string id) =>
        new(InventoryEntry.FromBlock(id,
            BlockStateSnapshot.FromCell(new VoxelCell(blocks.GetId(id)))));
}
