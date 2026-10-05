using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockShapeTests
{
    [Fact]
    public void SurfaceLayerIsStackableAndTargetsAFullBlock()
    {
        var shape = BlockShapeDefinition.SurfaceLayer(1f / 8f, "asteria:sand");

        Assert.Equal(BlockShapeKind.Layer, shape.Kind);
        Assert.Equal(BlockLayerPlacement.Surface, shape.LayerPlacement);
        Assert.Equal(0.125f, shape.Thickness);
        Assert.Equal("asteria:sand", shape.StackToBlockId);
        Assert.True(shape.IsStackableLayer);
        Assert.False(shape.IsCenteredLayer);
    }

    [Fact]
    public void CenteredLayerUsesSamePartialShapeFamilyWithoutStackTarget()
    {
        var definition = BlockDefinitionJson.Parse("""
            {
              "id": "asteria:test_portal",
              "shape": {
                "type": "layer",
                "placement": "center",
                "thickness": 0.0625
              }
            }
            """);

        Assert.Equal(BlockShapeKind.Layer, definition.Shape.Kind);
        Assert.Equal(BlockLayerPlacement.Center, definition.Shape.LayerPlacement);
        Assert.Equal(0.0625f, definition.Shape.Thickness);
        Assert.Null(definition.Shape.StackToBlockId);
        Assert.True(definition.Shape.IsCenteredLayer);
        Assert.False(definition.Shape.IsStackableLayer);
    }

    [Fact]
    public void HollowShapeKeepsWallThicknessSeparateFromOrientation()
    {
        var definition = BlockDefinitionJson.Parse("""
            {
              "id": "asteria:test_hollow",
              "shape": {
                "type": "hollow",
                "wallThickness": 0.0625
              },
              "orientations": ["y", "z", "x"]
            }
            """);

        Assert.Equal(BlockShapeKind.Hollow, definition.Shape.Kind);
        Assert.Equal(0.0625f, definition.Shape.WallThickness);
        Assert.Equal(
            [BlockOrientation.Y, BlockOrientation.Z, BlockOrientation.X],
            definition.Orientations);
        Assert.True(definition.IsRotatable);
    }
}
