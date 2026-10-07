namespace Asteria.Core.World;

public static class FluidMotionSolver
{
    public static float VerticalSpeed(
        float currentSpeed,
        float deltaSeconds,
        FluidBodyContact contact,
        FluidMotionDefinition motion,
        bool ascendRequested)
    {
        if (!float.IsFinite(currentSpeed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentSpeed));
        }

        if (!float.IsFinite(deltaSeconds) ||
            deltaSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds));
        }

        if (!contact.IsImmersed)
        {
            return currentSpeed;
        }

        if (ascendRequested &&
            contact.IsNearSurface(
                motion.SurfaceExitMargin))
        {
            return MathF.Max(
                currentSpeed,
                motion.SurfaceExitSpeed);
        }

        var target =
            ascendRequested
                ? motion.AscendSpeed
                : -motion.SinkSpeed;
        var maximumDelta =
            motion.VerticalAcceleration *
            deltaSeconds;
        var difference =
            target -
            currentSpeed;

        if (MathF.Abs(difference) <=
            maximumDelta)
        {
            return target;
        }

        return currentSpeed +
               MathF.Sign(difference) *
               maximumDelta;
    }
}
