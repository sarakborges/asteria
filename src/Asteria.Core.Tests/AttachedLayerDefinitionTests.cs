using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class AttachedLayerDefinitionTests
{
    [Fact]
    public void AuthoredLayersValidateFacesAndAppearance()
    {
        var definition = AttachedLayerDefinition.Parse(
            """
            {"id":"asteria:layer_foliage","texture":"textures/blocks/grass/side_overlay.png",
             "faces":["top","front"],"tint":"foliage","offset":0.002,"alphaCutoff":0.5}
            """);
        Assert.True(definition.Supports(BlockFace.Top));
        Assert.False(definition.Supports(BlockFace.Back));
        Assert.Equal(BlockTint.Foliage, definition.Tint);
        Assert.Equal(BlockRenderMode.Cutout, definition.RenderMode);
        Assert.Throws<ArgumentException>(() => new AttachedLayerDefinition(
            "asteria:bad", "textures/test.png", [BlockFace.Top, BlockFace.Top]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttachedLayerDefinition(
            "asteria:bad", "textures/test.png", offset: 1f));
    }

    [Fact]
    public void InteractivePropertiesAreAuthoredAndLegacyLayersKeepSelfDrops()
    {
        var ore = AttachedLayerDefinition.Parse(
            """
            {"id":"asteria:ore_iron","texture":"textures/layers/ore.png",
             "scope":"voxel",
             "mining":{"hardness":3,"requiredTools":["pickaxe"],"preferredTools":["pickaxe"]},
             "drops":[{"kind":"item","id":"asteria:ore_iron_raw","quantity":2}]}
            """);
        Assert.Equal(LayerRemovalScope.Voxel, ore.Scope);
        Assert.Equal(3f, ore.Mining.Hardness);
        Assert.Equal("pickaxe", Assert.Single(ore.Mining.RequiredTools));
        var drop = Assert.Single(ore.Drops).ToStack();
        Assert.Equal(InventoryEntryKind.Item, drop.Entry.Kind);
        Assert.Equal("asteria:ore_iron_raw", drop.Id);
        Assert.Equal(2, drop.Quantity);

        var legacy = AttachedLayerDefinition.Parse(
            """{"id":"asteria:moss","texture":"textures/layers/moss.png"}""");
        Assert.Equal(LayerRemovalScope.Face, legacy.Scope);
        Assert.Equal("asteria:moss", Assert.Single(legacy.Drops).Id);
        Assert.Equal(InventoryEntryKind.Layer, legacy.Drops[0].Kind);
        Assert.Throws<FormatException>(() => AttachedLayerDefinition.Parse(
            """{"id":"asteria:bad","texture":"textures/x.png","scope":"both"}"""));
        Assert.Throws<FormatException>(() => AttachedLayerDefinition.Parse(
            """{"id":"asteria:bad","texture":"textures/x.png","drops":[{"kind":"block","id":"asteria:stone"}]}"""));
    }

    [Fact]
    public void RegistryRejectsDuplicateIdsAndSortsDeterministically()
    {
        var a = new AttachedLayerDefinition("asteria:a", "textures/a.png");
        var z = new AttachedLayerDefinition("asteria:z", "textures/z.png");
        var registry = new AttachedLayerRegistry([z, a]);
        Assert.Equal(["asteria:a", "asteria:z"], registry.Definitions.Select(x => x.Id));
        Assert.Throws<ArgumentException>(() => new AttachedLayerRegistry([a, a]));
        Assert.Throws<FormatException>(() => AttachedLayerDefinition.Parse(
            "{\"id\":\"asteria:bad\",\"texture\":\"../outside.png\"}"));
    }
}
