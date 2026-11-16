namespace Asteria.Core.World;

/// <summary>Shared voxel hit-normal conversion for tool and layer interactions.</summary>
public static class VoxelHitFace
{
    public static bool TryResolve(VoxelWorldHit hit, out BlockFace face)
    {
        face = (hit.NormalX, hit.NormalY, hit.NormalZ) switch
        {
            (1, 0, 0) => BlockFace.Right,
            (-1, 0, 0) => BlockFace.Left,
            (0, 1, 0) => BlockFace.Top,
            (0, -1, 0) => BlockFace.Bottom,
            (0, 0, 1) => BlockFace.Front,
            (0, 0, -1) => BlockFace.Back,
            _ => (BlockFace)byte.MaxValue,
        };
        return Enum.IsDefined(face);
    }
}
