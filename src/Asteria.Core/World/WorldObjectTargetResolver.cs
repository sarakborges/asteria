using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Shared block/Object target arbitration. An Object must be hit before any
/// collidable block geometry on the same ray; its backing support voxel may
/// remain a valid target behind the Object but never occlude the Object itself.
/// </summary>
public static class WorldObjectTargetResolver
{
    public static WorldObjectHit? RaycastVisible(
        VoxelWorld world, BlockRegistry blocks,
        PackContentRegistry<ObjectDefinition> objects,
        Vector3 origin, Vector3 direction, float maxDistance)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(objects);

        var objectHit = WorldObjectRaycaster.Raycast(
            world, objects, origin, direction, maxDistance);
        if (objectHit is not { } resolved) return null;

        // Do not pick Objects through geometry in the foreground. The
        // epsilon permits a touch against the Object's own support surface,
        // which lies at the same mathematical plane for thin Object visuals.
        var blockingHit = VoxelWorldRaycaster.Raycast(
            world, blocks, origin, direction,
            MathF.Max(0f, resolved.Distance - .001f));
        return blockingHit is null ? resolved : null;
    }
}
