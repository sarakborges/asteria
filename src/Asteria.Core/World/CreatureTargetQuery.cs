using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

public readonly record struct CreatureTargetHit(
    CreatureInstanceState Creature,
    float Distance);

/// <summary>
/// Nearest-ray creature selection using authored target colliders, with
/// stable instance-id tie breaks and voxel line-of-sight occlusion.
/// </summary>
public static class CreatureTargetQuery
{
    public static CreatureTargetHit? Find(
        IEnumerable<CreatureInstanceState> creatures,
        PackContentRegistry<CreatureDefinition> definitions,
        VoxelWorld world,
        BlockRegistry blocks,
        Vector3 origin,
        Vector3 direction,
        float maxDistance)
    {
        ArgumentNullException.ThrowIfNull(creatures);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);

        if (!Finite(origin) || !Finite(direction) ||
            !float.IsFinite(maxDistance) || maxDistance <= 0 ||
            direction.LengthSquared() <= float.Epsilon)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var unit = Vector3.Normalize(direction);
        CreatureTargetHit? nearest = null;
        foreach (var creature in creatures)
        {
            var definition = definitions.Get(creature.DefinitionId);
            var collider = definition.TargetCollider ?? definition.Collider;
            var center = creature.Position + new Vector3(
                collider.CenterOffset.X, collider.CenterOffset.Y,
                collider.CenterOffset.Z);
            var half = new Vector3(
                collider.Size.X, collider.Size.Y, collider.Size.Z) * 0.5f;

            if (!IntersectAabb(origin, unit, center - half,
                    center + half, maxDistance, out var distance))
                continue;

            if (nearest is not { } current ||
                distance < current.Distance ||
                (distance == current.Distance &&
                 creature.Id.Value < current.Creature.Id.Value))
            {
                nearest = new CreatureTargetHit(creature, distance);
            }
        }

        if (nearest is not { } hit) return null;

        var point = origin + unit * hit.Distance;
        var voxel = new WorldVoxelCoord(
            (int)MathF.Floor(point.X),
            (int)MathF.Floor(point.Y),
            (int)MathF.Floor(point.Z));
        if (voxel.Y < 0 || !world.IsLoadedAt(voxel)) return null;

        // Use the same detailed microblock-aware raycast as mining.
        // A block hit up to the candidate's entry surface wins over a creature.
        return VoxelWorldRaycaster.Raycast(
            world, blocks, origin, unit, hit.Distance) is null
            ? hit : null;
    }

    private static bool IntersectAabb(
        Vector3 origin,
        Vector3 direction,
        Vector3 min,
        Vector3 max,
        float maximum,
        out float distance)
    {
        var near = 0f;
        var far = maximum;
        for (var axis = 0; axis < 3; axis++)
        {
            var start = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            var ray = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            var low = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            var high = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;

            if (MathF.Abs(ray) < 0.000001f)
            {
                if (start < low || start > high)
                {
                    distance = 0f;
                    return false;
                }
                continue;
            }

            var one = (low - start) / ray;
            var two = (high - start) / ray;
            if (one > two) (one, two) = (two, one);
            near = MathF.Max(near, one);
            far = MathF.Min(far, two);
            if (near > far)
            {
                distance = 0f;
                return false;
            }
        }

        distance = near;
        return true;
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
