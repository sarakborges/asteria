using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class StructureVoxelRotationTests
{
    [Theory]
    [InlineData(StructureRotation.Degrees0, 1, 2, 3)]
    [InlineData(StructureRotation.Degrees90, 4, 2, 1)]
    [InlineData(StructureRotation.Degrees180, 6, 2, 4)]
    [InlineData(StructureRotation.Degrees270, 3, 2, 6)]
    public void SculptedVoxelOccupancyFollowsGlobalYawAndRoundTrips(
        StructureRotation rotation, int x, int y, int z)
    {
        var asymmetric = MicroblockMask.Empty.Edit(
            1, 2, 3, MicroblockResolution.ExtraThin, occupied: true);
        var rotated = asymmetric.RotateAroundY(rotation);
        Assert.Equal(1, rotated.OccupiedCount);
        Assert.True(rotated.Contains(x, y, z));
        Assert.Equal(asymmetric, rotated.RotateAroundY(Inverse(rotation)));
        Assert.Equal(asymmetric.OccupiedCount, rotated.OccupiedCount);
    }

    [Fact]
    public void FourQuarterTurnsRestoreComplexMaskExactly()
    {
        var source = MicroblockMask.Full
            .Edit(1, 0, 3, MicroblockResolution.Thick, false)
            .Edit(7, 6, 7, MicroblockResolution.ExtraThin, false)
            .Edit(2, 5, 5, MicroblockResolution.ExtraThin, false);
        var rotated = source;
        for (var turn = 0; turn < 4; turn++)
            rotated = rotated.RotateAroundY(StructureRotation.Degrees90);
        Assert.Equal(source, rotated);
        Assert.Equal(source.OccupiedCount, rotated.OccupiedCount);
        Assert.Equal(MicroblockMask.Empty,
            MicroblockMask.Empty.RotateAroundY(StructureRotation.Degrees270));
        Assert.Equal(MicroblockMask.Full,
            MicroblockMask.Full.RotateAroundY(StructureRotation.Degrees90));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            source.RotateAroundY((StructureRotation)100));
    }

    [Fact]
    public void AllFaceDirectionsMatchExistingStructureOffsetRotation()
    {
        foreach (var rotation in Enum.GetValues<StructureRotation>())
        foreach (var face in Enum.GetValues<BlockFace>())
        {
            var rotated = StructureVoxelRotation.RotateFace(face, rotation);
            var restored = StructureVoxelRotation.RotateFace(
                rotated, Inverse(rotation));
            Assert.Equal(face, restored);
            if (face is BlockFace.Top or BlockFace.Bottom)
                Assert.Equal(face, rotated);
            else
                Assert.NotEqual(BlockFace.Top, rotated);
        }
        Assert.Equal(BlockFace.Left,
            StructureVoxelRotation.RotateFace(BlockFace.Front,
                StructureRotation.Degrees90));
        Assert.Equal(BlockFace.Front,
            StructureVoxelRotation.RotateFace(BlockFace.Right,
                StructureRotation.Degrees90));
    }

    [Fact]
    public void SurfaceRotationPreservesOrderedLayersAndCorrectsTopBottomUvs()
    {
        var surface = new BlockSurfaceState("asteria:yellow", [
            new AttachedBlockLayer(BlockFace.Top, "asteria:moss",
                TextureRotation.Degrees90),
            new AttachedBlockLayer(BlockFace.Front, "asteria:moss",
                TextureRotation.Degrees180),
            new AttachedBlockLayer(BlockFace.Bottom, "asteria:moss",
                TextureRotation.Degrees270),
            new AttachedBlockLayer(BlockFace.Top, "asteria:ivy")
        ]);
        var quarter = StructureVoxelRotation.RotateSurface(
            surface, StructureRotation.Degrees90);
        Assert.Equal(surface.DyeId, quarter.DyeId);
        Assert.Equal(surface.Layers.Select(l => l.LayerId),
            quarter.Layers.Select(l => l.LayerId));
        Assert.Equal(new[]
        {
            BlockFace.Top, BlockFace.Left, BlockFace.Bottom, BlockFace.Top
        }, quarter.Layers.Select(l => l.Face));
        Assert.Equal(new[]
        {
            TextureRotation.Degrees0,
            TextureRotation.Degrees180,
            TextureRotation.Degrees0,
            TextureRotation.Degrees270
        }, quarter.Layers.Select(l => l.Rotation));
        Assert.Equal(surface, StructureVoxelRotation.RotateSurface(
            quarter, StructureRotation.Degrees270));
        Assert.Same(surface, StructureVoxelRotation.RotateSurface(
            surface, StructureRotation.Degrees0));

        foreach (var rotation in Enum.GetValues<StructureRotation>())
            Assert.Equal(surface, StructureVoxelRotation.RotateSurface(
                StructureVoxelRotation.RotateSurface(surface, rotation),
                Inverse(rotation)));
    }

    [Theory]
    [InlineData(StructureRotation.Degrees90, HorizontalFacing.West)]
    [InlineData(StructureRotation.Degrees180, HorizontalFacing.North)]
    [InlineData(StructureRotation.Degrees270, HorizontalFacing.East)]
    public void HorizontalFacingFollowsStructureYaw(
        StructureRotation rotation, HorizontalFacing expected)
    {
        Assert.Equal(expected, StructureVoxelRotation.RotateFacing(
            HorizontalFacing.South, rotation));
        Assert.Equal(HorizontalFacing.South,
            StructureVoxelRotation.RotateFacing(expected, Inverse(rotation)));
    }

    [Fact]
    public void MetadataRotationDoesNotDestroyBlockIdentityTextureOrState()
    {
        var source = new VoxelCell(new BlockRuntimeId(1),
            textureRotation: TextureRotation.Degrees270,
            orientation: BlockOrientation.X,
            facing: HorizontalFacing.East, state: 49);
        var quarter = StructureVoxelRotation.RotateCell(
            source, StructureRotation.Degrees90);
        Assert.Equal(source.Block, quarter.Block);
        Assert.Equal(source.TextureRotation, quarter.TextureRotation);
        Assert.Equal(source.State, quarter.State);
        Assert.Equal(BlockOrientation.Z, quarter.Orientation);
        Assert.Equal(HorizontalFacing.South, quarter.Facing);
        Assert.Equal(source, StructureVoxelRotation.RotateCell(
            quarter, StructureRotation.Degrees270));
    }

    [Fact]
    public void RotationValidationRejectsFaceLimitedLayerDefinitions()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone")
        ]);
        var layers = AttachedLayerRegistry.FromJson([
            """
            {"id":"asteria:front_only",
             "texture":"textures/layers/front.png","faces":["front"]}
            """
        ]);
        var detail = new StructureVoxelState(
            TextureRotation.Degrees0,
            HorizontalFacing.South,
            0,
            MicroblockMask.Empty,
            new BlockSurfaceState(null, [
                new AttachedBlockLayer(BlockFace.Front, "asteria:front_only")
            ]));
        var voxel = new StructureVoxelDefinition(0, 0, 0,
            "asteria:stone", BlockOrientation.Y, detail);
        new StructureRegistry([
            new StructureDefinition("asteria:still", false, default, [voxel])
        ]).ValidateBlocks(blocks, attachedLayers: layers);
        Assert.Throws<ArgumentException>(() => new StructureRegistry([
            new StructureDefinition("asteria:rotating", true, default, [voxel])
        ]).ValidateBlocks(blocks, attachedLayers: layers));
    }

    private static StructureRotation Inverse(StructureRotation rotation) =>
        rotation switch
        {
            StructureRotation.Degrees0 => StructureRotation.Degrees0,
            StructureRotation.Degrees90 => StructureRotation.Degrees270,
            StructureRotation.Degrees180 => StructureRotation.Degrees180,
            StructureRotation.Degrees270 => StructureRotation.Degrees90,
            _ => throw new ArgumentOutOfRangeException(nameof(rotation))
        };
}
