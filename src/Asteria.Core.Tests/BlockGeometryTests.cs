using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockGeometryTests
{
    [Fact]
    public void SpikeSquareTaperMatchesItsVoxelOccupancy()
    {
        var smooth = BlockShapeDefinition.Spike(taperPower: 1f);
        var curved = BlockShapeDefinition.Spike(taperPower: 1.5f);
        var state = SpikeSegmentState.Encode(1, 5, false);
        Assert.True(SpikeSegmentState.HalfWidthAt(curved, state, 0.5f) >=
                    SpikeSegmentState.HalfWidthAt(smooth, state, 0.5f));
        var width = SpikeSegmentState.HalfWidthAt(curved, state, 0.5f);
        Assert.Equal(MathF.Round(width * BlockGeometry.Resolution),
            width * BlockGeometry.Resolution, 4);

        var block = new BlockDefinition("asteria:test_spike",
            shape: BlockShapeDefinition.Spike());
        var single = new VoxelCell(new BlockRuntimeId(1),
            state: SpikeSegmentState.Encode(0, 1, false));
        Assert.True(BlockGeometry.IsOccupied(
            block, single, MicroblockMask.Empty, 4, 0, 4));
        Assert.False(BlockGeometry.IsOccupied(
            block, single, MicroblockMask.Empty, 1, 0, 1));
        Assert.ThrowsAny<ArgumentException>(() =>
            BlockShapeDefinition.Spike(taperPower: 0.4f));
    }

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
    public void SurfaceLayerTouchesBottomButNotTopFace()
    {
        var definition =
            new BlockDefinition(
                "asteria:layer",
                shape:
                    BlockShapeDefinition.SurfaceLayer(
                        0.125f,
                        "asteria:stone"));
        var cell =
            new VoxelCell(
                new BlockRuntimeId(1));

        Assert.True(
            BlockGeometry.TouchesFace(
                definition,
                cell,
                MicroblockMask.Empty,
                BlockFace.Bottom));
        Assert.False(
            BlockGeometry.TouchesFace(
                definition,
                cell,
                MicroblockMask.Empty,
                BlockFace.Top));
    }

    [Fact]
    public void MicroblockFaceContactUsesActualMask()
    {
        var definition =
            new BlockDefinition(
                "asteria:micro");
        var bottomOnly =
            MicroblockMask.Empty.Edit(
                0,
                0,
                0,
                MicroblockResolution.ExtraThin,
                occupied: true);
        var topOnly =
            MicroblockMask.Empty.Edit(
                0,
                MicroblockMask.Edge - 1,
                0,
                MicroblockResolution.ExtraThin,
                occupied: true);
        var cell =
            new VoxelCell(
                new BlockRuntimeId(1),
                microblockMaskId: 1);

        Assert.False(
            BlockGeometry.TouchesFace(
                definition,
                cell,
                bottomOnly,
                BlockFace.Top));
        Assert.True(
            BlockGeometry.TouchesFace(
                definition,
                cell,
                topOnly,
                BlockFace.Top));
    }

    [Fact]
    public void FaceCoverageRequiresEntireDependentFootprint()
    {
        var supportDefinition =
            new BlockDefinition(
                "asteria:support");
        var dependentDefinition =
            new BlockDefinition(
                "asteria:layer",
                shape:
                    BlockShapeDefinition.SurfaceLayer(
                        0.125f,
                        "asteria:support"));
        var supportCell =
            new VoxelCell(
                new BlockRuntimeId(1),
                microblockMaskId: 1);
        var dependentCell =
            new VoxelCell(
                new BlockRuntimeId(2));

        var oneTopMicroblock =
            MicroblockMask.Empty.Edit(
                0,
                MicroblockMask.Edge - 1,
                0,
                MicroblockResolution.ExtraThin,
                occupied: true);

        Assert.False(
            BlockGeometry.SupportsFaceCoverage(
                supportDefinition,
                supportCell,
                oneTopMicroblock,
                BlockFace.Top,
                dependentDefinition,
                dependentCell,
                MicroblockMask.Empty,
                BlockFace.Bottom));

        Assert.True(
            BlockGeometry.SupportsFaceCoverage(
                supportDefinition,
                new VoxelCell(
                    new BlockRuntimeId(1)),
                MicroblockMask.Empty,
                BlockFace.Top,
                dependentDefinition,
                dependentCell,
                MicroblockMask.Empty,
                BlockFace.Bottom));
    }

    [Fact]
    public void PartialGeometryIntersectionUsesOnlyOverlappedFineCells()
    {
        var definition =
            new BlockDefinition(
                "asteria:layer",
                shape:
                    BlockShapeDefinition.SurfaceLayer(
                        0.125f,
                        "asteria:stone"));
        var cell =
            new VoxelCell(
                new BlockRuntimeId(1));
        var position =
            new WorldVoxelCoord(
                4,
                4,
                4);

        Assert.True(
            BlockGeometry.Intersects(
                definition,
                cell,
                MicroblockMask.Empty,
                position,
                new WorldAabb(
                    new System.Numerics.Vector3(
                        4.2f,
                        4.02f,
                        4.2f),
                    new System.Numerics.Vector3(
                        4.8f,
                        4.08f,
                        4.8f))));
        Assert.False(
            BlockGeometry.Intersects(
                definition,
                cell,
                MicroblockMask.Empty,
                position,
                new WorldAabb(
                    new System.Numerics.Vector3(
                        4.2f,
                        4.5f,
                        4.2f),
                    new System.Numerics.Vector3(
                        4.8f,
                        4.9f,
                        4.8f))));
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
