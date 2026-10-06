using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockRenderModeTests
{
    [Theory]
    [InlineData(BlockRenderMode.Opaque, true)]
    [InlineData(BlockRenderMode.Cutout, true)]
    [InlineData(BlockRenderMode.Translucent, false)]
    public void GreedyMergingMatchesTransparencyOrderingRequirement(
        BlockRenderMode mode,
        bool expected)
    {
        Assert.Equal(
            expected,
            BlockRenderModePolicy.CanGreedyMerge(mode));
    }

    [Fact]
    public void JsonAlphaSettingsMapToRenderModes()
    {
        var opaque = BlockDefinitionJson.Parse(
            """{ "id": "asteria:opaque" }""");
        var cutout = BlockDefinitionJson.Parse(
            """{ "id": "asteria:cutout", "alphaCutoff": 0.5 }""");
        var translucent = BlockDefinitionJson.Parse(
            """{ "id": "asteria:glass", "alphaBlend": true }""");

        Assert.Equal(
            BlockRenderMode.Opaque,
            opaque.RenderMode);
        Assert.Equal(
            BlockRenderMode.Cutout,
            cutout.RenderMode);
        Assert.Equal(
            BlockRenderMode.Translucent,
            translucent.RenderMode);
    }
}
