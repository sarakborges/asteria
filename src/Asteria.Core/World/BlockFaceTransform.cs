namespace Asteria.Core.World;

public static class BlockFaceTransform
{
    public static BlockFace SourceFaceForWorldFace(
        BlockFace worldFace,
        VoxelCell cell,
        BlockDefinition definition)
    {
        var face = definition.UsesHorizontalFacing
            ? UndoHorizontalFacing(worldFace, cell.Facing)
            : worldFace;

        return UndoOrientation(face, cell.Orientation);
    }

    private static BlockFace UndoOrientation(BlockFace face, BlockOrientation orientation) =>
        orientation switch
        {
            BlockOrientation.Y => face,
            BlockOrientation.Z => face switch
            {
                BlockFace.Front => BlockFace.Top,
                BlockFace.Back => BlockFace.Bottom,
                BlockFace.Bottom => BlockFace.Front,
                BlockFace.Top => BlockFace.Back,
                _ => face,
            },
            BlockOrientation.X => face switch
            {
                BlockFace.Right => BlockFace.Top,
                BlockFace.Left => BlockFace.Bottom,
                BlockFace.Bottom => BlockFace.Right,
                BlockFace.Top => BlockFace.Left,
                _ => face,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(orientation)),
        };

    private static BlockFace UndoHorizontalFacing(BlockFace face, HorizontalFacing facing)
    {
        var turns = facing switch
        {
            HorizontalFacing.South => 0,
            HorizontalFacing.East => 1,
            HorizontalFacing.North => 2,
            HorizontalFacing.West => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(facing)),
        };

        for (var index = 0; index < turns; index++)
        {
            face = face switch
            {
                BlockFace.Right => BlockFace.Front,
                BlockFace.Back => BlockFace.Right,
                BlockFace.Left => BlockFace.Back,
                BlockFace.Front => BlockFace.Left,
                _ => face,
            };
        }

        return face;
    }
}
