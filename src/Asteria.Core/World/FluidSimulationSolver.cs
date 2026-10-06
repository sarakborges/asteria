namespace Asteria.Core.World;

public readonly record struct FluidCellChange(
    WorldVoxelCoord Position,
    FluidCell Fluid);

public sealed record FluidSimulationResult(
    IReadOnlyList<FluidCellChange> Changes,
    IReadOnlyList<WorldVoxelCoord> RemainingPositions,
    int ProcessedVoxelCount);

public static class FluidSimulationSolver
{
    private static readonly (int X, int Y, int Z)[] Horizontal =
    [
        (1, 0, 0),
        (-1, 0, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    private static readonly (int X, int Y, int Z)[] ChangedNeighborhood =
    [
        (0, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (1, 0, 0),
        (-1, 0, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    public static FluidSimulationResult Process(
        VoxelWorld world,
        FluidRegistry fluids,
        IEnumerable<WorldVoxelCoord> seeds,
        int maximumProcessedVoxels = 512)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(seeds);

        if (maximumProcessedVoxels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumProcessedVoxels));
        }

        var queue = new Queue<WorldVoxelCoord>();
        var queued = new HashSet<WorldVoxelCoord>();
        var changed = new HashSet<WorldVoxelCoord>();

        void Enqueue(WorldVoxelCoord position)
        {
            if (position.Y < 0 ||
                !world.IsLoadedAt(position) ||
                !queued.Add(position))
            {
                return;
            }

            queue.Enqueue(position);
        }

        foreach (var seed in seeds)
        {
            Enqueue(seed);
        }

        var processed = 0;

        while (queue.Count > 0 &&
               processed < maximumProcessedVoxels)
        {
            var position = queue.Dequeue();
            queued.Remove(position);
            processed++;

            var current =
                world.GetFluidOrEmpty(position);
            var desired =
                DesiredFluid(
                    world,
                    fluids,
                    position,
                    current);

            if (current == desired)
            {
                continue;
            }

            if (!world.SetFluidAt(
                    position,
                    desired,
                    out _))
            {
                continue;
            }

            changed.Add(position);

            foreach (var offset in ChangedNeighborhood)
            {
                Enqueue(position + offset);
            }
        }

        var changes = changed
            .OrderBy(position => position.Y)
            .ThenBy(position => position.Z)
            .ThenBy(position => position.X)
            .Select(position =>
                new FluidCellChange(
                    position,
                    world.GetFluidOrEmpty(position)))
            .ToArray();

        return new FluidSimulationResult(
            changes,
            queue.ToArray(),
            processed);
    }

    public static FluidCell DesiredFluid(
        VoxelWorld world,
        FluidRegistry fluids,
        WorldVoxelCoord position,
        FluidCell current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(fluids);

        if (!world.IsLoadedAt(position) ||
            !world.GetCellOrEmpty(position).IsEmpty)
        {
            return FluidCell.Empty;
        }

        if (current.IsSource)
        {
            return current;
        }

        var above =
            world.GetFluidOrEmpty(
                position + (0, 1, 0));

        if (!above.IsEmpty)
        {
            return FluidCell.Spreading(
                above.Fluid,
                FluidCell.MaxLevel,
                spreadDistance: 0);
        }

        FluidCell best = default;

        foreach (var offset in Horizontal)
        {
            var origin = position + offset;
            var neighbor =
                world.GetFluidOrEmpty(origin);

            if (neighbor.IsEmpty ||
                !CanSpreadHorizontallyFrom(
                    world,
                    origin,
                    neighbor))
            {
                continue;
            }

            var definition =
                fluids.GetDefinition(
                    neighbor.Fluid);
            var candidate =
                HorizontalSpread(
                    neighbor,
                    definition.MaxSpread);

            if (candidate.IsEmpty)
            {
                continue;
            }

            if (best.IsEmpty ||
                candidate.Level > best.Level ||
                (candidate.Level == best.Level &&
                 candidate.SpreadDistance <
                 best.SpreadDistance) ||
                (candidate.Level == best.Level &&
                 candidate.SpreadDistance ==
                 best.SpreadDistance &&
                 candidate.Fluid.Value <
                 best.Fluid.Value))
            {
                best = candidate;
            }
        }

        return best;
    }

    public static bool CanSpreadHorizontallyFrom(
        VoxelWorld world,
        WorldVoxelCoord position,
        FluidCell fluid)
    {
        if (position.Y == 0 ||
            !world.GetCellOrEmpty(
                position + (0, -1, 0)).IsEmpty)
        {
            return true;
        }

        // Dynamic falling columns stay vertical until they land. A source at
        // an exposed edge can still spill sideways into a waterfall.
        return fluid.IsSource &&
               world.GetFluidOrEmpty(
                   position + (0, 1, 0)).IsEmpty;
    }

    public static FluidCell HorizontalSpread(
        FluidCell neighbor,
        ushort maxSpread)
    {
        if (neighbor.IsEmpty ||
            maxSpread == 0)
        {
            return FluidCell.Empty;
        }

        var distance =
            checked((ushort)(
                neighbor.SpreadDistance + 1));

        if (distance > maxSpread)
        {
            return FluidCell.Empty;
        }

        var remaining =
            (uint)(maxSpread - distance);
        var range =
            FluidCell.MaxLevel -
            FluidCell.MinLevel;
        var offset =
            (remaining * range +
             maxSpread / 2u) /
            maxSpread;
        var level =
            checked((byte)(
                FluidCell.MinLevel +
                offset));

        return FluidCell.Spreading(
            neighbor.Fluid,
            level,
            distance);
    }
}
