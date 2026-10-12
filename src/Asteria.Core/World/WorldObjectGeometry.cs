using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Shared engine-independent object pose used by GPU presentation and precise
/// targeting. Supports independent face attachment, rotation, scale and
/// bounded stable world-space jitter (no runtime random state).
/// </summary>
public readonly record struct WorldObjectPose(
    Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public Vector3 ToWorld(Vector3 local) =>
        Position + Vector3.Transform(local * Scale, Rotation);
}

public static class WorldObjectGeometry
{
    public static WorldObjectPose Pose(
        WorldVoxelCoord support, ObjectCell cell, ObjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (cell.Id != definition.Id || !definition.Supports(cell.Face))
            throw new ArgumentException("Object identity or support face mismatch.", nameof(cell));

        var faceRotation = cell.Face switch
        {
            BlockFace.Top => Quaternion.Identity,
            BlockFace.Bottom => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI),
            BlockFace.Right => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f),
            BlockFace.Left => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f),
            BlockFace.Front => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f),
            BlockFace.Back => Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f),
            _ => throw new ArgumentOutOfRangeException(nameof(cell)),
        };
        var rotation = Quaternion.Normalize(faceRotation *
            Quaternion.CreateFromAxisAngle(Vector3.UnitY,
                (int)cell.Rotation * MathF.PI / 2f));
        var normal = cell.Face switch
        {
            BlockFace.Top => Vector3.UnitY,
            BlockFace.Bottom => -Vector3.UnitY,
            BlockFace.Right => Vector3.UnitX,
            BlockFace.Left => -Vector3.UnitX,
            BlockFace.Front => Vector3.UnitZ,
            _ => -Vector3.UnitZ,
        };
        var basePosition = new Vector3(
            support.X + .5f, support.Y + .5f, support.Z + .5f) + normal * .5f;

        // Same hash for rendering and targeting; no per-frame randomness.
        var jitter = StableJitter(support, cell.Id, definition.PositionJitter);
        var localOffset = cell.Transform.Offset + new Vector3(jitter.X, 0, jitter.Y);
        return new WorldObjectPose(
            basePosition + Vector3.Transform(localOffset, rotation),
            rotation, cell.Transform.Scale);
    }

    private static Vector2 StableJitter(
        WorldVoxelCoord support, string id, Vector2 limits)
    {
        if (limits == Vector2.Zero) return Vector2.Zero;
        var hash = 2166136261u;
        foreach (var character in id)
        {
            hash ^= character;
            hash = unchecked(hash * 16777619u);
        }
        hash = Step(hash, unchecked((uint)support.X));
        hash = Step(hash, unchecked((uint)support.Y));
        hash = Step(hash, unchecked((uint)support.Z));
        var x = (hash & 65535u) / 65535f * 2f - 1f;
        var z = ((hash >> 16) & 65535u) / 65535f * 2f - 1f;
        return new Vector2(x * limits.X, z * limits.Y);
    }

    private static uint Step(uint seed, uint value) =>
        unchecked((seed ^ value) * 16777619u);

    public static WorldAabb TargetAabb(
        WorldObjectPose pose, ObjectTargetDefinition target)
    {
        var center = target.CenterOffset;
        var half = target.Size * .5f;
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < 8; i++)
        {
            var offset = new Vector3(
                (i & 1) == 0 ? -half.X : half.X,
                (i & 2) == 0 ? -half.Y : half.Y,
                (i & 4) == 0 ? -half.Z : half.Z);
            var vertex = pose.ToWorld(center + offset);
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }
        return new WorldAabb(min, max);
    }

    public static bool TryHitTarget(
        WorldObjectPose pose, ObjectTargetDefinition target,
        Vector3 rayOrigin, Vector3 rayDirection, float maxDistance,
        out float distance)
    {
        distance = 0f;
        if (maxDistance < 0f || rayDirection.LengthSquared() <= float.Epsilon)
            return false;
        var inverse = Quaternion.Inverse(pose.Rotation);
        var localOrigin = Vector3.Transform(rayOrigin - pose.Position, inverse) / pose.Scale;
        var localDirection = Vector3.Transform(Vector3.Normalize(rayDirection), inverse) / pose.Scale;
        var half = target.Size * .5f;
        var min = target.CenterOffset - half;
        var max = target.CenterOffset + half;
        var near = 0f;
        var far = maxDistance;
        if (!Clip(localOrigin.X, localDirection.X, min.X, max.X, ref near, ref far) ||
            !Clip(localOrigin.Y, localDirection.Y, min.Y, max.Y, ref near, ref far) ||
            !Clip(localOrigin.Z, localDirection.Z, min.Z, max.Z, ref near, ref far))
            return false;
        distance = near;
        return near <= maxDistance;
    }

    private static bool Clip(float origin, float direction,
        float min, float max, ref float near, ref float far)
    {
        if (MathF.Abs(direction) < 0.0000001f)
            return origin >= min && origin <= max;
        var first = (min - origin) / direction;
        var second = (max - origin) / direction;
        if (first > second) (first, second) = (second, first);
        near = MathF.Max(near, first);
        far = MathF.Min(far, second);
        return near <= far;
    }
}
