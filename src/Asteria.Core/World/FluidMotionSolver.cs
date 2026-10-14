namespace Asteria.Core.World;

public static class FluidMotionSolver
{
    public static float VerticalSpeed(
        float currentSpeed,
        float deltaSeconds,
        FluidBodyContact contact,
        FluidMotionDefinition motion,
        bool ascendRequested,
        bool descendRequested)
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

        // Jump wins over Descend. Near-surface exit is a target speed
        // approached at the same authored acceleration as other swimming.
        var target =
            ascendRequested
                ? contact.IsNearSurface(motion.SurfaceExitMargin)
                    ? motion.SurfaceExitSpeed
                    : motion.AscendSpeed
                : descendRequested
                    ? -motion.SinkSpeed
                    : motion.BuoyancySpeed;
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
