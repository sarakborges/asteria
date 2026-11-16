namespace Asteria.Core.World;

public sealed class WorldTickClock
{
    private double _accumulatedTicks;

    public WorldTickClock(
        ulong initialTick = 0)
    {
        CurrentTick =
            initialTick;
    }

    public ulong CurrentTick { get; private set; }

    public uint TicksThisFrame { get; private set; }

    public uint Advance(
        double deltaSeconds,
        uint ticksPerSecond)
    {
        if (!double.IsFinite(deltaSeconds) ||
            deltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds));
        }

        if (ticksPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticksPerSecond));
        }

        TicksThisFrame = 0;
        _accumulatedTicks +=
            deltaSeconds * ticksPerSecond;

        var elapsed =
            (uint)Math.Min(
                Math.Floor(_accumulatedTicks),
                uint.MaxValue);

        if (elapsed == 0)
        {
            return 0;
        }

        _accumulatedTicks -= elapsed;
        CurrentTick =
            ulong.MaxValue - CurrentTick < elapsed
                ? ulong.MaxValue
                : CurrentTick + elapsed;
        TicksThisFrame = elapsed;
        return elapsed;
    }
}
