using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>Shared adjacent-voxel light sampler for attached object sprites.</summary>
internal static class WorldObjectLighting
{
    public static WorldVoxelCoord SamplePosition(WorldVoxelCoord support, BlockFace face) =>
        face switch
        {
            BlockFace.Top => new(support.X, support.Y + 1, support.Z),
            BlockFace.Bottom => new(support.X, support.Y - 1, support.Z),
            BlockFace.Right => new(support.X + 1, support.Y, support.Z),
            BlockFace.Left => new(support.X - 1, support.Y, support.Z),
            BlockFace.Front => new(support.X, support.Y, support.Z + 1),
            BlockFace.Back => new(support.X, support.Y, support.Z - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };

    public static Color ToInstanceColor(VoxelLight light) =>
        new(
            light.Sky / (float)VoxelLight.MaxLevel,
            light.Red / (float)VoxelLight.MaxLevel,
            light.Green / (float)VoxelLight.MaxLevel,
            light.Blue / (float)VoxelLight.MaxLevel);
}
