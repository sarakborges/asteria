using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class HeldBlockPlacementTests
{
    [Fact]
    public void AxisRotationsFollowAuthoredOrderAndWrap()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition(
                "asteria:rotatable",
                orientations: [BlockOrientation.Y, BlockOrientation.Z, BlockOrientation.X]),
        ]);
        var held = new HeldBlockPlacement();
        var block = blocks.GetId("asteria:rotatable");
        held.Select(block, blocks.GetDefinition(block));
        Assert.True(held.CanRotate);
        Assert.Equal(BlockOrientation.Y, held.CurrentCell().Orientation);
        Assert.True(held.Rotate());
        Assert.Equal(BlockOrientation.Z, held.CurrentCell().Orientation);
        Assert.True(held.Rotate());
        Assert.Equal(BlockOrientation.X, held.CurrentCell().Orientation);
        Assert.True(held.Rotate());
        Assert.Equal(BlockOrientation.Y, held.CurrentCell().Orientation);
    }

    [Fact]
    public void NonRotatableBlocksRejectToolAction()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
        ]);
        var held = new HeldBlockPlacement();
        var stone = blocks.GetId("asteria:stone");
        held.Select(stone, blocks.GetDefinition(stone));
        Assert.False(held.CanRotate);
        Assert.False(held.Rotate());
        Assert.Equal(BlockOrientation.Y, held.CurrentCell().Orientation);
    }

    [Fact]
    public void HorizontalFacingTakesPriorityOverAxisRotation()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:faced", tags: ["horizontal_facing"]),
        ]);
        var held = new HeldBlockPlacement();
        var block = blocks.GetId("asteria:faced");
        held.Select(block, blocks.GetDefinition(block));
        foreach (var facing in new[] {
            HorizontalFacing.East, HorizontalFacing.North,
            HorizontalFacing.West, HorizontalFacing.South,
        })
        {
            Assert.True(held.Rotate());
            Assert.Equal(facing, held.CurrentCell().Facing);
        }
    }

    [Fact]
    public void PortableBlockStateIsPreservedWhenSelectedFromInventory()
    {
        var registry = new BlockRegistry([
            new BlockDefinition("asteria:rotatable",
                orientations: [BlockOrientation.Y, BlockOrientation.X]),
        ]);
        var held = new HeldBlockPlacement();
        var block = registry.GetId("asteria:rotatable");
        var source = BlockStateSnapshot.FromCell(new VoxelCell(
            block,
            TextureRotation.Degrees90,
            BlockOrientation.X,
            HorizontalFacing.West,
            state: 7));
        held.Select(source, registry.GetDefinition(block));
        var result = held.CurrentCell();
        Assert.Equal(TextureRotation.Degrees90, result.TextureRotation);
        Assert.Equal(BlockOrientation.X, result.Orientation);
        Assert.Equal(HorizontalFacing.West, result.Facing);
        Assert.Equal((ushort)7, result.State);
        held.Rotate();
        Assert.Equal(BlockOrientation.Y, held.CurrentCell().Orientation);
        Assert.Equal((ushort)7, held.CurrentCell().State);
    }

    [Fact]
    public void SelectingAnotherBlockResetsRotation()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:one", orientations: [
                BlockOrientation.Y, BlockOrientation.X]),
            new BlockDefinition("asteria:two"),
        ]);
        var held = new HeldBlockPlacement();
        var one = blocks.GetId("asteria:one");
        var two = blocks.GetId("asteria:two");
        held.Select(one, blocks.GetDefinition(one));
        held.Rotate();
        held.Select(two, blocks.GetDefinition(two));
        Assert.False(held.CanRotate);
        Assert.Equal(BlockOrientation.Y, held.CurrentCell().Orientation);
        Assert.Equal(two, held.CurrentCell().Block);
    }
}
