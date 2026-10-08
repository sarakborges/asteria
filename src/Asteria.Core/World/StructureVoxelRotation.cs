namespace Asteria.Core.World;

/// <summary>
/// Y-axis structure rotation for voxel-local directional state. This uses
/// StructureDefinition.RotateOffset as the single world-space convention.
/// </summary>
public static class StructureVoxelRotation
{
    public static VoxelCell RotateCell(VoxelCell cell, StructureRotation rotation)
    {
        Validate(rotation);
        if (cell.IsEmpty || rotation == StructureRotation.Degrees0)
            return cell;
        return new VoxelCell(
            cell.Block,
            cell.TextureRotation,
            StructureDefinition.RotateOrientation(rotation, cell.Orientation),
            RotateFacing(cell.Facing, rotation),
            cell.State);
    }

    public static HorizontalFacing RotateFacing(
        HorizontalFacing facing, StructureRotation rotation)
    {
        Validate(rotation);
        if (!Enum.IsDefined(facing))
            throw new ArgumentOutOfRangeException(nameof(facing));
        // Positive structure yaw transforms +Z (South) into -X (West).
        return (HorizontalFacing)(((int)facing + 4 - (int)rotation) % 4);
    }

    public static BlockFace RotateFace(BlockFace face, StructureRotation rotation)
    {
        Validate(rotation);
        var offset = face switch
        {
            BlockFace.Right => (1, 0, 0),
            BlockFace.Left => (-1, 0, 0),
            BlockFace.Top => (0, 1, 0),
            BlockFace.Bottom => (0, -1, 0),
            BlockFace.Front => (0, 0, 1),
            BlockFace.Back => (0, 0, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(face))
        };
        var rotated = StructureDefinition.RotateOffset(
            rotation, offset.Item1, offset.Item2, offset.Item3);
        return rotated switch
        {
            (1, 0, 0) => BlockFace.Right,
            (-1, 0, 0) => BlockFace.Left,
            (0, 1, 0) => BlockFace.Top,
            (0, -1, 0) => BlockFace.Bottom,
            (0, 0, 1) => BlockFace.Front,
            (0, 0, -1) => BlockFace.Back,
            _ => throw new InvalidOperationException("Structure rotation produced an invalid face.")
        };
    }

    /// <summary>
    /// World UV conventions: side face U follows the rotating horizontal
    /// tangent so its rotation is unchanged; top and bottom tangents turn
    /// in opposite directions. A local texture must counterrotate accordingly.
    /// </summary>
    public static TextureRotation RotateFaceTexture(
        BlockFace originalFace,
        TextureRotation rotationOnFace,
        StructureRotation structureRotation)
    {
        Validate(structureRotation);
        if (!Enum.IsDefined(originalFace))
            throw new ArgumentOutOfRangeException(nameof(originalFace));
        if (!Enum.IsDefined(rotationOnFace))
            throw new ArgumentOutOfRangeException(nameof(rotationOnFace));
        var delta = originalFace switch
        {
            BlockFace.Top => 4 - (int)structureRotation,
            BlockFace.Bottom => (int)structureRotation,
            _ => 0
        };
        return TextureRotationExtensions.FromQuarterTurn(
            (byte)(((int)rotationOnFace + delta) % 4));
    }

    public static BlockSurfaceState RotateSurface(
        BlockSurfaceState surface, StructureRotation rotation)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Validate(rotation);
        if (rotation == StructureRotation.Degrees0 || surface.Layers.Count == 0)
            return surface;
        var rotated = surface.Layers.Select(layer => new AttachedBlockLayer(
            RotateFace(layer.Face, rotation), layer.LayerId,
            RotateFaceTexture(layer.Face, layer.Rotation, rotation)));
        return new BlockSurfaceState(surface.DyeId, rotated);
    }

    private static void Validate(StructureRotation rotation)
    {
        if (!Enum.IsDefined(rotation))
            throw new ArgumentOutOfRangeException(nameof(rotation));
    }
}
