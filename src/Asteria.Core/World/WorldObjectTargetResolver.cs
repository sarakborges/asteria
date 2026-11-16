using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Shared block/Object target arbitration. An Object must be hit before any
/// collidable block geometry or a source fluid on the same ray. Its
/// backing support voxel may remain behind the Object, but must not hide
/// the Object itself.
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

        // Source fluids must also occlude an Object behind them. The
        // epsilon permits touching an Object on its own support surface,
        // even when the backing voxel or water begins at that plane.
        var blockingHit = VoxelWorldRaycaster.Raycast(
            world, blocks, origin, direction,
            MathF.Max(0f, resolved.Distance - .001f),
            includeFluidSources: true);
        return blockingHit is null ? resolved : null;
    }
}
