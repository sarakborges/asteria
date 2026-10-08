using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Validates a prepared generated warp destination against authoritative
/// resident voxels, including edits made since initial world generation.
/// The query never materializes missing chunks or alters residency.
/// </summary>
public static class ResidentWarpDestinationQuery
{
    private const int HorizontalRadius = 6;
    private const int VerticalRadius = 4;

    public static Vector3? Find(VoxelWorld world, Vector3 preferred)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!float.IsFinite(preferred.X) ||
            !float.IsFinite(preferred.Y) ||
            !float.IsFinite(preferred.Z) ||
            preferred.Y < 1f ||
            preferred.X < int.MinValue + HorizontalRadius ||
            preferred.X > int.MaxValue - HorizontalRadius ||
            preferred.Z < int.MinValue + HorizontalRadius ||
            preferred.Z > int.MaxValue - HorizontalRadius ||
            preferred.Y > int.MaxValue - VerticalRadius - 2)
            return null;

        var x = (int)MathF.Floor(preferred.X);
        var y = (int)MathF.Floor(preferred.Y);
        var z = (int)MathF.Floor(preferred.Z);
        // Deterministic nearest-first traversal with stable X/Z/Y tie-break.
        var candidates = new List<(int X, int Y, int Z, int Dist)>();
        for (var dz = -HorizontalRadius; dz <= HorizontalRadius; dz++)
        for (var dx = -HorizontalRadius; dx <= HorizontalRadius; dx++)
        for (var dy = -VerticalRadius; dy <= VerticalRadius; dy++)
        {
            var targetY = y + dy;
            if (targetY < 1) continue;
            candidates.Add((x + dx, targetY, z + dz,
                dx * dx + dz * dz + dy * dy));
        }
        foreach (var candidate in candidates
                     .OrderBy(value => value.Dist)
                     .ThenBy(value => value.X)
                     .ThenBy(value => value.Z)
                     .ThenBy(value => value.Y))
        {
            var feet = new WorldVoxelCoord(candidate.X, candidate.Y, candidate.Z);
            var head = new WorldVoxelCoord(candidate.X, candidate.Y + 1, candidate.Z);
            var below = new WorldVoxelCoord(candidate.X, candidate.Y - 1, candidate.Z);
            if (!world.IsLoadedAt(feet) ||
                !world.IsLoadedAt(head) ||
                !world.IsLoadedAt(below) ||
                !world.GetCellOrEmpty(feet).IsEmpty ||
                !world.GetCellOrEmpty(head).IsEmpty ||
                world.GetCellOrEmpty(below).IsEmpty ||
                !world.GetFluidOrEmpty(feet).IsEmpty ||
                !world.GetFluidOrEmpty(head).IsEmpty)
                continue;
            return new Vector3(candidate.X + 0.5f,
                candidate.Y, candidate.Z + 0.5f);
        }
        return null;
    }
}
