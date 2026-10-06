namespace Asteria.Core.World;

public readonly record struct FluidCellChange(
    WorldVoxelCoord Position,
    FluidCell Previous,
    FluidCell Current);

public readonly record struct FluidScheduleRequest(
    FluidRuntimeId Fluid,
    WorldVoxelCoord Position,
    bool Neighborhood);

public sealed record FluidSimulationResult(
    IReadOnlyList<FluidCellChange> Changes,
    IReadOnlyList<FluidScheduleRequest> ScheduleRequests,
    IReadOnlyList<FluidTickKey> DormantTicks,
    int ProcessedVoxelCount,
    int DownhillSearchCount,
    int DownhillVisitedNodeCount);

public static class FluidSimulationSolver
{
    private static readonly (int X, int Y, int Z)[] Horizontal =
    [
        (1, 0, 0),
        (-1, 0, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    public static FluidSimulationResult Process(
        VoxelWorld world,
        FluidRegistry fluids,
        FluidWorkBatch batch)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(batch);

        var changes =
            new List<FluidCellChange>();
        var requests =
            new HashSet<FluidScheduleRequest>();
        var dormant =
            new List<FluidTickKey>();
        var scratch =
            new SolverScratch();
        var processed = 0;

        foreach (var position in
                 batch.TopologyPositions)
        {
            processed++;

            if (!world.IsLoadedAt(position))
            {
                continue;
            }

            var current =
                world.GetFluidOrEmpty(position);
            var desired =
                DesiredFluid(
                    world,
                    fluids,
                    position,
                    current,
                    scratch);

            if (current == desired)
            {
                continue;
            }

            var transition =
                TransitionFluid(
                    current,
                    desired);

            if (!transition.IsNone)
            {
                requests.Add(
                    new FluidScheduleRequest(
                        transition,
                        position,
                        Neighborhood: false));
            }
        }

        foreach (var scheduled in
                 batch.DueTicks)
        {
            processed++;
            var position =
                scheduled.Position;

            if (!world.IsLoadedAt(position))
            {
                dormant.Add(scheduled);
                continue;
            }

            var current =
                world.GetFluidOrEmpty(position);
            var desired =
                DesiredFluid(
                    world,
                    fluids,
                    position,
                    current,
                    scratch);

            if (current == desired)
            {
                continue;
            }

            var transition =
                TransitionFluid(
                    current,
                    desired);

            if (transition.IsNone)
            {
                continue;
            }

            if (transition != scheduled.Fluid)
            {
                requests.Add(
                    new FluidScheduleRequest(
                        transition,
                        position,
                        Neighborhood: false));
                continue;
            }

            if (!world.SetFluidAt(
                    position,
                    desired,
                    out _))
            {
                continue;
            }

            changes.Add(
                new FluidCellChange(
                    position,
                    current,
                    desired));

            if (!current.IsEmpty)
            {
                requests.Add(
                    new FluidScheduleRequest(
                        current.Fluid,
                        position,
                        Neighborhood: true));
            }

            if (!desired.IsEmpty &&
                desired.Fluid != current.Fluid)
            {
                requests.Add(
                    new FluidScheduleRequest(
                        desired.Fluid,
                        position,
                        Neighborhood: true));
            }
            else if (!desired.IsEmpty)
            {
                requests.Add(
                    new FluidScheduleRequest(
                        desired.Fluid,
                        position,
                        Neighborhood: true));
            }
        }

        return new FluidSimulationResult(
            changes,
            requests
                .OrderBy(request => request.Fluid.Value)
                .ThenBy(request => request.Position.Y)
                .ThenBy(request => request.Position.Z)
                .ThenBy(request => request.Position.X)
                .ThenBy(request => request.Neighborhood)
                .ToArray(),
            dormant,
            processed,
            scratch.DownhillSearchCount,
            scratch.DownhillVisitedNodeCount);
    }

    public static FluidCell DesiredFluid(
        VoxelWorld world,
        FluidRegistry fluids,
        WorldVoxelCoord position,
        FluidCell current) =>
        DesiredFluid(
            world,
            fluids,
            position,
            current,
            new SolverScratch());

    public static FluidCell DesiredFluid(
        VoxelWorld world,
        FluidRegistry fluids,
        WorldVoxelCoord position,
        FluidCell current,
        SolverScratch scratch)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(scratch);

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

        Span<HorizontalCandidate> candidates =
            stackalloc HorizontalCandidate[4];
        var candidateCount = 0;

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

            candidates[candidateCount++] =
                new HorizontalCandidate(
                    origin,
                    candidate,
                    definition.MaxSpread
                        .SaturatingSubtract(
                            neighbor.SpreadDistance));
        }

        SortHorizontalCandidates(
            candidates[..candidateCount]);

        for (var index = 0;
             index < candidateCount;
             index++)
        {
            var candidate =
                candidates[index];

            if (HorizontalSpreadIsPreferred(
                    world,
                    candidate.Origin,
                    position,
                    candidate.Fluid.Fluid,
                    candidate.RemainingSteps,
                    scratch))
            {
                return candidate.Fluid;
            }
        }

        return FluidCell.Empty;
    }

    private static void SortHorizontalCandidates(
        Span<HorizontalCandidate> candidates)
    {
        for (var index = 1;
             index < candidates.Length;
             index++)
        {
            var candidate =
                candidates[index];
            var insertion =
                index - 1;

            while (insertion >= 0 &&
                   CompareHorizontalCandidates(
                       candidate,
                       candidates[insertion]) < 0)
            {
                candidates[insertion + 1] =
                    candidates[insertion];
                insertion--;
            }

            candidates[insertion + 1] =
                candidate;
        }
    }

    private static int CompareHorizontalCandidates(
        HorizontalCandidate left,
        HorizontalCandidate right)
    {
        var level =
            right.Fluid.Level.CompareTo(
                left.Fluid.Level);

        if (level != 0)
        {
            return level;
        }

        var distance =
            left.Fluid.SpreadDistance.CompareTo(
                right.Fluid.SpreadDistance);

        return distance != 0
            ? distance
            : left.Fluid.Fluid.Value.CompareTo(
                right.Fluid.Fluid.Value);
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
            neighbor.SpreadDistance == ushort.MaxValue
                ? ushort.MaxValue
                : (ushort)(
                    neighbor.SpreadDistance + 1);

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

    public static bool HorizontalSpreadIsPreferred(
        VoxelWorld world,
        WorldVoxelCoord origin,
        WorldVoxelCoord target,
        FluidRuntimeId fluid,
        ushort remainingSteps) =>
        HorizontalSpreadIsPreferred(
            world,
            origin,
            target,
            fluid,
            remainingSteps,
            new SolverScratch());

    private static bool HorizontalSpreadIsPreferred(
        VoxelWorld world,
        WorldVoxelCoord origin,
        WorldVoxelCoord target,
        FluidRuntimeId fluid,
        ushort remainingSteps,
        SolverScratch scratch)
    {
        var preferred =
            PreferredHorizontalDirections(
                world,
                origin,
                fluid,
                remainingSteps,
                scratch);

        if (preferred is null)
        {
            return true;
        }

        var direction =
            HorizontalDirectionBit(
                target.X - origin.X,
                target.Z - origin.Z);

        return direction is not null &&
               (preferred.Value &
                direction.Value) != 0;
    }

    private static byte? PreferredHorizontalDirections(
        VoxelWorld world,
        WorldVoxelCoord origin,
        FluidRuntimeId fluid,
        ushort remainingSteps,
        SolverScratch scratch)
    {
        if (remainingSteps == 0)
        {
            return null;
        }

        scratch.DownhillSearchCount++;
        scratch.Queue.Clear();
        scratch.Visited.Clear();
        scratch.Visited.Add(
            origin,
            new VisitedPath(0, 0));

        for (var index = 0;
             index < Horizontal.Length;
             index++)
        {
            var offset = Horizontal[index];
            var position =
                origin + offset;

            if (!CanFlowHorizontallyThrough(
                    world,
                    position,
                    fluid))
            {
                continue;
            }

            var direction =
                checked((byte)(1 << index));
            scratch.Visited[position] =
                new VisitedPath(
                    1,
                    direction);
            scratch.Queue.Enqueue(
                new SearchNode(
                    position,
                    1));
        }

        ushort? nearestDrop = null;
        byte preferred = 0;

        while (scratch.Queue.Count > 0)
        {
            var node =
                scratch.Queue.Dequeue();
            scratch.DownhillVisitedNodeCount++;

            if (nearestDrop is { } best &&
                node.Distance > best)
            {
                break;
            }

            var visited =
                scratch.Visited[node.Position];

            if (CanFallFrom(
                    world,
                    node.Position,
                    fluid))
            {
                if (nearestDrop is null)
                {
                    nearestDrop =
                        node.Distance;
                    preferred =
                        visited.Directions;
                }
                else if (
                    nearestDrop.Value ==
                    node.Distance)
                {
                    preferred |=
                        visited.Directions;
                }

                continue;
            }

            if (node.Distance >= remainingSteps ||
                nearestDrop is not null)
            {
                continue;
            }

            var nextDistance =
                checked((ushort)(
                    node.Distance + 1));

            foreach (var offset in Horizontal)
            {
                var next =
                    node.Position + offset;

                if (scratch.Visited.TryGetValue(
                        next,
                        out var known))
                {
                    if (known.Distance ==
                        nextDistance)
                    {
                        var merged =
                            (byte)(
                                known.Directions |
                                visited.Directions);

                        if (merged !=
                            known.Directions)
                        {
                            scratch.Visited[next] =
                                known with
                                {
                                    Directions =
                                        merged,
                                };
                            scratch.Queue.Enqueue(
                                new SearchNode(
                                    next,
                                    nextDistance));
                        }
                    }

                    continue;
                }

                if (!CanFlowHorizontallyThrough(
                        world,
                        next,
                        fluid))
                {
                    continue;
                }

                scratch.Visited.Add(
                    next,
                    new VisitedPath(
                        nextDistance,
                        visited.Directions));
                scratch.Queue.Enqueue(
                    new SearchNode(
                        next,
                        nextDistance));
            }
        }

        return nearestDrop is null
            ? null
            : preferred;
    }

    private static bool CanFlowHorizontallyThrough(
        VoxelWorld world,
        WorldVoxelCoord position,
        FluidRuntimeId fluid)
    {
        if (!world.IsLoadedAt(position) ||
            !world.GetCellOrEmpty(position).IsEmpty)
        {
            return false;
        }

        var existing =
            world.GetFluidOrEmpty(position);

        return existing.IsEmpty ||
               existing.Fluid == fluid;
    }

    private static bool CanFallFrom(
        VoxelWorld world,
        WorldVoxelCoord position,
        FluidRuntimeId fluid)
    {
        if (position.Y == 0)
        {
            return false;
        }

        var below =
            position + (0, -1, 0);

        if (!world.IsLoadedAt(below) ||
            !world.GetCellOrEmpty(below).IsEmpty)
        {
            return false;
        }

        var existing =
            world.GetFluidOrEmpty(below);

        return existing.IsEmpty ||
               (existing.Fluid == fluid &&
                !existing.IsSource &&
                existing.SpreadDistance == 0);
    }

    private static byte? HorizontalDirectionBit(
        int dx,
        int dz)
    {
        for (var index = 0;
             index < Horizontal.Length;
             index++)
        {
            var offset = Horizontal[index];

            if (offset.X == dx &&
                offset.Z == dz)
            {
                return checked(
                    (byte)(1 << index));
            }
        }

        return null;
    }

    private static FluidRuntimeId TransitionFluid(
        FluidCell current,
        FluidCell desired) =>
        !desired.IsEmpty
            ? desired.Fluid
            : !current.IsEmpty
                ? current.Fluid
                : FluidRuntimeId.None;

    public sealed class SolverScratch
    {
        internal Queue<SearchNode> Queue { get; } = new();

        internal Dictionary<WorldVoxelCoord, VisitedPath>
            Visited { get; } = [];

        public int DownhillSearchCount { get; internal set; }

        public int DownhillVisitedNodeCount { get; internal set; }
    }

    private readonly record struct HorizontalCandidate(
        WorldVoxelCoord Origin,
        FluidCell Fluid,
        ushort RemainingSteps);

    internal readonly record struct SearchNode(
        WorldVoxelCoord Position,
        ushort Distance);

    internal readonly record struct VisitedPath(
        ushort Distance,
        byte Directions);
}

internal static class UShortMath
{
    public static ushort SaturatingSubtract(
        this ushort value,
        ushort amount) =>
        amount >= value
            ? (ushort)0
            : (ushort)(value - amount);
}
