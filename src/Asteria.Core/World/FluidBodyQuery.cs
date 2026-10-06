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
        var fluid =
            world.GetFluidOrEmpty(voxel);

        if (fluid.IsEmpty)
        {
            return default;
        }

        var surfaceY =
            voxel.Y +
            fluid.Height;
        var immersed =
            sampleY <
            surfaceY;

        return new FluidBodyContact(
            immersed,
            eyeY < surfaceY,
            fluid.Fluid,
            surfaceY,
            sampleY);
    }
}
