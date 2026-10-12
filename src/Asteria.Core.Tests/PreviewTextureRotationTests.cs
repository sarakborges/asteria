using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PreviewTextureRotationTests
{
    // Nonsquare input proves that a quarter turn swaps the axis lengths.
    // Unique alpha values prove preservation of full RGBA, not RGB only.
    private static readonly byte[] Source =
    [
        1, 10, 20, 11,  2, 20, 30, 22,
        3, 30, 40, 33,  4, 40, 50, 44,
        5, 50, 60, 55,  6, 60, 70, 66,
    ];

    [Theory]
    [InlineData(TextureRotation.Degrees0, 2, 3, new byte[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData(TextureRotation.Degrees90, 3, 2, new byte[] { 2, 4, 6, 1, 3, 5 })]
    [InlineData(TextureRotation.Degrees180, 2, 3, new byte[] { 6, 5, 4, 3, 2, 1 })]
    [InlineData(TextureRotation.Degrees270, 3, 2, new byte[] { 5, 3, 1, 6, 4, 2 })]
    public void RotationsFollowTheExactTerrainUvSamplingOrder(
        TextureRotation rotation, int expectedWidth, int expectedHeight,
        byte[] ids)
    {
        var rotated = PreviewTextureRotation.Rotate(Source, 2, 3, rotation);
        Assert.Equal(expectedWidth, rotated.Width);
        Assert.Equal(expectedHeight, rotated.Height);
        Assert.Equal(ids, Enumerable.Range(0, ids.Length)
            .Select(index => rotated.Rgba[index * 4]).ToArray());
        for (var i = 0; i < ids.Length; i++)
        {
            var original = ids[i] - 1;
            Assert.Equal(Source[original * 4 + 1], rotated.Rgba[i * 4 + 1]);
            Assert.Equal(Source[original * 4 + 2], rotated.Rgba[i * 4 + 2]);
            Assert.Equal(Source[original * 4 + 3], rotated.Rgba[i * 4 + 3]);
        }
        Assert.Equal((byte)1, Source[0]);
    }

    [Fact]
    public void InvalidPixelBuffersAndRotationAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewTextureRotation.Rotate([], 2, 3, TextureRotation.Degrees0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewTextureRotation.Rotate(new byte[12], -1, 3,
                TextureRotation.Degrees0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewTextureRotation.Rotate(new byte[8], 1, 2,
                (TextureRotation)255));
    }

    [Fact]
    public void QuarterTurnForHorizontalLogsUsesExistingWorldMeshPolicy()
    {
        // World-side face and authored rotation feed exactly the same Core
        // policy as the terrain mesh, not a separately inferred icon angle.
        var orientation = BlockOrientation.Z;
        var face = BlockFace.Right;
        var turn = BlockUvRotation.ForWorldFace(
            face, orientation, TextureRotation.Degrees270, false);
        Assert.Equal(TextureRotation.Degrees90, turn);

        var rotated = PreviewTextureRotation.Rotate(Source, 2, 3, turn);
        Assert.Equal(new byte[] { 2, 4, 6, 1, 3, 5 },
            Enumerable.Range(0, 6)
                .Select(index => rotated.Rgba[index * 4]).ToArray());
    }
}
