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
    [Fact]
    public void PreviewFaceSelectionRespectsExistingCoreOrientationAndFacing()
    {
        var definition = new BlockDefinition("asteria:oriented_preview",
            tags: ["horizontal_facing"],
            orientations: [BlockOrientation.Y, BlockOrientation.Z, BlockOrientation.X],
            textures: new BlockTextureSet(
                top: [new BlockTextureLayer("textures/end.png")],
                front: [new BlockTextureLayer("textures/front.png")],
                back: [new BlockTextureLayer("textures/back.png")],
                right: [new BlockTextureLayer("textures/right.png")],
                left: [new BlockTextureLayer("textures/left.png")],
                bottom: [new BlockTextureLayer("textures/bottom.png")]));
        var registry = new BlockRegistry([definition]);
        var south = new VoxelCell(registry.GetId(definition.Id));
        Assert.Equal(BlockFace.Top, BlockFaceTransform.SourceFaceForWorldFace(
            BlockFace.Top, south, definition));
        Assert.Equal(BlockFace.Front, BlockFaceTransform.SourceFaceForWorldFace(
            BlockFace.Front, south, definition));

        var laidDown = south.WithOrientation(BlockOrientation.Z);
        Assert.Equal(BlockFace.Back, BlockFaceTransform.SourceFaceForWorldFace(
            BlockFace.Top, laidDown, definition));
        Assert.Equal(BlockFace.Top, BlockFaceTransform.SourceFaceForWorldFace(
            BlockFace.Front, laidDown, definition));
        var east = south.WithFacing(HorizontalFacing.East);
        Assert.Equal(BlockFace.Left, BlockFaceTransform.SourceFaceForWorldFace(
            BlockFace.Front, east, definition));

        // The preview is not allowed to invent a second face transform.
        foreach (var face in new[] { BlockFace.Top, BlockFace.Front, BlockFace.Right })
        {
            var source = BlockFaceTransform.SourceFaceForWorldFace(
                face, laidDown, definition);
            Assert.Single(definition.Textures.ResolveForFace(source));
        }
    }

}
