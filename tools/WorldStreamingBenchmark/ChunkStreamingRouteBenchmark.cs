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

        var maximumWorldWorkers = ReadMaximumWorldWorkers(
            Math.Clamp(Environment.ProcessorCount - 1, 2, 8));
        var hasControlledWorkerCap = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("ASTERIA_BENCH_MAX_WORLD_WORKERS"));
        // Normal route runs retain the production terrain-admission cap.
        // A controlled worker cap defaults admission to the same value unless
        // ASTERIA_BENCH_MAX_TERRAIN_ADMISSION independently overrides it.
        var defaultTerrainAdmissionLimit = hasControlledWorkerCap
            ? maximumWorldWorkers
            : Math.Clamp(Environment.ProcessorCount - 2, 1, 6);
        var terrainAdmissionLimit = ReadTerrainAdmissionLimit(
            defaultTerrainAdmissionLimit);
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
        var residencySettings = CreateResidencySettings(
            terrainAdmissionLimit, maximumWorldWorkers, rules);
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
            residencySettings);
        var selection = new ChunkStreamingSelectionRuntime(generator);
        var presented = new HashSet<ChunkCoord>();
        var chunkDigests = new Dictionary<ChunkCoord, ChunkContentDigest>();
        var chunkBlockDiagnostics = new Dictionary<ChunkCoord, ChunkBlockDiagnostics>();
        var stageCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var stageMilliseconds = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var stageMaximumMilliseconds = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var stageMaximumSamples = new SortedDictionary<string, ChunkMaterializationStageTiming>(StringComparer.Ordinal);
        var supportFallbackDiagnostics = new SupportFallbackDiagnostics();
        var selectionLatencyFrames = new List<int>();
        var stepStates = new List<RouteStepState>(routeSteps);

        var totalStartedTasks = 0;
        var providerActivations = 0;
        var archiveRestores = 0;
        var totalRetirements = 0;
        var totalFluidCellsProcessed = 0L;
        var totalFluidFrontierCellsProcessed = 0L;
        var totalFluidActivationSlices = 0L;
        var totalFluidActivationMilliseconds = 0d;
        var maximumFluidActivationSliceMilliseconds = 0d;
        var totalPhysicsWakeupsProcessed = 0L;
        var totalFallingBlocksStarted = 0L;
        var totalUnsupportedBlocksRemoved = 0L;
        var observedFallingBlockIds = new HashSet<ulong>();
        var fallingBlockSamples = new List<object>();
        var gravityWakeupObservations =
            new Dictionary<WorldVoxelCoord, GravityWakeupObservation>();
        var gravityWakeupSamples = new List<object>();
        var activatedChunkFrames = new Dictionary<ChunkCoord, int>();
        var totalWorkerMilliseconds = 0d;
        var totalTerrainMilliseconds = 0d;
        var totalLightingMilliseconds = 0d;
        long totalWorkerSlices = 0;
        var totalInterSliceWaitMilliseconds = 0d;
        var maximumInterSliceWaitMillisecondsPerChunk = 0d;
        var maximumWorkerSlicesPerChunk = 0;
        var hasDetailedSchedulerDiagnostics =
            typeof(ChunkResidencyRuntime).GetProperty("GenerationWorkerCount") is not null &&
            typeof(ChunkResidencyRuntime).GetProperty("ActiveGenerationWorkerCount") is not null &&
            typeof(ChunkResidencyRuntime).GetProperty("PendingGenerationWorkerQueueCount") is not null;
        var maximumPending = 0;
        var maximumMaterializationJobs = 0;
        var maximumActiveWorkers = 0;
        var maximumActiveGenerationWorkers = 0;
        var maximumPendingGenerationWorkerQueue = 0;
        var maximumSuspendedMaterializations = 0;
        var maximumReadyGenerationTasks = 0;
        var maximumPendingGenerationWorkerOperations = 0;
        var maximumPendingGenerationCompletions = 0;
        var admissionSaturatedIdleWorkerFrames = 0;
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
                blocks,
                chunkDigests,
                chunkBlockDiagnostics,
                activatedChunkFrames,
                frame,
                ref providerActivations,
                ref archiveRestores,
                ref totalWorkerMilliseconds,
                ref totalTerrainMilliseconds,
                ref totalLightingMilliseconds,
                stageCounts,
                stageMilliseconds,
                stageMaximumMilliseconds,
                stageMaximumSamples,
                supportFallbackDiagnostics);
            AccumulateSchedulerDiagnostics(
                collected.Activations,
                ref totalWorkerSlices,
                ref totalInterSliceWaitMilliseconds,
                ref maximumInterSliceWaitMillisecondsPerChunk,
                ref maximumWorkerSlicesPerChunk);

            var availableSlots = Math.Max(
                0, maximumWorldWorkers - residency.ActiveWorkerCount);
            // The route does not run lighting or terrain-mesh workers. Observe
            // those queues, but do not use their undrained work as artificial
            // backpressure for the measured materialization path.
            var pressure = WorldStreamingWorkPressure.Evaluate(
                1d / 60d,
                dirtyTerrainChunks: 0,
                loadingWorld: false,
                pendingLightingSeeds: 0,
                pendingPhysicsWakeups: physicsUpdates.Count,
                pendingTerrainPublications: 0);
            if (pressure.IsPressured)
                pressuredFrames++;
            var maximumNewTasks = pressure.IsPressured
                ? Math.Min(availableSlots, 2)
                : availableSlots;
            var jobsBeforeDispatch = residency.MaterializationJobCount;
            var dispatched = residency.DispatchMaterializationTasks(
                budget,
                presented,
                maximumNewTasks);
            totalStartedTasks += Math.Max(
                0, residency.MaterializationJobCount - jobsBeforeDispatch);
            AccumulateUpdate(
                dispatched,
                world,
                blocks,
                chunkDigests,
                chunkBlockDiagnostics,
                activatedChunkFrames,
                frame,
                ref providerActivations,
                ref archiveRestores,
                ref totalWorkerMilliseconds,
                ref totalTerrainMilliseconds,
                ref totalLightingMilliseconds,
                stageCounts,
                stageMilliseconds,
                stageMaximumMilliseconds,
                stageMaximumSamples,
                supportFallbackDiagnostics);
            AccumulateSchedulerDiagnostics(
                dispatched.Activations,
                ref totalWorkerSlices,
                ref totalInterSliceWaitMilliseconds,
                ref maximumInterSliceWaitMillisecondsPerChunk,
                ref maximumWorkerSlicesPerChunk);

            var fluidReport = residency.ProcessPendingFluidActivations(budget);
            lastFluidPending = fluidReport.PendingChunks;
            totalFluidCellsProcessed += fluidReport.FluidCellsProcessed;
            totalFluidFrontierCellsProcessed += fluidReport.FrontierCellsProcessed;
            totalFluidActivationSlices += fluidReport.SlicesProcessed;
            totalFluidActivationMilliseconds += fluidReport.ProcessingMilliseconds;
            maximumFluidActivationSliceMilliseconds = Math.Max(
                maximumFluidActivationSliceMilliseconds,
                fluidReport.MaximumSliceMilliseconds);

            maximumPhysicsWakeups = Math.Max(
                maximumPhysicsWakeups, physicsUpdates.Count);

            // Snapshot queued gravity checks before ProcessWakeups mutates the
            // voxel world. This separates blocks generated without a lower
            // support from blocks exposed later by another falling block.
            var pendingWakeups = physicsUpdates.Count > 0
                ? physicsUpdates.CaptureState().Positions
                : Array.Empty<WorldVoxelCoord>();
            foreach (var position in pendingWakeups)
            {
                if (!world.TryGetCell(position, out var pendingCell) ||
                    pendingCell.IsEmpty)
                    continue;

                var pendingDefinition = blocks.GetDefinition(pendingCell.Block);
                if (!pendingDefinition.HasTag(BlockPhysicsCapabilities.Gravity))
                    continue;

                var below = position + (0, -1, 0);
                if (BlockMaterializationRules.Evaluate(world, below) !=
                    BlockMaterializationState.Available)
                    continue;

                if (gravityWakeupObservations.ContainsKey(position))
                    continue;

                var sourceChunk = VoxelCoordinates.FromWorld(
                    position.X, position.Y, position.Z).Chunk;
                var belowChunk = VoxelCoordinates.FromWorld(
                    below.X, below.Y, below.Z).Chunk;
                int? activatedFrame = activatedChunkFrames.TryGetValue(
                    sourceChunk, out var firstActivated)
                    ? firstActivated
                    : null;
                int? belowActivatedFrame = activatedChunkFrames.TryGetValue(
                    belowChunk, out var belowFirstActivated)
                    ? belowFirstActivated
                    : null;
                var observation = new GravityWakeupObservation(
                    frame,
                    physicsUpdates.Count,
                    activatedFrame,
                    belowActivatedFrame,
                    pendingDefinition.Id,
                    position,
                    below);
                gravityWakeupObservations.Add(position, observation);
                if (gravityWakeupSamples.Count < 64)
                {
                    gravityWakeupSamples.Add(new
                    {
                        Block = pendingDefinition.Id,
                        X = position.X,
                        Y = position.Y,
                        Z = position.Z,
                        SourceChunkX = sourceChunk.X,
                        SourceChunkY = sourceChunk.Y,
                        SourceChunkZ = sourceChunk.Z,
                        WakeupFrame = frame,
                        SourceChunkActivatedFrame = activatedFrame,
                        FramesSinceSourceChunkActivation = activatedFrame is { } activated
                            ? frame - activated
                            : (int?)null,
                        BelowChunkX = belowChunk.X,
                        BelowChunkY = belowChunk.Y,
                        BelowChunkZ = belowChunk.Z,
                        BelowChunkActivatedFrame = belowActivatedFrame,
                        FramesSinceBelowChunkActivation =
                            belowActivatedFrame is { } belowActivated
                                ? frame - belowActivated
                                : (int?)null,
                        FramesSinceBothChunksResident =
                            activatedFrame is { } sourceAt &&
                            belowActivatedFrame is { } supportAt
                                ? frame - Math.Max(sourceAt, supportAt)
                                : (int?)null,
                        QueueDepth = physicsUpdates.Count,
                        BelowBlock = "asteria:air",
                    });
                }
            }

            var physicsReport = physics.ProcessWakeups();
            totalFallingBlocksStarted += physicsReport.FallingStarted;
            totalUnsupportedBlocksRemoved += physicsReport.UnsupportedRemoved;
            totalPhysicsWakeupsProcessed += physicsReport.FallingStarted +
                physicsReport.UnsupportedRemoved;

            foreach (var falling in physics.ActiveBlocks)
            {
                if (!observedFallingBlockIds.Add(falling.Id.Value))
                    continue;

                var worldY = checked((int)Math.Floor(falling.CenterY));
                var source = new WorldVoxelCoord(
                    falling.ColumnX, worldY, falling.ColumnZ);
                var sourceChunk = VoxelCoordinates.FromWorld(
                    source.X, source.Y, source.Z).Chunk;
                var definition = blocks.GetDefinition(falling.Cell.Block);
                var below = source + (0, -1, 0);
                var belowLoaded = world.TryGetCell(below, out var belowCell);
                var belowBlock = !belowLoaded
                    ? "unloaded"
                    : belowCell.IsEmpty
                        ? "asteria:air"
                        : blocks.GetDefinition(belowCell.Block).Id;

                var hasWakeup = gravityWakeupObservations.TryGetValue(
                    source, out var wakeup);
                ChunkCoord? wakeupBelowChunk = hasWakeup
                    ? VoxelCoordinates.FromWorld(
                        wakeup.Below.X, wakeup.Below.Y, wakeup.Below.Z).Chunk
                    : null;

                if (fallingBlockSamples.Count < 64)
                {
                    fallingBlockSamples.Add(new
                    {
                        FallingId = falling.Id.Value,
                        Block = definition.Id,
                        IsGravityTagged = definition.HasTag(
                            BlockPhysicsCapabilities.Gravity),
                        X = source.X,
                        Y = source.Y,
                        Z = source.Z,
                        SourceChunkX = sourceChunk.X,
                        SourceChunkY = sourceChunk.Y,
                        SourceChunkZ = sourceChunk.Z,
                        BelowBlock = belowBlock,
                        BelowChunkLoaded = belowLoaded,
                        WakeupFrame = hasWakeup ? wakeup.Frame : (int?)null,
                        SourceChunkActivatedFrame = hasWakeup
                            ? wakeup.ActivatedFrame
                            : null,
                        FramesSinceSourceChunkActivation =
                            hasWakeup && wakeup.ActivatedFrame is { } activatedAt
                                ? wakeup.Frame - activatedAt
                                : (int?)null,
                        BelowChunkX = wakeupBelowChunk?.X,
                        BelowChunkY = wakeupBelowChunk?.Y,
                        BelowChunkZ = wakeupBelowChunk?.Z,
                        BelowChunkActivatedFrame = hasWakeup
                            ? wakeup.BelowActivatedFrame
                            : null,
                        FramesSinceBelowChunkActivation =
                            hasWakeup && wakeup.BelowActivatedFrame is { } belowActivatedAt
                                ? wakeup.Frame - belowActivatedAt
                                : (int?)null,
                        FramesSinceBothChunksResident =
                            hasWakeup &&
                            wakeup.ActivatedFrame is { } sourceAt &&
                            wakeup.BelowActivatedFrame is { } supportAt
                                ? wakeup.Frame - Math.Max(sourceAt, supportAt)
                                : (int?)null,
                        QueueDepthAtWakeup = hasWakeup ? wakeup.QueueDepth : (int?)null,
                        BelowBlockAtWakeup = hasWakeup ? "asteria:air" : "not-captured",
                    });
                }
            }

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
            var activeGenerationWorkers = ReadOptionalRuntimeCounter(
                residency, "ActiveGenerationWorkerCount");
            var pendingGenerationWorkerQueue = ReadOptionalRuntimeCounter(
                residency, "PendingGenerationWorkerQueueCount");
            var generationWorkerCount = ReadOptionalRuntimeCounter(
                residency, "GenerationWorkerCount", maximumWorldWorkers);
            var terrainAdmissionCount = ReadOptionalRuntimeCounter(
                residency, "MaxTerrainMaterializationsInFlight",
                terrainAdmissionLimit);
            maximumActiveGenerationWorkers = Math.Max(
                maximumActiveGenerationWorkers, activeGenerationWorkers);
            maximumPendingGenerationWorkerQueue = Math.Max(
                maximumPendingGenerationWorkerQueue, pendingGenerationWorkerQueue);
            maximumSuspendedMaterializations = Math.Max(
                maximumSuspendedMaterializations,
                residency.SuspendedMaterializationCount);
            var pendingGenerationWorkerOperations = ReadOptionalRuntimeCounter(
                residency, "PendingGenerationWorkerOperations");
            var readyGenerationTasks = ReadOptionalRuntimeCounter(
                residency, "ReadyGenerationTaskCount");
            var pendingGenerationCompletions = ReadOptionalRuntimeCounter(
                residency, "PendingGenerationCompletions");
            maximumPendingGenerationWorkerOperations = Math.Max(
                maximumPendingGenerationWorkerOperations,
                pendingGenerationWorkerOperations);
            maximumPendingGenerationCompletions = Math.Max(
                maximumPendingGenerationCompletions,
                pendingGenerationCompletions);
            maximumReadyGenerationTasks = Math.Max(
                maximumReadyGenerationTasks, readyGenerationTasks);
            if (hasDetailedSchedulerDiagnostics &&
                residency.MaterializationJobCount >= terrainAdmissionCount &&
                activeGenerationWorkers + pendingGenerationWorkerQueue <
                    generationWorkerCount &&
                pendingGenerationWorkerOperations == 0 &&
                readyGenerationTasks == 0 &&
                pendingGenerationCompletions == 0)
            {
                admissionSaturatedIdleWorkerFrames++;
            }
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
                residency.PresentationPendingCount == 0 &&
                physicsUpdates.Count == 0;
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
            physicsUpdates.Count != 0 ||
            lastFluidPending != 0)
        {
            selection.BeginRetirement();
            residency.BeginRetirement();
            throw new TimeoutException(
                $"Headless streaming route did not drain after {frame} frames " +
                $"(pending={residency.PendingCount}, jobs={residency.MaterializationJobCount}, " +
                $"ready={ReadOptionalRuntimeCounter(residency, "ReadyGenerationTaskCount")}, " +
                $"waiting={ReadOptionalRuntimeCounter(residency, "SuspendedMaterializationCount")}, " +
                $"workerOps={ReadOptionalRuntimeCounter(residency, "PendingGenerationWorkerOperations")}, " +
                $"activeWorkers={residency.ActiveWorkerCount}, " +
                $"completions={ReadOptionalRuntimeCounter(residency, "PendingGenerationCompletions")}, " +
                $"presentation={residency.PresentationPendingCount}, " +
                $"physics={physicsUpdates.Count}, fluids={lastFluidPending}).");
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
            routeDigest = HashNumber(routeDigest, pair.Value.Combined);
        }

        return new
        {
            RouteSteps = routeSteps,
            MovementChunks = routeSteps - 1,
            FramesPerChunk,
            HorizontalRadius = horizontalRadius,
            RetentionRadius = retentionRadius,
            MaterializationWorkers = terrainAdmissionLimit,
            TerrainAdmissionLimit = ReadOptionalRuntimeCounter(
                residency, "MaxTerrainMaterializationsInFlight",
                terrainAdmissionLimit),
            MaximumWorldWorkers = maximumWorldWorkers,
            GenerationWorkerPoolSize = ReadOptionalRuntimeCounter(
                residency, "GenerationWorkerCount", maximumWorldWorkers),
            FrameCount = frame,
            TotalElapsedMilliseconds = elapsedMilliseconds,
            SelectionResultLatencyMedianFrames = Median(
                selectionLatencyFrames.Select(value => (double)value).ToArray()),
            SelectionResultLatencyP95Frames = Percentile95(
                selectionLatencyFrames.Select(value => (double)value).ToArray()),
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
            TotalWorkerSlices = totalWorkerSlices,
            AverageWorkerSlicesPerProviderActivation =
                providerActivations == 0 ? 0d :
                    (double)totalWorkerSlices / providerActivations,
            MaximumWorkerSlicesPerChunk = maximumWorkerSlicesPerChunk,
            TotalInterSliceWaitMilliseconds =
                totalInterSliceWaitMilliseconds,
            AverageInterSliceWaitMillisecondsPerProviderActivation =
                providerActivations == 0 ? 0d :
                    totalInterSliceWaitMilliseconds / providerActivations,
            MaximumInterSliceWaitMillisecondsPerChunk =
                maximumInterSliceWaitMillisecondsPerChunk,
            FramesAdmissionSaturatedWithIdleWorkers =
                admissionSaturatedIdleWorkerFrames,
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
            MaximumActiveGenerationWorkers = maximumActiveGenerationWorkers,
            MaximumPendingGenerationWorkerQueue =
                maximumPendingGenerationWorkerQueue,
            MaximumSuspendedMaterializations =
                maximumSuspendedMaterializations,
            MaximumReadyGenerationTasks = maximumReadyGenerationTasks,
            MaximumPendingGenerationWorkerOperations =
                maximumPendingGenerationWorkerOperations,
            MaximumPendingGenerationCompletions =
                maximumPendingGenerationCompletions,
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
            TotalFluidActivationSlices = totalFluidActivationSlices,
            TotalFluidActivationMilliseconds = totalFluidActivationMilliseconds,
            MaximumFluidActivationSliceMilliseconds = maximumFluidActivationSliceMilliseconds,
            TotalPhysicsChanges = totalPhysicsWakeupsProcessed,
            TotalFallingBlocksStarted = totalFallingBlocksStarted,
            TotalUnsupportedBlocksRemoved = totalUnsupportedBlocksRemoved,
            ObservedFallingEntityCount = observedFallingBlockIds.Count,
            GravityWakeupsWithAirBelowBeforeProcessing =
                gravityWakeupObservations.Count,
            GravityWakeupSamples = gravityWakeupSamples,
            FallingBlockSamples = fallingBlockSamples,
            MaterializationStages = stageCounts.Select(pair => new
            {
                Stage = pair.Key,
                AdvanceCount = pair.Value,
                WorkerMilliseconds = stageMilliseconds[pair.Key],
                MaximumWorkerMilliseconds = stageMaximumMilliseconds[pair.Key],
                MaximumCoord = new
                {
                    X = stageMaximumSamples[pair.Key].Coord.X,
                    Y = stageMaximumSamples[pair.Key].Coord.Y,
                    Z = stageMaximumSamples[pair.Key].Coord.Z,
                },
            }).ToArray(),
            SupportFallbackDiagnostics = supportFallbackDiagnostics.Snapshot(),
            RouteDigest = routeDigest,
            ActivatedChunkDigests = chunkDigests
                .OrderBy(entry => entry.Key.Y)
                .ThenBy(entry => entry.Key.Z)
                .ThenBy(entry => entry.Key.X)
                .Select(entry => new
                {
                    X = entry.Key.X,
                    Y = entry.Key.Y,
                    Z = entry.Key.Z,
                    Digest = entry.Value.Combined,
                    BlockDigest = entry.Value.Blocks,
                    FluidDigest = entry.Value.Fluids,
                    ObjectDigest = entry.Value.Objects,
                    BlockLayerDigests = chunkBlockDiagnostics[entry.Key].BlockLayerDigests,
                    BlockColumnDigests = chunkBlockDiagnostics[entry.Key].BlockColumnDigests,
                    BlockCounts = chunkBlockDiagnostics[entry.Key].BlockCounts,
                    GravityBlockCountsByLayer = chunkBlockDiagnostics[entry.Key].GravityBlockCountsByLayer,
                    GravityBlockCounts = chunkBlockDiagnostics[entry.Key].GravityBlockCounts,
                    BlockVoxelIndicesById = chunkBlockDiagnostics[entry.Key].BlockVoxelIndicesById,
                })
                .ToArray(),
            GeneratedChunkDigestsMatchOnReactivation = true,
            Notes = new[]
            {
                "Uses the real Core ChunkStreamingSelectionRuntime and ChunkResidencyRuntime with the production BiomeWorldGenerator provider.",
                "Route centers move one chunk along +X every eight simulated 60 Hz frames; selection and materialization workers run concurrently.",
                "Materialization worker limits, dispatch/result limits, retention margin and presentation reservation throughput mirror Main.cs.",
                "Presentation reservations are treated as immediate and cost-free; Godot mesh building, collision construction, GPU upload and actual frame pacing are excluded.",
                "Admission pressure is normalized across revisions and uses only physics wakeups, which this route processes. Lighting and terrain-mesh queues are observed but their workers are excluded from this Core-only route.",
                "Per-chunk content digests cover blocks, fluid state and attached Objects; block diagnostics add per-Y-layer and per-X/Z-column hashes plus authored block/gravity counts.",
                "GravityWakeupSamples snapshots queued gravity positions before each wakeup batch mutates the world; frames since chunk activation distinguish initially unsupported generation from later cascades.",
                "FallingBlockSamples records the first 64 gravity entities created by support wakeups, including the source coordinate, block ID, and the below-block state after the wakeup batch.",
            },
        };
    }

    // The A/B workflow builds this same harness against an older baseline
    // Core assembly as well as the candidate. Use the expanded constructor
    // when available, while preserving source compatibility with that baseline.
    private static int ReadMaximumWorldWorkers(int defaultValue)
    {
        var configured = Environment.GetEnvironmentVariable(
            "ASTERIA_BENCH_MAX_WORLD_WORKERS");
        if (string.IsNullOrWhiteSpace(configured))
            return defaultValue;

        if (!int.TryParse(configured, out var maximumWorkers) ||
            maximumWorkers is < 2 or > 8)
            throw new ArgumentOutOfRangeException(
                "ASTERIA_BENCH_MAX_WORLD_WORKERS",
                "The benchmark worker cap must be between 2 and 8.");

        return maximumWorkers;
    }

    private static int ReadTerrainAdmissionLimit(int defaultValue)
    {
        var configured = Environment.GetEnvironmentVariable(
            "ASTERIA_BENCH_MAX_TERRAIN_ADMISSION");
        if (string.IsNullOrWhiteSpace(configured))
            return defaultValue;

        if (!int.TryParse(configured, out var maximumTasks) ||
            maximumTasks is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(
                "ASTERIA_BENCH_MAX_TERRAIN_ADMISSION",
                "The benchmark terrain-admission cap must be between 1 and 16.");
        }

        return maximumTasks;
    }

    private static ChunkResidencySettings CreateResidencySettings(
        int terrainWorkers,
        int totalWorkers,
        WorldGameRules rules)
    {
        var sharedWorkerLimitConstructor =
            typeof(ChunkResidencySettings).GetConstructor(
            [
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(WorldGameRules),
                typeof(int?),
            ]);

        if (sharedWorkerLimitConstructor is null)
        {
            return new ChunkResidencySettings(
                maxMaterializationsInFlight: terrainWorkers,
                maxDispatchesPerFrame: terrainWorkers,
                maxResultsPerFrame: 8,
                maxEvictionsPerFrame: 2,
                gameRules: rules);
        }

        return (ChunkResidencySettings)sharedWorkerLimitConstructor.Invoke(
        [
            terrainWorkers,
            terrainWorkers,
            8,
            2,
            rules,
            (int?)totalWorkers,
        ]);
    }

    private static void AccumulateSchedulerDiagnostics(
        IReadOnlyList<ChunkResidencyActivation> activations,
        ref long totalWorkerSlices,
        ref double totalInterSliceWaitMilliseconds,
        ref double maximumInterSliceWaitMillisecondsPerChunk,
        ref int maximumWorkerSlicesPerChunk)
    {
        foreach (var activation in activations)
        {
            if (activation.Source != ChunkActivationSource.Provider)
                continue;

            var workerSliceCount = ReadOptionalRuntimeCounter(
                activation, "WorkerSliceCount");
            var interSliceWaitMilliseconds = ReadOptionalRuntimeDouble(
                activation, "InterSliceWaitMilliseconds");
            totalWorkerSlices += workerSliceCount;
            totalInterSliceWaitMilliseconds += interSliceWaitMilliseconds;
            maximumInterSliceWaitMillisecondsPerChunk = Math.Max(
                maximumInterSliceWaitMillisecondsPerChunk,
                interSliceWaitMilliseconds);
            maximumWorkerSlicesPerChunk = Math.Max(
                maximumWorkerSlicesPerChunk,
                workerSliceCount);
        }
    }

    private static void AccumulateUpdate(
        ChunkResidencyUpdate update,
        VoxelWorld world,
        BlockRegistry blocks,
        IDictionary<ChunkCoord, ChunkContentDigest> chunkDigests,
        IDictionary<ChunkCoord, ChunkBlockDiagnostics> chunkBlockDiagnostics,
        IDictionary<ChunkCoord, int> activatedChunkFrames,
        int currentFrame,
        ref int providerActivations,
        ref int archiveRestores,
        ref double totalWorkerMilliseconds,
        ref double totalTerrainMilliseconds,
        ref double totalLightingMilliseconds,
        IDictionary<string, int> stageCounts,
        IDictionary<string, double> stageMilliseconds,
        IDictionary<string, double> stageMaximumMilliseconds,
        IDictionary<string, ChunkMaterializationStageTiming> stageMaximumSamples,
        SupportFallbackDiagnostics supportFallbackDiagnostics)
    {
        foreach (var failure in update.Failures)
            throw new InvalidOperationException(
                $"Chunk {failure.Coord} failed during route materialization.",
                failure.Error);

        foreach (var timing in update.MaterializationStageTimings)
        {
            supportFallbackDiagnostics.Observe(timing);
            var name = timing.Stage.ToString();
            stageCounts[name] = stageCounts.TryGetValue(name, out var count)
                ? count + 1
                : 1;
            stageMilliseconds[name] = (
                stageMilliseconds.TryGetValue(name, out var milliseconds)
                    ? milliseconds
                    : 0d) + timing.WorkerMilliseconds;
            stageMaximumMilliseconds[name] = Math.Max(
                stageMaximumMilliseconds.TryGetValue(name, out var maximum)
                    ? maximum
                    : 0d,
                timing.WorkerMilliseconds);
            if (!stageMaximumSamples.TryGetValue(name, out var maximumSample) ||
                timing.WorkerMilliseconds > maximumSample.WorkerMilliseconds)
            {
                stageMaximumSamples[name] = timing;
            }
        }

        foreach (var activation in update.Activations)
        {
            activatedChunkFrames[activation.Coord] = currentFrame;
            if (activation.Source == ChunkActivationSource.Provider)
                providerActivations++;
            else
                archiveRestores++;

            totalWorkerMilliseconds += activation.WorkerMilliseconds;
            totalTerrainMilliseconds += activation.TerrainMilliseconds;
            totalLightingMilliseconds += activation.InitialLightingMilliseconds;

            if (!world.TryGetChunk(activation.Coord, out var chunk))
                throw new InvalidOperationException(
                    $"Activated chunk {activation.Coord} is not resident.");

            var (digest, blockDiagnostics) = ChunkDigest(
                activation.Coord, chunk, blocks);
            if (chunkDigests.TryGetValue(activation.Coord, out var prior) &&
                prior != digest)
            {
                throw new InvalidOperationException(
                    $"Chunk {activation.Coord} changed content after archive/restore.");
            }
            chunkDigests[activation.Coord] = digest;
            chunkBlockDiagnostics[activation.Coord] = blockDiagnostics;
        }
    }

    private sealed class SupportFallbackDiagnostics
    {
        private readonly SortedDictionary<string, int> _requestedColumnBuckets =
            CreateBuckets();
        private readonly SortedDictionary<string, int> _materializedColumnBuckets =
            CreateBuckets();
        private readonly SortedDictionary<string, int> _fastPathStageAdvances =
            new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, double> _fastPathStageMilliseconds =
            new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> _sparseStageAdvances =
            new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, double> _sparseStageMilliseconds =
            new(StringComparer.Ordinal);

        public int SupportOnlyJobs { get; private set; }
        public int SupportFaceFastPathJobs { get; private set; }
        public int SparseFallbackJobs { get; private set; }
        public int FullFallbackJobs { get; private set; }
        public long FallbackRequestedColumns { get; private set; }
        public long FallbackMaterializedColumns { get; private set; }

        public void Observe(ChunkMaterializationStageTiming timing)
        {
            var boxed = (object)timing;
            if (!ReadBoolean(boxed, "IsSupportOnly"))
                return;

            if (timing.Stage == ChunkMaterializationStage.TerrainSetup)
            {
                SupportOnlyJobs++;
                var isFastPath = ReadBoolean(boxed, "IsSupportFaceFastPath");
                var isSparseFallback = ReadBoolean(
                    boxed, "IsSparseSupportFallback");
                var requested = ReadInteger(
                    boxed, "SupportRequestedColumns");
                var materialized = ReadInteger(
                    boxed, "SupportMaterializationColumns");

                if (isFastPath)
                {
                    SupportFaceFastPathJobs++;
                }
                else
                {
                    FallbackRequestedColumns += requested;
                    FallbackMaterializedColumns += materialized;
                    IncrementBucket(_requestedColumnBuckets, requested);
                    IncrementBucket(_materializedColumnBuckets, materialized);
                    if (isSparseFallback)
                        SparseFallbackJobs++;
                    else
                        FullFallbackJobs++;
                }
            }

            if (ReadBoolean(boxed, "IsSupportFaceFastPath"))
            {
                var stage = timing.Stage.ToString();
                _fastPathStageAdvances[stage] =
                    _fastPathStageAdvances.TryGetValue(stage, out var count)
                        ? count + 1
                        : 1;
                _fastPathStageMilliseconds[stage] =
                    (_fastPathStageMilliseconds.TryGetValue(stage, out var elapsed)
                        ? elapsed
                        : 0d) + timing.WorkerMilliseconds;
            }

            if (ReadBoolean(boxed, "IsSparseSupportFallback"))
            {
                var stage = timing.Stage.ToString();
                _sparseStageAdvances[stage] =
                    _sparseStageAdvances.TryGetValue(stage, out var count)
                        ? count + 1
                        : 1;
                _sparseStageMilliseconds[stage] =
                    (_sparseStageMilliseconds.TryGetValue(stage, out var elapsed)
                        ? elapsed
                        : 0d) + timing.WorkerMilliseconds;
            }
        }

        public object Snapshot() => new
        {
            SupportOnlyJobs,
            SupportFaceFastPathJobs,
            SparseFallbackJobs,
            FullFallbackJobs,
            FallbackRequestedColumns,
            FallbackMaterializedColumns,
            AverageRequestedColumnsPerFallback =
                SparseFallbackJobs + FullFallbackJobs == 0
                    ? 0d
                    : (double)FallbackRequestedColumns /
                      (SparseFallbackJobs + FullFallbackJobs),
            AverageMaterializedColumnsPerFallback =
                SparseFallbackJobs + FullFallbackJobs == 0
                    ? 0d
                    : (double)FallbackMaterializedColumns /
                      (SparseFallbackJobs + FullFallbackJobs),
            RequestedColumnsByBucket = _requestedColumnBuckets,
            MaterializedColumnsByBucket = _materializedColumnBuckets,
            SupportFaceFastPathStageAdvances = _fastPathStageAdvances,
            SupportFaceFastPathStageMilliseconds = _fastPathStageMilliseconds,
            SparseFallbackStageAdvances = _sparseStageAdvances,
            SparseFallbackStageMilliseconds = _sparseStageMilliseconds,
        };

        private static SortedDictionary<string, int> CreateBuckets() =>
            new(StringComparer.Ordinal)
            {
                ["1-4"] = 0,
                ["5-16"] = 0,
                ["17-64"] = 0,
                ["65-128"] = 0,
                ["129-255"] = 0,
                ["256"] = 0,
            };

        private static bool ReadBoolean(object source, string propertyName) =>
            source.GetType().GetProperty(propertyName)?.GetValue(source) is true;

        private static int ReadInteger(object source, string propertyName) =>
            source.GetType().GetProperty(propertyName)?.GetValue(source) is int value
                ? value
                : 0;

        private static void IncrementBucket(
            IDictionary<string, int> buckets,
            int value)
        {
            var key = value switch
            {
                <= 4 => "1-4",
                <= 16 => "5-16",
                <= 64 => "17-64",
                <= 128 => "65-128",
                <= 255 => "129-255",
                _ => "256",
            };
            buckets[key]++;
        }
    }

    private static (ChunkContentDigest Content, ChunkBlockDiagnostics Diagnostics)
        ChunkDigest(ChunkCoord coord, Chunk chunk, BlockRegistry blocks)
    {
        var blockLayerDigests = new ulong[Chunk.Size];
        var blockColumnDigests = new ulong[Chunk.Area];
        Array.Fill(blockLayerDigests, FnvOffset);
        Array.Fill(blockColumnDigests, FnvOffset);
        var blockCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var gravityBlockCountsByLayer = new int[Chunk.Size];
        var gravityBlockCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var blockVoxelIndicesById = new SortedDictionary<string, List<ushort>>(
            StringComparer.Ordinal);

        var blockDigest = FnvOffset;
        blockDigest = HashNumber(blockDigest, unchecked((ulong)coord.X));
        blockDigest = HashNumber(blockDigest, unchecked((ulong)coord.Y));
        blockDigest = HashNumber(blockDigest, unchecked((ulong)coord.Z));
        blockDigest = HashNumber(blockDigest, 0xB10CUL);
        blockDigest = HashNumber(blockDigest, (ulong)chunk.NonEmptyVoxelCount);
        chunk.VisitBlockCells((x, y, z, cell) =>
        {
            blockDigest = HashNumber(blockDigest, (ulong)x);
            blockDigest = HashNumber(blockDigest, (ulong)y);
            blockDigest = HashNumber(blockDigest, (ulong)z);
            blockDigest = HashNumber(blockDigest, cell.Block.Value);

            var definition = blocks.GetDefinition(cell.Block);
            var blockId = definition.Id;
            blockCounts[blockId] = blockCounts.TryGetValue(blockId, out var count)
                ? count + 1
                : 1;
            if (!blockVoxelIndicesById.TryGetValue(blockId, out var voxelIndices))
            {
                voxelIndices = [];
                blockVoxelIndicesById.Add(blockId, voxelIndices);
            }
            var voxelIndex = checked((ushort)(
                x + Chunk.Size * (z + Chunk.Size * y)));
            voxelIndices.Add(voxelIndex);

            var columnIndex = z * Chunk.Size + x;
            blockColumnDigests[columnIndex] = HashNumber(
                HashNumber(blockColumnDigests[columnIndex], (ulong)y),
                cell.Block.Value);
            blockLayerDigests[y] = HashNumber(
                HashNumber(blockLayerDigests[y], (ulong)columnIndex),
                cell.Block.Value);

            if (definition.HasTag(BlockPhysicsCapabilities.Gravity))
            {
                gravityBlockCountsByLayer[y]++;
                gravityBlockCounts[blockId] =
                    gravityBlockCounts.TryGetValue(blockId, out var gravityCount)
                        ? gravityCount + 1
                        : 1;
            }
        });

        var blockVoxelMap = new SortedDictionary<string, ushort[]>(
            StringComparer.Ordinal);
        foreach (var (blockId, voxelIndices) in blockVoxelIndicesById)
        {
            voxelIndices.Sort();
            blockVoxelMap.Add(blockId, voxelIndices.ToArray());
        }

        var blockDiagnostics = new ChunkBlockDiagnostics(
            blockLayerDigests,
            blockColumnDigests,
            blockCounts,
            gravityBlockCountsByLayer,
            gravityBlockCounts,
            blockVoxelMap);

        // Keep fluid occupancy and simulation state in its own digest.
        var fluidDigest = FnvOffset;
        fluidDigest = HashNumber(fluidDigest, unchecked((ulong)coord.X));
        fluidDigest = HashNumber(fluidDigest, unchecked((ulong)coord.Y));
        fluidDigest = HashNumber(fluidDigest, unchecked((ulong)coord.Z));
        fluidDigest = HashNumber(fluidDigest, 0xF10DUL);
        fluidDigest = HashNumber(fluidDigest, (ulong)chunk.FluidCount);
        chunk.VisitFluidCells((x, y, z, fluid) =>
        {
            fluidDigest = HashNumber(fluidDigest, (ulong)x);
            fluidDigest = HashNumber(fluidDigest, (ulong)y);
            fluidDigest = HashNumber(fluidDigest, (ulong)z);
            fluidDigest = HashNumber(fluidDigest, fluid.Fluid.Value);
            fluidDigest = HashNumber(fluidDigest, fluid.Level);
            fluidDigest = HashNumber(fluidDigest, fluid.IsSource ? 1UL : 0UL);
            fluidDigest = HashNumber(fluidDigest, fluid.SpreadDistance);
        });

        // Objects are attached to support voxels rather than occupying block
        // cells, so include their identity and transform independently.
        var objectDigest = FnvOffset;
        objectDigest = HashNumber(objectDigest, unchecked((ulong)coord.X));
        objectDigest = HashNumber(objectDigest, unchecked((ulong)coord.Y));
        objectDigest = HashNumber(objectDigest, unchecked((ulong)coord.Z));
        objectDigest = HashNumber(objectDigest, 0x0B1EC7UL);
        objectDigest = HashNumber(objectDigest, (ulong)chunk.ObjectCount);
        chunk.VisitObjectCells((x, y, z, obj) =>
        {
            objectDigest = HashNumber(objectDigest, (ulong)x);
            objectDigest = HashNumber(objectDigest, (ulong)y);
            objectDigest = HashNumber(objectDigest, (ulong)z);
            objectDigest = HashNumber(objectDigest, (ulong)obj.Id.Length);
            foreach (var character in obj.Id)
                objectDigest = HashNumber(objectDigest, character);
            objectDigest = HashNumber(objectDigest, (ulong)obj.Face);
            objectDigest = HashNumber(objectDigest, (ulong)obj.Rotation);
            objectDigest = HashNumber(
                objectDigest, unchecked((ulong)(long)obj.Transform.OffsetX));
            objectDigest = HashNumber(
                objectDigest, unchecked((ulong)(long)obj.Transform.OffsetY));
            objectDigest = HashNumber(
                objectDigest, unchecked((ulong)(long)obj.Transform.OffsetZ));
            objectDigest = HashNumber(objectDigest, obj.Transform.ScaleX);
            objectDigest = HashNumber(objectDigest, obj.Transform.ScaleY);
            objectDigest = HashNumber(objectDigest, obj.Transform.ScaleZ);
        });

        var combined = FnvOffset;
        combined = HashNumber(combined, blockDigest);
        combined = HashNumber(combined, fluidDigest);
        combined = HashNumber(combined, objectDigest);
        return (
            new ChunkContentDigest(
                combined, blockDigest, fluidDigest, objectDigest),
            blockDiagnostics);
    }

    // The terrain A/B workflow also compiles this benchmark against an older
    // baseline revision. Read candidate-only diagnostics reflectively so the
    // same benchmark source remains buildable against both revisions.
    private static int ReadOptionalRuntimeCounter(
        object source,
        string propertyName,
        int fallback = 0) =>
        source.GetType().GetProperty(propertyName)?.GetValue(source) is int value
            ? value
            : fallback;

    private static double ReadOptionalRuntimeDouble(
        object source,
        string propertyName) =>
        source.GetType().GetProperty(propertyName)?.GetValue(source) is double value
            ? value
            : 0d;

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

    private readonly record struct GravityWakeupObservation(
        int Frame,
        int QueueDepth,
        int? ActivatedFrame,
        int? BelowActivatedFrame,
        string Block,
        WorldVoxelCoord Position,
        WorldVoxelCoord Below);

    private readonly record struct ChunkContentDigest(
        ulong Combined,
        ulong Blocks,
        ulong Fluids,
        ulong Objects);

    private readonly record struct ChunkBlockDiagnostics(
        ulong[] BlockLayerDigests,
        ulong[] BlockColumnDigests,
        SortedDictionary<string, int> BlockCounts,
        int[] GravityBlockCountsByLayer,
        SortedDictionary<string, int> GravityBlockCounts,
        SortedDictionary<string, ushort[]> BlockVoxelIndicesById);

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
