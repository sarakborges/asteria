namespace Asteria.Core.World;

public static class FluidTiming
{
    public static ulong? DelayTicks(
        float spreadSpeed,
        uint ticksPerSecond)
    {
        if (!float.IsFinite(spreadSpeed) ||
            spreadSpeed <= float.Epsilon ||
            ticksPerSecond == 0)
        {
            return null;
        }

        var ticks =
            Math.Max(
                1d,
                Math.Round(
                    ticksPerSecond /
                    (double)spreadSpeed,
                    MidpointRounding.AwayFromZero));

        return checked((ulong)ticks);
    }

    public static ulong? DelayTicks(
        FluidRegistry fluids,
        FluidRuntimeId fluid,
        uint ticksPerSecond)
    {
        ArgumentNullException.ThrowIfNull(fluids);

        return DelayTicks(
            fluids.GetDefinition(fluid).SpreadSpeed,
            ticksPerSecond);
    }
}
