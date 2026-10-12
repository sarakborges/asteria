using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ObjectIconTintTests
{
    [Fact]
    public void TintMultipliesRgbWithoutTouchingAlphaOrTheSource()
    {
        byte[] source = [255, 128, 64, 255, 30, 200, 255, 100, 10, 20, 30, 0];
        var result = ObjectIconTint.Apply(source, new BlockPreviewColor(128, 64, 255));
        Assert.Equal(new byte[]
        {
            128, 32, 64, 255,
            15, 50, 255, 100,
            5, 5, 30, 0
        }, result);
        Assert.Equal(255, source[0]);
        Assert.Equal(200, source[5]);
    }

    [Fact]
    public void WhiteTintIsIdentityAndBlackTintKeepsTransparency()
    {
        byte[] source = [76, 127, 190, 70];
        Assert.Equal(source, ObjectIconTint.Apply(
            source, new BlockPreviewColor(255, 255, 255)));
        Assert.Equal(new byte[] { 0, 0, 0, 70 },
            ObjectIconTint.Apply(source, new BlockPreviewColor(0, 0, 0)));
    }

    [Fact]
    public void MalformedOrUnboundedRgbaBuffersAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ObjectIconTint.Apply([], new BlockPreviewColor(20, 30, 40)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ObjectIconTint.Apply([1, 2, 3], new BlockPreviewColor(20, 30, 40)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ObjectIconTint.Apply(new byte[(ObjectIconTint.MaximumPixels + 1) * 4],
                new BlockPreviewColor(20, 30, 40)));
    }

    [Fact]
    public void RealPackGrassObjectAuthorsPreviewTintInsteadOfMagentaFallback()
    {
        var file = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", "objects", "grass.json");
        var objectDefinition = ObjectDefinition.Parse(File.ReadAllText(file));
        Assert.Equal(BlockTint.Foliage, objectDefinition.Tint);
        Assert.NotEqual(BlockPreviewColor.Missing, objectDefinition.TintFallback);
        Assert.Equal("textures/objects/grass.png", objectDefinition.Icon);
    }
}
