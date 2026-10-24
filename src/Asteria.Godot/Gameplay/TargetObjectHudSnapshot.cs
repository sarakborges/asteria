using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

/// <summary>
/// An Object's stable HUD identity. Target raycast distance changes as the
/// player moves, but that distance is not visible and must not cause a new
/// WebUI publication every frame.
/// </summary>
public readonly record struct TargetObjectHudSnapshot(
    WorldVoxelCoord Support,
    ObjectCell Object)
{
    public static TargetObjectHudSnapshot FromWorld(
        VoxelWorld world, WorldObjectHit hit)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new TargetObjectHudSnapshot(hit.Support, hit.Object);
    }
}
