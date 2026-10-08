namespace Asteria.Core.World;

public readonly record struct ScheduledFluidTick(FluidTickKey Tick, ulong DueTick);

/// <summary>
/// Detached scheduled/topology/dormant fluid work. Restoring is separate
/// from the authoritative queue; pending simulation must not disappear
/// when switching Spheres.
/// </summary>
public sealed class FluidUpdateQueueSnapshot
{
    public const int MaximumEntries = 262144;

    public FluidUpdateQueueSnapshot(
        IEnumerable<WorldVoxelCoord> topology,
        IEnumerable<ScheduledFluidTick> scheduled,
        IEnumerable<FluidTickKey> dormant)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(scheduled);
        ArgumentNullException.ThrowIfNull(dormant);

        var topo = topology.Take(MaximumEntries + 1).ToArray();
        var due = scheduled.Take(MaximumEntries + 1).ToArray();
        var asleep = dormant.Take(MaximumEntries + 1).ToArray();
        if ((long)topo.Length + due.Length + asleep.Length > MaximumEntries)
            throw new InvalidDataException("Saved fluid work exceeds maximum capacity.");

        var topologyKeys = new HashSet<WorldVoxelCoord>();
        foreach (var position in topo)
            if (position.Y < 0 || !topologyKeys.Add(position))
                throw new InvalidDataException("Invalid or repeated fluid topology position.");

        var tickKeys = new HashSet<FluidTickKey>();
        foreach (var entry in due)
            if (entry.Tick.Fluid.IsNone || entry.Tick.Position.Y < 0 ||
                !tickKeys.Add(entry.Tick))
                throw new InvalidDataException("Invalid or repeated scheduled fluid tick.");

        foreach (var tick in asleep)
            if (tick.Fluid.IsNone || tick.Position.Y < 0 ||
                !tickKeys.Add(tick))
                throw new InvalidDataException("Invalid or repeated dormant fluid tick.");

        Topology = Array.AsReadOnly(topo);
        Scheduled = Array.AsReadOnly(due);
        Dormant = Array.AsReadOnly(asleep);
    }

    public IReadOnlyList<WorldVoxelCoord> Topology { get; }
    public IReadOnlyList<ScheduledFluidTick> Scheduled { get; }
    public IReadOnlyList<FluidTickKey> Dormant { get; }
}

/// <summary>Detached FIFO wakeup queue for block gravity/support checks.</summary>
public sealed class BlockPhysicsUpdateQueueSnapshot
{
    public const int MaximumEntries = 262144;

    public BlockPhysicsUpdateQueueSnapshot(IEnumerable<WorldVoxelCoord> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var entries = positions.Take(MaximumEntries + 1).ToArray();
        if (entries.Length > MaximumEntries || entries.Any(position => position.Y < 0) ||
            entries.Distinct().Count() != entries.Length)
            throw new InvalidDataException("Invalid saved block-physics wakeups.");

        Positions = Array.AsReadOnly(entries);
    }

    public IReadOnlyList<WorldVoxelCoord> Positions { get; }
}
