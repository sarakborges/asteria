using System.Numerics;
using System.Runtime.CompilerServices;
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

    // Derived solely from immutable pack definitions. Keys are weak so pack
    // replacement/retirement does not retain a previous registry.
    private static readonly ConditionalWeakTable<
        PackContentRegistry<ObjectDefinition>, TargetEnvelope> Envelopes = new();

    private sealed record TargetEnvelope(double SupportReach)
    {
        public static TargetEnvelope From(PackContentRegistry<ObjectDefinition> registry)
        {
            // Every instance offset component is bounded by eight blocks,
            // scale by eight, and authored jitter by its own definition.
            // This bound covers all support-face and yaw rotations, even when
            // the target extends beyond its supporting chunk.
            const double instanceOffsetReach = 8d * 1.7320508075688772d;
            var reach = 0d;
            foreach (var definition in registry.Definitions)
            {
                var target = definition.Target;
                var jitter = definition.PositionJitter;
                var targetReach = 8d * (
                    Length(target.CenterOffset) +
                    Length(target.Size) * .5d);
                reach = Math.Max(reach,
                    1.5d + instanceOffsetReach +
                    Math.Sqrt((double)jitter.X * jitter.X +
                              (double)jitter.Y * jitter.Y) + targetReach);
            }
            return new TargetEnvelope(reach);
        }

        private static double Length(Vector3 value) =>
            Math.Sqrt((double)value.X * value.X +
                      (double)value.Y * value.Y +
                      (double)value.Z * value.Z);
    }

    // The sparse object-chunk index already belongs to VoxelWorld. Cull
    // each chunk's potential target envelope against the ray segment before
    // visiting its objects; no mutable second spatial owner is needed.
    private static bool MayIntersectChunk(
        ChunkCoord coord, Vector3 origin, Vector3 direction,
        float distance, double padding)
    {
        var near = 0d;
        var far = (double)distance;
        return ClipAxis(origin.X, direction.X,
                   (double)coord.X * Chunk.Size - padding,
                   ((double)coord.X + 1d) * Chunk.Size + padding,
                   ref near, ref far) &&
               ClipAxis(origin.Y, direction.Y,
                   (double)coord.Y * Chunk.Size - padding,
                   ((double)coord.Y + 1d) * Chunk.Size + padding,
                   ref near, ref far) &&
               ClipAxis(origin.Z, direction.Z,
                   (double)coord.Z * Chunk.Size - padding,
                   ((double)coord.Z + 1d) * Chunk.Size + padding,
                   ref near, ref far);
    }

    private static bool ClipAxis(double origin, double direction,
        double min, double max, ref double near, ref double far)
    {
        if (direction == 0d) return origin >= min && origin <= max;
        var first = (min - origin) / direction;
        var second = (max - origin) / direction;
        if (first > second) (first, second) = (second, first);
        near = Math.Max(near, first);
        far = Math.Min(far, second);
        return near <= far;
    }

    // A tighter instance-specific bound avoids building poses and testing
    // oriented hitboxes for distant objects inside a candidate chunk. An L1
    // extent is deliberately conservative for any face/yaw rotation.
    private static bool MayIntersectObject(
        WorldVoxelCoord support, ObjectCell cell, ObjectDefinition definition,
        Vector3 origin, Vector3 direction, float distance)
    {
        var offset = cell.Transform.Offset;
        var scale = cell.Transform.Scale;
        var center = definition.Target.CenterOffset;
        var size = definition.Target.Size;
        var maxScale = Math.Max(scale.X, Math.Max(scale.Y, scale.Z));
        var reach = 1.5d +
            Math.Abs((double)offset.X) +
            Math.Abs((double)offset.Y) +
            Math.Abs((double)offset.Z) +
            definition.PositionJitter.X + definition.PositionJitter.Y +
            maxScale * (Math.Abs((double)center.X) +
                        Math.Abs((double)center.Y) +
                        Math.Abs((double)center.Z) +
                        .5d * (size.X + size.Y + size.Z));

        var x = (double)support.X + .5d - origin.X;
        var y = (double)support.Y + .5d - origin.Y;
        var z = (double)support.Z + .5d - origin.Z;
        var along = Math.Clamp(x * direction.X + y * direction.Y +
                               z * direction.Z, 0d, distance);
        x -= along * direction.X;
        y -= along * direction.Y;
        z -= along * direction.Z;
        return x * x + y * y + z * z <= reach * reach;
    }


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
        var envelope = Envelopes.GetValue(objects, static registry =>
            TargetEnvelope.From(registry));
        WorldObjectHit? nearest = null;
        // A maintained resident-object-chunk index is complete even for
        // objects with authored large offsets/scales. A fixed ray envelope
        // can silently miss their hitboxes; scanning all voxel cells would
        // be needlessly expensive.
        foreach (var coord in world.ResidentObjectChunkCoords)
        {
            if (!MayIntersectChunk(coord, origin, normalized, maxDistance,
                    envelope.SupportReach))
                continue;
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
                if (!MayIntersectObject(support, cell, definition,
                        origin, normalized, maxDistance))
                    return;
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
