using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

/// <summary>
/// An Object's stable HUD identity and displayed illumination. Target
/// raycast distance changes as the player moves, but that distance is not
/// visible and must not cause a new WebUI publication every frame.
/// </summary>
public readonly record struct TargetObjectHudSnapshot(
    WorldVoxelCoord Support,
    ObjectCell Object,
    byte SkyLight,
    byte BlockLight)
{
    public static TargetObjectHudSnapshot FromWorld(
        VoxelWorld world, WorldObjectHit hit)
    {
        ArgumentNullException.ThrowIfNull(world);
        var light = world.GetLightOrDark(hit.Support + (0, 1, 0));
        return new TargetObjectHudSnapshot(
            hit.Support, hit.Object, light.Sky, light.BlockPeak);
    }
}
