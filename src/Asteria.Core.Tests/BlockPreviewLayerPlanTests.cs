using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockPreviewLayerPlanTests
{
    [Fact]
    public void KeepsRealFaceRotationTintAndAttachmentOrder()
    {
        var registry = new AttachedLayerRegistry([
            new AttachedLayerDefinition("asteria:moss", "textures/layers/moss.png",
                [BlockFace.Top], tint: BlockTint.Foliage),
            new AttachedLayerDefinition("asteria:ore", "textures/layers/ore.png",
                [BlockFace.Top, BlockFace.Front], alphaCutoff: null, alphaBlend: true)
        ]);
        var surface = new BlockSurfaceState(null, [
            new AttachedBlockLayer(BlockFace.Top, "asteria:moss",
                TextureRotation.Degrees90),
            new AttachedBlockLayer(BlockFace.Front, "asteria:ore"),
            new AttachedBlockLayer(BlockFace.Top, "asteria:ore",
                TextureRotation.Degrees270)
        ]);
        var top = BlockPreviewLayerPlan.ForWorldFace(surface, registry, BlockFace.Top);
        Assert.Equal(2, top.Count);
        Assert.Equal("textures/layers/moss.png", top[0].Texture);
        Assert.Equal(TextureRotation.Degrees90, top[0].Rotation);
        Assert.Equal(BlockTint.Foliage, top[0].Tint);
        Assert.Equal(.5f, top[0].AlphaCutoff);
        Assert.Equal("textures/layers/ore.png", top[1].Texture);
        Assert.Equal(TextureRotation.Degrees270, top[1].Rotation);
        Assert.Null(top[1].AlphaCutoff);
        Assert.Single(BlockPreviewLayerPlan.ForWorldFace(
            surface, registry, BlockFace.Front));
        Assert.Empty(BlockPreviewLayerPlan.ForWorldFace(
            surface, registry, BlockFace.Right));
    }

    [Fact]
    public void CutoutAndPartialAlphaPreserveTheUnderlyingBlock()
    {
        byte[] bottom = [10, 20, 30, 255, 10, 20, 30, 255];
        byte[] top = [240, 160, 80, 60, 240, 160, 80, 128];
        var result = PreviewLayerBlend.SourceOver(bottom, top,
            new BlockPreviewColor(128, 255, 255), .5f);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, result[..4]);
        Assert.Equal((byte)65, result[4]);
        Assert.Equal((byte)90, result[5]);
        Assert.Equal((byte)55, result[6]);
        Assert.Equal((byte)255, result[7]);
        Assert.Equal((byte)10, bottom[0]);
    }

    [Fact]
    public void TransparentHostReceivesAlphaAndStackingIsOrdered()
    {
        byte[] transparent = [0, 0, 0, 0];
        byte[] first = [220, 20, 20, 255];
        byte[] second = [10, 200, 10, 128];
        var afterFirst = PreviewLayerBlend.SourceOver(
            transparent, first, new BlockPreviewColor(255, 255, 255));
        var afterSecond = PreviewLayerBlend.SourceOver(
            afterFirst, second, new BlockPreviewColor(255, 255, 255));
        Assert.Equal((byte)255, afterSecond[3]);
        Assert.InRange(afterSecond[0], (byte)114, (byte)116);
        Assert.InRange(afterSecond[1], (byte)109, (byte)111);
        Assert.Equal((byte)15, afterSecond[2]);
    }

    [Fact]
    public void InvalidSizesAndCutoffsAreRejectedBeforeBlending()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewLayerBlend.SourceOver([], [],
                new BlockPreviewColor(255, 255, 255)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewLayerBlend.SourceOver([0, 0, 0, 0], [0, 0, 0],
                new BlockPreviewColor(255, 255, 255)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewLayerBlend.SourceOver([0, 0, 0, 0], [0, 0, 0, 0],
                new BlockPreviewColor(255, 255, 255), float.NaN));
    }
}
