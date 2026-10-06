using System.Numerics;

namespace Asteria.Core.World;

public readonly record struct FluidBodyContact(
    bool IsImmersed,
    bool EyeSubmerged,
    FluidRuntimeId Fluid,
    float SurfaceY,
    float SampleY)
{
    public float Depth =>
        IsImmersed
            ? MathF.Max(
                0f,
                SurfaceY -
                SampleY)
            : 0f;

    public bool IsNearSurface(
        float margin) =>
        IsImmersed &&
        margin >= 0f &&
        Depth <= margin;
}

public static class FluidBodyQuery
{
    public static FluidBodyContact Sample(
        VoxelWorld world,
        WorldAabb body,
        float eyeY)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!float.IsFinite(eyeY))
        {
            throw new ArgumentOutOfRangeException(
                nameof(eyeY));
        }

        var centerX =
            (body.Minimum.X +
             body.Maximum.X) *
            0.5f;
        var centerZ =
            (body.Minimum.Z +
             body.Maximum.Z) *
            0.5f;
        var sampleY =
            (body.Minimum.Y +
             body.Maximum.Y) *
            0.5f;
        var voxel =
            new WorldVoxelCoord(
                (int)MathF.Floor(centerX),
                (int)MathF.Floor(sampleY),
                (int)MathF.Floor(centerZ));
        var bodyFluid =
            world.GetFluidOrEmpty(voxel);
        var bodySurfaceY =
            bodyFluid.IsEmpty
                ? 0f
                : voxel.Y +
                  bodyFluid.Height;
        var immersed =
            !bodyFluid.IsEmpty &&
            sampleY <
            bodySurfaceY;

        var eyeVoxel =
            new WorldVoxelCoord(
                voxel.X,
                (int)MathF.Floor(eyeY),
                voxel.Z);
        var eyeFluid =
            world.GetFluidOrEmpty(
                eyeVoxel);
        var eyeSurfaceY =
            eyeFluid.IsEmpty
                ? 0f
                : eyeVoxel.Y +
                  eyeFluid.Height;
        var eyeSubmerged =
            !eyeFluid.IsEmpty &&
            eyeY <
            eyeSurfaceY;
        var contactFluid =
            eyeSubmerged
                ? eyeFluid.Fluid
                : bodyFluid.Fluid;

        if (!immersed &&
            !eyeSubmerged)
        {
            return default;
        }

        return new FluidBodyContact(
            immersed,
            eyeSubmerged,
            contactFluid,
            immersed
                ? bodySurfaceY
                : eyeSurfaceY,
            sampleY);
    }
}
