using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockTextureSetTests
{
    [Fact]
    public void MissingFaceFallsBackToFirstAuthoredTextureLayers()
    {
        var top =
            new BlockTextureLayer("textures/blocks/test_top.png", dyable: true);
        var overlay =
            new BlockTextureLayer("textures/blocks/test_overlay.png");

        var textures = new BlockTextureSet(top: [top, overlay]);

        var resolved = textures.ResolveForFace(BlockFace.Left);

        Assert.Equal(2, resolved.Count);
        Assert.Equal(top, resolved[0]);
        Assert.Equal(overlay, resolved[1]);
    }

    [Fact]
    public void DirectFaceOverridesFallback()
    {
        var top = new BlockTextureLayer("textures/blocks/top.png");
        var side = new BlockTextureLayer("textures/blocks/side.png");
        var textures = new BlockTextureSet(top: [top], left: [side]);

        Assert.Equal(side, textures.ResolveForFace(BlockFace.Left).Single());
        Assert.Equal(top, textures.ResolveForFace(BlockFace.Right).Single());
    }

    [Fact]
    public void AllLayersEnumeratesAuthoredLayersWithoutInventingFallbackCopies()
    {
        var shared = new BlockTextureLayer("textures/blocks/shared.png");
        var overlay = new BlockTextureLayer("textures/blocks/overlay.png");
        var textures = new BlockTextureSet(
            top: [shared],
            left: [shared, overlay]);

        var layers = textures.AllLayers().ToArray();

        Assert.Equal(3, layers.Length);
        Assert.Equal(2, layers.Count(layer => layer == shared));
        Assert.Single(layers, layer => layer == overlay);
    }
}
