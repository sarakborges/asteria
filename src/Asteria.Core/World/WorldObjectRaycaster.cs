using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

public readonly record struct WorldObjectHit(
    WorldVoxelCoord Support, ObjectCell Object, float Distance);

/// <summary>
/// Queries only the resident chunks that contain Objects, not all chunks
/// or voxel positions. Uses the canonical render pose for precise hitboxes.
/// </summary>
public static class WorldObjectRaycaster
{
    private const float MaximumReach = 32f;

    public static WorldObjectHit? Raycast(
        VoxelWorld world, PackContentRegistry<ObjectDefinition> objects,
        Vector3 origin, Vector3 direction, float maxDistance)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(objects);
        if (!Finite(origin) || !Finite(direction) ||
            !float.IsFinite(maxDistance) || maxDistance < 0f ||
            maxDistance > MaximumReach || direction.LengthSquared() <= float.Epsilon)
            return null;

        var normalized = Vector3.Normalize(direction);
        WorldObjectHit? nearest = null;
        // A maintained resident-object-chunk index is complete even for
        // objects with authored large offsets/scales. A fixed ray envelope
        // can silently miss their hitboxes; scanning all voxel cells would
        // be needlessly expensive.
        foreach (var coord in world.ResidentObjectChunkCoords)
        {
            if (!world.TryGetChunk(coord, out var chunk) || chunk.ObjectCount == 0)
                continue;
            chunk.VisitObjectCells((localX, localY, localZ, cell) =>
            {
                if (!objects.TryGet(cell.Id, out var definition) ||
                    definition is null || !definition.Supports(cell.Face))
                    return;
                var (worldX, worldY, worldZ) = VoxelCoordinates.ToWorld(coord,
                    new LocalVoxelCoord(localX, localY, localZ));
                var support = new WorldVoxelCoord(worldX, worldY, worldZ);
                var pose = WorldObjectGeometry.Pose(support, cell, definition);
                if (!WorldObjectGeometry.TryHitTarget(pose, definition.Target,
                    origin, normalized, maxDistance, out var distance))
                    return;
                var result = new WorldObjectHit(support, cell, distance);
                if (nearest is null || distance < nearest.Value.Distance - 0.00001f ||
                    (MathF.Abs(distance - nearest.Value.Distance) <= 0.00001f &&
                     Order(result, nearest.Value) < 0))
                    nearest = result;
            });
        }
        return nearest;
    }

    private static int Order(WorldObjectHit a, WorldObjectHit b)
    {
        var result = a.Support.Y.CompareTo(b.Support.Y);
        if (result == 0) result = a.Support.Z.CompareTo(b.Support.Z);
        if (result == 0) result = a.Support.X.CompareTo(b.Support.X);
        if (result == 0) result = string.CompareOrdinal(a.Object.Id, b.Object.Id);
        if (result == 0) result = a.Object.Face.CompareTo(b.Object.Face);
        return result == 0 ? a.Object.Rotation.CompareTo(b.Object.Rotation) : result;
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
