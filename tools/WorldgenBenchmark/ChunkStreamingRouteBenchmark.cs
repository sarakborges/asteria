using System.Diagnostics;
using Asteria.Core.Content;
using Asteria.Core.World;

/// <summary>
/// Headless movement-route benchmark for Core chunk selection and residency.
/// It intentionally stops at chunk activation/presentation reservation: Godot
/// mesh objects, collision shapes, GPU uploads and frame pacing are not measured.
/// </summary>
internal static class ChunkStreamingRouteBenchmark
{
    private const int FramesPerChunk = 8;
    private const int MaximumPresentationReservationsPerFrame = 4;
    private const int MaximumFramesAfterRoute = 30_000;
    private const int RetentionMarginChunks = 4;
    private const ulong FnvOffset = 14695981039346656037UL;

    public static object Run(
        BiomeWorldGenerator generator,
        BlockRegistry blocks,
        FluidRegistry fluids,
        ChunkCoord start,
        int routeSteps,
        int horizontalRadius)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        if (routeSteps < 2)
            throw new ArgumentOutOfRangeException(nameof(routeSteps));
        if (horizontalRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalRadius));

        var materializationWorkers = Math.Clamp(
            Environment.ProcessorCount - 2, 1, 6);
        var maximumWorldWorkers = Math.Clamp(
            Environment.ProcessorCount - 1, 2, 8);
        var retentionRadius = horizontalRadius + RetentionMarginChunks;
        var rules = new WorldGameRules();
        var world = new VoxelWorld();
        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates = new FluidMeshUpdateQueue();
        var physicsUpdates = new BlockPhysicsUpdateQueue();
        var terrainRevisions = new MeshletContentRevisions();
        var fluidRevisions = new MeshletContentRevisions();
        var ticks = new WorldTickClock();

        var mutations = new VoxelMutationRuntime(
            world,
            worldUpdates,
            fluidUpdates,
            fluidMeshUpdates,
            physicsUpdates,
            terrainRevisions,
            fluidRevisions);
        var droppedBlocks = new DroppedBlockRuntime(world, blocks);
        var physics = new BlockPhysicsRuntime(
            world,
            blocks,
            mutations,
            physicsUpdates,
            droppedBlocks);
        var residency = new ChunkResidencyRuntime(
            world,
            blocks,
            fluids,
            generator,
            worldUpdates,
            fluidUpdates,
            fluidMeshUpdates,
            physics,
            terrainRevisions,
            fluidRevisions,
            ticks,
            new ChunkResidencySettings(
                maxMaterializationsInFlight: materializationWorkers,
                maxDispatchesPerFrame: materializationWorkers,
                maxResultsPerFrame: 8,
                maxEvictionsPerFrame: 2,
                gameRules: rules));
        var selection = new ChunkStreamingSelectionRuntime(generator);
        var presented = new HashSet<ChunkCoord>();
        var chunkDigests = new Dictionary<ChunkCoord, ulong>();
        var stageCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var stageMilliseconds = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var activationElapsed = new List<double>();
        var pendingQueueWait = new List<double>();
        var workerQueueWait = new List<double>();
        var workerWall = new List<double>();
        var resultCollectionWait = new List<double>();
        var selectionLatencyFrames = new List<int>();
        var stepStates = new List<RouteStepState>(routeSteps);

        var totalStartedTasks = 0;
        var providerActivations = 0;
        var archiveRestores = 0;
        var totalRetirements = 0;
        var totalFluidCellsProcessed = 0L;
        var totalFluidFrontierCellsProcessed = 0L;
        var totalPhysicsWakeupsProcessed = 0L;
        var totalFallingBlocksStarted = 0L;
        var totalUnsupportedBlocksRemoved = 0L;
        var totalWorkerMilliseconds = 0d;
        var totalTerrainMilliseconds = 0d;
        var totalLightingMilliseconds = 0d;
        var maximumPending = 0;
        var maximumMaterializationJobs = 0;
        var maximumActiveWorkers = 0;
        var maximumDesired = 0;
        var maximumRetained = 0;
        var maximumResident = 0;
        var maximumArchived = 0;
        var maximumPresentationPending = 0;
        var maximumFluidPending = 0;
        var maximumLightingSeeds = 0;
        var maximumPhysicsWakeups = 0;
        var maximumDirtyTerrainChunks = 0;
        var maximumLocalMissingChunks = 0;
        var localGapFrames = 0;
        var localMissingChunkFrames = 0L;
        var pressuredFrames = 0;
        var currentCenter = start;
        var currentRouteStep = -1;
        var currentSelectionApplied = false;
        var currentSelectionRequestedFrame = -1;
        var lastFluidPending = 0;
        var frame = 0;
        var loopStarted = Stopwatch.GetTimestamp();
        var frameLimit = checked(
            routeSteps * FramesPerChunk + MaximumFramesAfterRoute);

        while (frame < frameLimit)
        {
            if (frame % FramesPerChunk == 0 &&
                currentRouteStep + 1 < routeSteps)
            {
                currentRouteStep++;
                currentCenter = new ChunkCoord(
                    checked(start.X + currentRouteStep),
                    start.Y,
                    start.Z);
                currentSelectionApplied = false;
                currentSelectionRequestedFrame = frame;
                stepStates.Add(new RouteStepState(
                    currentRouteStep,
                    currentCenter,
                    frame));

                var bootstrap = residency.BootstrapSelection(
                    currentCenter,
                    horizontalRadius);
                residency.SyncSelection(
                    currentCenter,
                    horizontalRadius,
                    retentionRadius,
                    bootstrap,
                    presented);
                selection.Request(currentCenter, horizontalRadius);
            }

            if (selection.TryPollCompleted(
                    out var completedSelection,
                    out var selectionError))
            {
                if (selectionError is not null)
                    throw new InvalidOperationException(
                        "Headless route selection worker failed.",
                        selectionError);

                if (completedSelection is not null)
                {
                    residency.SyncSelection(
                        completedSelection.Center,
                        completedSelection.HorizontalRadius,
                        completedSelection.HorizontalRadius +
                            RetentionMarginChunks,
                        completedSelection.Desired,
                        presented);
                    if (completedSelection.Center == currentCenter)
                    {
                        currentSelectionApplied = true;
                        var selectionFrames = Math.Max(
                            0, frame - currentSelectionRequestedFrame);
                        selectionLatencyFrames.Add(selectionFrames);
                        if (stepStates.Count > 0)
                            stepStates[^1].SelectionReadyAfterFrames = selectionFrames;
                    }
                }
            }

            var budget = WorldFrameWorkBudget.Begin(
                1d / 60d,
                Stopwatch.GetTimestamp(),
                Stopwatch.Frequency);

            var collected = residency.CollectMaterializationResults(
                budget, presented);
            AccumulateUpdate(
                collected,
                world,
                chunkDigests,
                activationElapsed,
                pendingQueueWait,
                workerQueueWait,
                workerWall,
                resultCollectionWait,
                ref providerActivations,
                ref archiveRestores,
                ref totalWorkerMilliseconds,
                ref totalTerrainMilliseconds,
                ref totalLightingMilliseconds,
                stageCounts,
                stageMilliseconds);

            var availableSlots = Math.Max(
                0, maximumWorldWorkers - residency.ActiveWorkerCount);
            var pressure = WorldStreamingWorkPressure.Evaluate(
                1d / 60d,
                worldUpdates.DirtyChunkCount,
                loadingWorld: false,
                pendingLightingSeeds: worldUpdates.LightingEditCount,
                pendingPhysicsWakeups: physicsUpdates.Count,
                pendingTerrainPublications: 0);
            if (pressure.IsPressured)
                pressuredFrames++;
            var maximumNewTasks = pressure.LimitMaterializationStarts(
                availableSlots);
            var dispatched = residency.DispatchMaterializationTasks(
                budget,
                presented,
                maximumNewTasks);
            totalStartedTasks += dispatched.StartedTasks;
            AccumulateUpdate(
                dispatched,
                world,
                chunkDigests,
                activationElapsed,
                pendingQueueWait,
                workerQueueWait,
                workerWall,
                resultCollectionWait,
                ref providerActivations,
                ref archiveRestores,
                ref totalWorkerMilliseconds,
                ref totalTerrainMilliseconds,
                ref totalLightingMilliseconds,
                stageCounts,
                stageMilliseconds);

            var fluidReport = residency.ProcessPendingFluidActivations(budget);
            lastFluidPending = fluidReport.PendingChunks;
            totalFluidCellsProcessed += fluidReport.FluidCellsProcessed;
            totalFluidFrontierCellsProcessed += fluidReport.FrontierCellsProcessed;

            var physicsReport = physics.ProcessWakeups();
            totalFallingBlocksStarted += physicsReport.FallingStarted;
            totalUnsupportedBlocksRemoved += physicsReport.UnsupportedRemoved;
            totalPhysicsWakeupsProcessed += physicsReport.FallingStarted +
                physicsReport.UnsupportedRemoved;

            var reservedPresentations = 0;
            while (reservedPresentations < MaximumPresentationReservationsPerFrame &&
                   residency.PopPresentationByPriority() is { } ready)
            {
                presented.Add(ready);
                reservedPresentations++;
            }
            residency.SyncResidentState(presented);

            var retirements = residency.RetireDistantChunks(budget);
            foreach (var retirement in retirements)
                presented.Remove(retirement.Coord);
            totalRetirements += retirements.Count;

            var localChunks = ChunkStreamingSelection.DesiredPlayerLocalChunks(
                currentCenter, horizontalRadius);
            var localMissing = localChunks.Count(
                coord => !world.ContainsChunk(coord));
            if (localMissing > 0)
            {
                localGapFrames++;
                localMissingChunkFrames += localMissing;
            }
            maximumLocalMissingChunks = Math.Max(
                maximumLocalMissingChunks, localMissing);
            if (localMissing == 0 &&
                stepStates.Count > 0 &&
                stepStates[^1].LocalReadyAfterFrames is null)
            {
                stepStates[^1].LocalReadyAfterFrames =
                    frame - stepStates[^1].RequestedAtFrame;
            }

            maximumPending = Math.Max(maximumPending, residency.PendingCount);
            maximumMaterializationJobs = Math.Max(
                maximumMaterializationJobs, residency.MaterializationJobCount);
            maximumActiveWorkers = Math.Max(
                maximumActiveWorkers, residency.ActiveWorkerCount);
            maximumDesired = Math.Max(maximumDesired, residency.DesiredCount);
            maximumRetained = Math.Max(maximumRetained, residency.RetainedCount);
            maximumResident = Math.Max(maximumResident, world.ChunkCount);
            maximumArchived = Math.Max(maximumArchived, world.ArchivedChunkCount);
            maximumPresentationPending = Math.Max(
                maximumPresentationPending, residency.PresentationPendingCount);
            maximumFluidPending = Math.Max(maximumFluidPending, lastFluidPending);
            maximumLightingSeeds = Math.Max(
                maximumLightingSeeds, worldUpdates.LightingEditCount);
            maximumPhysicsWakeups = Math.Max(
                maximumPhysicsWakeups, physicsUpdates.Count);
            maximumDirtyTerrainChunks = Math.Max(
                maximumDirtyTerrainChunks, worldUpdates.DirtyChunkCount);

            var routeAndSelectionComplete =
                currentRouteStep == routeSteps - 1 &&
                currentSelectionApplied &&
                !selection.IsRunning;
            var residencyDrained =
                residency.PendingCount == 0 &&
                residency.MaterializationJobCount == 0 &&
                residency.PresentationPendingCount == 0;
            if (routeAndSelectionComplete &&
                residencyDrained &&
                lastFluidPending == 0)
            {
                frame++;
                break;
            }

            frame++;
            Thread.Sleep(1);
        }

        var elapsedMilliseconds = Stopwatch.GetElapsedTime(loopStarted).TotalMilliseconds;
        if (currentRouteStep != routeSteps - 1 ||
            !currentSelectionApplied ||
            selection.IsRunning ||
            residency.PendingCount != 0 ||
            residency.MaterializationJobCount != 0 ||
            residency.PresentationPendingCount != 0 ||
            lastFluidPending != 0)
        {
            selection.BeginRetirement();
            residency.BeginRetirement();
            throw new TimeoutException(
                $"Headless streaming route did not drain after {frame} frames " +
                $"(pending={residency.PendingCount}, jobs={residency.MaterializationJobCount}, " +
                $"presentation={residency.PresentationPendingCount}, fluids={lastFluidPending}).");
        }

        selection.BeginRetirement();
        residency.BeginRetirement();

        var routeDigest = FnvOffset;
        routeDigest = HashNumber(routeDigest, (ulong)routeSteps);
        routeDigest = HashNumber(routeDigest, (ulong)horizontalRadius);
        foreach (var pair in chunkDigests
                     .OrderBy(entry => entry.Key.Y)
                     .ThenBy(entry => entry.Key.Z)
                     .ThenBy(entry => entry.Key.X))
        {
            routeDigest = HashNumber(
                routeDigest, unchecked((ulong)pair.Key.X));
            routeDigest = HashNumber(
                routeDigest, unchecked((ulong)pair.Key.Y));
            routeDigest = HashNumber(
                routeDigest, unchecked((ulong)pair.Key.Z));
            routeDigest = HashNumber(routeDigest, pair.Value);
        }

        return new
        {
            RouteSteps = routeSteps,
            MovementChunks = routeSteps - 1,
            FramesPerChunk,
            HorizontalRadius = horizontalRadius,
            RetentionRadius = retentionRadius,
            MaterializationWorkers = materializationWorkers,
            MaximumWorldWorkers = maximumWorldWorkers,
            FrameCount = frame,
            TotalElapsedMilliseconds = elapsedMilliseconds,
            SelectionResultLatencyMedianFrames = Median(selectionLatencyFrames),
            SelectionResultLatencyP95Frames = Percentile95(selectionLatencyFrames),
            StartedMaterializationTasks = totalStartedTasks,
            ProviderActivations = providerActivations,
            ArchivedChunkRestores = archiveRestores,
            UniqueActivatedChunkCount = chunkDigests.Count,
            AverageWorkerMillisecondsPerProviderActivation =
                providerActivations == 0 ? 0d :
                    totalWorkerMilliseconds / providerActivations,
            TotalWorkerMilliseconds = totalWorkerMilliseconds,
            TotalTerrainMilliseconds = totalTerrainMilliseconds,
            TotalInitialLightingMilliseconds = totalLightingMilliseconds,
            ActivationQueueWaitMedianMilliseconds = Median(pendingQueueWait),
            ActivationQueueWaitP95Milliseconds = Percentile95(pendingQueueWait),
            WorkerQueueWaitMedianMilliseconds = Median(workerQueueWait),
            WorkerQueueWaitP95Milliseconds = Percentile95(workerQueueWait),
            WorkerWallMedianMilliseconds = Median(workerWall),
            ResultCollectionWaitMedianMilliseconds = Median(resultCollectionWait),
            ActivationElapsedMedianMilliseconds = Median(activationElapsed),
            ActivationElapsedP95Milliseconds = Percentile95(activationElapsed),
            FramesWithPlayerLocalGaps = localGapFrames,
            PlayerLocalMissingChunkFrames = localMissingChunkFrames,
            MaximumPlayerLocalMissingChunks = maximumLocalMissingChunks,
            PerMovementStep = stepStates.Select(step => new
            {
                step.Step,
                CenterChunkX = step.Center.X,
                CenterChunkY = step.Center.Y,
                CenterChunkZ = step.Center.Z,
                SelectionReadyAfterFrames = step.SelectionReadyAfterFrames,
                PlayerLocalReadyAfterFrames = step.LocalReadyAfterFrames,
            }).ToArray(),
            MaximumPendingChunks = maximumPending,
            MaximumMaterializationJobs = maximumMaterializationJobs,
            MaximumActiveWorkers = maximumActiveWorkers,
            MaximumDesiredChunks = maximumDesired,
            MaximumRetainedChunks = maximumRetained,
            MaximumResidentChunks = maximumResident,
            MaximumArchivedChunks = maximumArchived,
            MaximumPresentationPendingChunks = maximumPresentationPending,
            MaximumPendingFluidActivationChunks = maximumFluidPending,
            MaximumPendingLightingSeeds = maximumLightingSeeds,
            MaximumPendingPhysicsWakeups = maximumPhysicsWakeups,
            MaximumDirtyTerrainChunks = maximumDirtyTerrainChunks,
            PressuredFrames = pressuredFrames,
            TotalRetiredChunks = totalRetirements,
            TotalFluidCellsActivated = totalFluidCellsProcessed,
            TotalFluidFrontierCellsActivated = totalFluidFrontierCellsProcessed,
            TotalPhysicsWakeupsProcessed = totalPhysicsWakeupsProcessed,
            TotalFallingBlocksStarted = totalFallingBlocksStarted,
            TotalUnsupportedBlocksRemoved = totalUnsupportedBlocksRemoved,
            MaterializationStages = stageCounts.Select(pair => new
            {
                Stage = pair.Key,
                AdvanceCount = pair.Value,
                WorkerMilliseconds = stageMilliseconds[pair.Key],
            }).ToArray(),
            RouteDigest = routeDigest,
            GeneratedChunkDigestsMatchOnReactivation = true,
            Notes = new[]
            {
                "Uses the real Core ChunkStreamingSelectionRuntime and ChunkResidencyRuntime with the production BiomeWorldGenerator provider.",
                "Route centers move one chunk along +X every eight simulated 60 Hz frames; selection and materialization workers run concurrently.",
                "Materialization worker limits, dispatch/result limits, retention margin and presentation reservation throughput mirror Main.cs.",
                "Presentation reservations are treated as immediate and cost-free; Godot mesh building, collision construction, GPU upload and actual frame pacing are excluded.",
                "World streaming pressure uses Core queue counts, but terrain mesh publications are not modeled by this headless route.",
                "A route digest covers every unique activated chunk coordinate and its generated block content; repeated activation must reproduce the same chunk digest.",
            },
        };
    }

    private static void AccumulateUpdate(
        ChunkResidencyUpdate update,
        VoxelWorld world,
        IDictionary<ChunkCoord, ulong> chunkDigests,
        ICollection<double> activationElapsed,
        ICollection<double> pendingQueueWait,
        ICollection<double> workerQueueWait,
        ICollection<double> workerWall,
        ICollection<double> resultCollectionWait,
        ref int providerActivations,
        ref int archiveRestores,
        ref double totalWorkerMilliseconds,
        ref double totalTerrainMilliseconds,
        ref double totalLightingMilliseconds,
        IDictionary<string, int> stageCounts,
        IDictionary<string, double> stageMilliseconds)
    {
        foreach (var failure in update.Failures)
            throw new InvalidOperationException(
                $"Chunk {failure.Coord} failed during route materialization.",
                failure.Error);

        foreach (var timing in update.MaterializationStageTimings)
        {
            var name = timing.Stage.ToString();
            stageCounts[name] = stageCounts.TryGetValue(name, out var count)
                ? count + 1
                : 1;
            stageMilliseconds[name] = (
                stageMilliseconds.TryGetValue(name, out var milliseconds)
                    ? milliseconds
                    : 0d) + timing.WorkerMilliseconds;
        }

        foreach (var activation in update.Activations)
        {
            if (activation.Source == ChunkActivationSource.Provider)
                providerActivations++;
            else
                archiveRestores++;

            totalWorkerMilliseconds += activation.WorkerMilliseconds;
            totalTerrainMilliseconds += activation.TerrainMilliseconds;
            totalLightingMilliseconds += activation.InitialLightingMilliseconds;
            activationElapsed.Add(activation.ElapsedMilliseconds);
            pendingQueueWait.Add(activation.PendingQueueWaitMilliseconds);
            workerQueueWait.Add(activation.WorkerQueueWaitMilliseconds);
            workerWall.Add(activation.WorkerWallMilliseconds);
            resultCollectionWait.Add(activation.ResultCollectionWaitMilliseconds);

            if (!world.TryGetChunk(activation.Coord, out var chunk))
                throw new InvalidOperationException(
                    $"Activated chunk {activation.Coord} is not resident.");

            var digest = ChunkDigest(activation.Coord, chunk);
            if (chunkDigests.TryGetValue(activation.Coord, out var prior) &&
                prior != digest)
            {
                throw new InvalidOperationException(
                    $"Chunk {activation.Coord} changed content after archive/restore.");
            }
            chunkDigests[activation.Coord] = digest;
        }
    }

    private static ulong ChunkDigest(ChunkCoord coord, Chunk chunk)
    {
        var digest = FnvOffset;
        digest = HashNumber(digest, unchecked((ulong)coord.X));
        digest = HashNumber(digest, unchecked((ulong)coord.Y));
        digest = HashNumber(digest, unchecked((ulong)coord.Z));
        chunk.VisitBlockCells((x, y, z, cell) =>
        {
            digest = HashNumber(digest, (ulong)x);
            digest = HashNumber(digest, (ulong)y);
            digest = HashNumber(digest, (ulong)z);
            digest = HashNumber(digest, cell.Block.Value);
        });
        return digest;
    }

    private static ulong HashNumber(ulong current, ulong value) =>
        unchecked((current ^ value) * 1099511628211UL);

    private static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
            return 0d;
        var ordered = values.Order().ToArray();
        var middle = ordered.Length / 2;
        return (ordered.Length & 1) == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private static double Percentile95(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
            return 0d;
        var ordered = values.Order().ToArray();
        var index = Math.Clamp(
            (int)Math.Ceiling(ordered.Length * 0.95d) - 1,
            0,
            ordered.Length - 1);
        return ordered[index];
    }

    private sealed class RouteStepState(
        int step,
        ChunkCoord center,
        int requestedAtFrame)
    {
        public int Step { get; } = step;
        public ChunkCoord Center { get; } = center;
        public int RequestedAtFrame { get; } = requestedAtFrame;
        public int? SelectionReadyAfterFrames { get; set; }
        public int? LocalReadyAfterFrames { get; set; }
    }
}
