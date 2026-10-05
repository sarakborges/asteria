using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockGeometryTests
{
    [Fact]
    public void SurfaceLayerOccupiesOnlyItsAuthoredThickness()
    {
        var definition = new BlockDefinition(
            "asteria:test_layer",
            shape: BlockShapeDefinition.SurfaceLayer(1f / 8f, "asteria:stone"));
        var cell = new VoxelCell(new BlockRuntimeId(1));

        Assert.True(BlockGeometry.IsOccupied(definition, cell, MicroblockMask.Full, 12, 0, 12));
        Assert.True(BlockGeometry.IsOccupied(definition, cell, MicroblockMask.Full, 12, 3, 12));
        Assert.False(BlockGeometry.IsOccupied(definition, cell, MicroblockMask.Full, 12, 4, 12));
    }

    [Fact]
    public void CenteredLayerIsSymmetricAroundVoxelCenter()
    {
        var definition = new BlockDefinition(
            "asteria:test_portal",
            shape: BlockShapeDefinition.CenteredLayer(1f / 16f));
        var cell = new VoxelCell(new BlockRuntimeId(1));

        Assert.False(BlockGeometry.IsOccupied(definition, cell, MicroblockMask.Full, 12, 14, 12));
        Assert.True(BlockGeometry.IsOccupied(definition, cell, MicroblockMask.Full, 12, 15, 12));
        Assert.True(BlockGeometry.IsOccupied(definition, cell, MicroblockMask.Full, 12, 16, 12));
        Assert.False(BlockGeometry.IsOccupied(definition, cell, MicroblockMask.Full, 12, 17, 12));
    }

    [Fact]
    public void LayerOrientationMovesItsNormalAxis()
    {
        var definition = new BlockDefinition(
            "asteria:test_portal",
            shape: BlockShapeDefinition.CenteredLayer(1f / 8f),
            orientations: [BlockOrientation.Y, BlockOrientation.Z, BlockOrientation.X]);

        var alongX = new VoxelCell(new BlockRuntimeId(1), orientation: BlockOrientation.X);
        var alongZ = new VoxelCell(new BlockRuntimeId(1), orientation: BlockOrientation.Z);

        Assert.True(BlockGeometry.IsOccupied(definition, alongX, MicroblockMask.Full, 15, 2, 2));
        Assert.False(BlockGeometry.IsOccupied(definition, alongX, MicroblockMask.Full, 2, 15, 2));

        Assert.True(BlockGeometry.IsOccupied(definition, alongZ, MicroblockMask.Full, 2, 2, 15));
        Assert.False(BlockGeometry.IsOccupied(definition, alongZ, MicroblockMask.Full, 2, 15, 2));
    }

    [Fact]
    public void HollowShapeLeavesAnOpenAxisAlignedCore()
    {
        var definition = new BlockDefinition(
            "asteria:test_hollow",
            shape: BlockShapeDefinition.Hollow(1f / 16f),
            orientations: [BlockOrientation.Y, BlockOrientation.Z, BlockOrientation.X]);

        var vertical = new VoxelCell(new BlockRuntimeId(1));
        Assert.True(BlockGeometry.IsOccupied(definition, vertical, MicroblockMask.Full, 0, 16, 16));
        Assert.False(BlockGeometry.IsOccupied(definition, vertical, MicroblockMask.Full, 16, 0, 16));
        Assert.False(BlockGeometry.IsOccupied(definition, vertical, MicroblockMask.Full, 16, 31, 16));

        var alongX = vertical.WithOrientation(BlockOrientation.X);
        Assert.False(BlockGeometry.IsOccupied(definition, alongX, MicroblockMask.Full, 0, 16, 16));
        Assert.False(BlockGeometry.IsOccupied(definition, alongX, MicroblockMask.Full, 31, 16, 16));
        Assert.True(BlockGeometry.IsOccupied(definition, alongX, MicroblockMask.Full, 16, 0, 16));
    }

    [Fact]
    public void MicroblockMaskOverridesDefinitionShapeAtEightCubedResolution()
    {
        var definition = new BlockDefinition("asteria:test");
        var mask = MicroblockMask.Full.Edit(
            0,
            0,
            0,
            MicroblockResolution.Thick,
            occupied: false);
        var cell = new VoxelCell(new BlockRuntimeId(1), microblockMaskId: 1);

        Assert.False(BlockGeometry.IsOccupied(definition, cell, mask, 0, 0, 0));
        Assert.False(BlockGeometry.IsOccupied(definition, cell, mask, 15, 15, 15));
        Assert.True(BlockGeometry.IsOccupied(definition, cell, mask, 16, 0, 0));
    }

    [Fact]
    public void OrientedFacesResolveBackToAuthoredTextureFaces()
    {
        var definition = new BlockDefinition(
            "asteria:log",
            orientations: [BlockOrientation.Y, BlockOrientation.Z, BlockOrientation.X]);

        var alongZ = new VoxelCell(new BlockRuntimeId(1), orientation: BlockOrientation.Z);
        var alongX = new VoxelCell(new BlockRuntimeId(1), orientation: BlockOrientation.X);

        Assert.Equal(
            BlockFace.Top,
            BlockFaceTransform.SourceFaceForWorldFace(BlockFace.Front, alongZ, definition));
        Assert.Equal(
            BlockFace.Top,
            BlockFaceTransform.SourceFaceForWorldFace(BlockFace.Right, alongX, definition));
    }
}
