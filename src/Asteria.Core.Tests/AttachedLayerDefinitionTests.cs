using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class AttachedLayerDefinitionTests
{
    [Fact]
    public void AuthoredLayersValidateFacesAndAppearance()
    {
        var definition = AttachedLayerDefinition.Parse(
            """
            {"id":"asteria:foliage_layer","texture":"textures/blocks/grass/side_overlay.png",
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
