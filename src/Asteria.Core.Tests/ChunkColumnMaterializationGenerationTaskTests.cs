using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkColumnMaterializationGenerationTaskTests
{
    [Fact]
    public void ColumnResumesYieldedSectionsWhileOtherSectionsAwaitWorkerForks()
    {
        var column = new ChunkColumnCoord(4, -2);
        var orderedSection = new FakeMaterializationJob(
            new ChunkCoord(4, 0, -2),
            sliceCount: 2,
            ChunkMaterializationStage.SurfaceDecorations);
        var terrainSection = new FakeMaterializationJob(
            new ChunkCoord(4, 1, -2),
            sliceCount: 1,
            ChunkMaterializationStage.TerrainLayer);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [orderedSection, terrainSection],
            initializeLighting: _ => { });

        var initial = task.Advance([], CancellationToken.None);

        Assert.Equal(1, orderedSection.AdvanceCount);
        Assert.Equal(0, terrainSection.AdvanceCount);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, initial.Kind);
        var terrainOperation = Assert.Single(initial.WorkerOperations);

        var workerResult = terrainOperation.Execute(CancellationToken.None);
        var completion = Completion(
            column,
            terrainOperation.DependencyId,
            workerResult);

        var afterTerrain = task.Advance([completion], CancellationToken.None);

        Assert.Equal(1, terrainSection.AdvanceCount);
        Assert.Equal(1, orderedSection.AdvanceCount);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, afterTerrain.Kind);
        Assert.Equal(ChunkGenerationStatus.Terrain, afterTerrain.ProgressStatus);
        Assert.Empty(afterTerrain.WorkerOperations);

        // The aggregate status is published on the same coordinator turn that
        // processes the final section's Terrain completion, not one yield later.
        var aggregateTerrain = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, aggregateTerrain.Kind);
        Assert.Null(aggregateTerrain.ProgressStatus);
        Assert.Empty(aggregateTerrain.WorkerOperations);

        var lightOperations = 0;
        var final = CompleteColumn(
            task, column, aggregateTerrain, ref lightOperations);

        Assert.Equal(2, orderedSection.AdvanceCount);
        Assert.Equal(1, terrainSection.AdvanceCount);
        Assert.Equal(2, lightOperations);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
        var output = Assert.IsType<ChunkColumnMaterializationTaskOutput>(
            final.CompletionPayload);
        Assert.Equal(column, output.Column);
        Assert.Collection(
            output.Sections,
            section => Assert.Equal(0, section.Coord.Y),
            section => Assert.Equal(1, section.Coord.Y));
    }

    [Fact]
    public void ColumnBatchesNextTerrainForkForEveryReadyVerticalSection()
    {
        var column = new ChunkColumnCoord(6, -4);
        var lower = new FakeMaterializationJob(
            new ChunkCoord(6, 0, -4),
            sliceCount: 2,
            ChunkMaterializationStage.TerrainLayer);
        var upper = new FakeMaterializationJob(
            new ChunkCoord(6, 1, -4),
            sliceCount: 2,
            ChunkMaterializationStage.TerrainLayer);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [lower, upper],
            initializeLighting: _ => { });

        var firstTerrainBatch = task.Advance([], CancellationToken.None);
        Assert.Equal(2, firstTerrainBatch.WorkerOperations.Length);

        var firstCompletions = firstTerrainBatch.WorkerOperations
            .Select(operation => Completion(
                column,
                operation.DependencyId,
                operation.Execute(CancellationToken.None)))
            .ToArray();
        var terrainProgress = task.Advance(
            firstCompletions, CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, terrainProgress.Kind);
        Assert.Null(terrainProgress.ProgressStatus);
        Assert.Equal(1, lower.AdvanceCount);
        Assert.Equal(1, upper.AdvanceCount);

        var secondTerrainBatch = task.Advance([], CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, secondTerrainBatch.Kind);
        Assert.Equal(2, secondTerrainBatch.WorkerOperations.Length);
        Assert.All(
            secondTerrainBatch.WorkerOperations,
            operation => Assert.Equal(
                ChunkGenerationExecutionPolicy.SharedWorkerPool,
                operation.ExecutionPolicy));
    }

    [Fact]
    public void ColumnBatchesIndependentSurfaceDecorationStagesForEveryReadyVerticalSection()
    {
        var column = new ChunkColumnCoord(8, -5);
        var lower = new FakeMaterializationJob(
            new ChunkCoord(8, 0, -5),
            sliceCount: 1,
            ChunkMaterializationStage.SurfaceDecorations);
        var upper = new FakeMaterializationJob(
            new ChunkCoord(8, 1, -5),
            sliceCount: 1,
            ChunkMaterializationStage.SurfaceDecorations);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [lower, upper],
            initializeLighting: _ => { });

        var advance = task.Advance([], CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, advance.Kind);
        Assert.Equal(2, advance.WorkerOperations.Length);
        Assert.All(
            advance.WorkerOperations,
            operation => Assert.Equal(
                ChunkGenerationExecutionPolicy.SharedWorkerPool,
                operation.ExecutionPolicy));
        Assert.Equal(0, lower.AdvanceCount);
        Assert.Equal(0, upper.AdvanceCount);
    }

    [Fact]
    public void GravitySupportWaitsForLowerSectionTerrainStatusFromThePlan()
    {
        var column = new ChunkColumnCoord(7, -4);
        var lower = new FakeMaterializationJob(
            new ChunkCoord(7, 0, -4),
            sliceCount: 1,
            ChunkMaterializationStage.TerrainLayer);
        var upper = new FakeMaterializationJob(
            new ChunkCoord(7, 1, -4),
            sliceCount: 2,
            ChunkMaterializationStage.GravitySupport,
            pendingDependencyCoord: lower.Coord);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [lower, upper],
            initializeLighting: _ => { });

        var initial = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, initial.Kind);
        Assert.Equal(0, upper.AdvanceCount);
        var terrainOperation = Assert.Single(initial.WorkerOperations);
        var terrainCompletion = Completion(
            column,
            terrainOperation.DependencyId,
            terrainOperation.Execute(CancellationToken.None));

        var afterTerrain = task.Advance(
            [terrainCompletion], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, afterTerrain.Kind);
        Assert.Equal(ChunkGenerationStatus.Terrain, afterTerrain.ProgressStatus);
        Assert.Equal(0, upper.AdvanceCount);

        var lightOperations = 0;
        var final = CompleteColumn(
            task, column, afterTerrain, ref lightOperations);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
        Assert.Equal(1, lower.AdvanceCount);
        Assert.Equal(2, upper.AdvanceCount);
        Assert.NotNull(upper.CreatedDependency);
        Assert.Equal(0, upper.CreatedDependency!.AdvanceCount);
        Assert.True(upper.CreatedDependency.IsDisposed);
        Assert.Same(lower.Result, upper.ResolvedDependency);
        Assert.Equal(2, lightOperations);
    }

    [Fact]
    public void ColumnTaskParksAtAchievedStatusAndResumesAfterTargetUpgrade()
    {
        var column = new ChunkColumnCoord(12, -3);
        var coord = new ChunkCoord(12, 0, -3);
        var job = new FakeMaterializationJob(
            coord,
            sliceCount: 1,
            ChunkMaterializationStage.TerrainSetup);
        var lightOperations = 0;
        var lightingInitializationCalls = 0;
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [job],
            _ => lightingInitializationCalls++,
            targetStatus: ChunkGenerationStatus.Biomes);

        var initial = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, initial.Kind);
        var terrainSetup = Assert.Single(initial.WorkerOperations);
        var setupCompletion = Completion(
            column,
            terrainSetup.DependencyId,
            terrainSetup.Execute(CancellationToken.None));

        var biomes = task.Advance([setupCompletion], CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForFutures, biomes.Kind);
        Assert.Equal(ChunkGenerationStatus.Biomes, biomes.ProgressStatus);
        var upgrade = Assert.Single(biomes.FutureOperations);
        Assert.Equal(1, job.AdvanceCount);
        Assert.Equal(ChunkGenerationStatus.Biomes, task.TargetStatus);
        Assert.Equal(0, lightingInitializationCalls);

        Assert.True(task.TryUpgradeTargetStatus(ChunkGenerationStatus.Full));
        var upgradeCompletion = Completion(
            column,
            upgrade.DependencyId,
            upgrade.Future.GetAwaiter().GetResult());
        var resumed = task.Advance(
            [upgradeCompletion],
            CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, resumed.Kind);
        Assert.Equal(ChunkGenerationStatus.Full, task.TargetStatus);

        var final = CompleteColumn(
            task, column, resumed, ref lightOperations);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
        Assert.Equal(1, lightOperations);
        Assert.Equal(1, lightingInitializationCalls);
    }

    [Fact]
    public void ColumnSharesCrossColumnHolderFutureAcrossSectionConsumers()
    {
        var column = new ChunkColumnCoord(4, -2);
        var lower = new FakeMaterializationJob(
            new ChunkCoord(4, 0, -2),
            sliceCount: 1,
            ChunkMaterializationStage.SurfaceDecorations);
        var upper = new FakeMaterializationJob(
            new ChunkCoord(4, 1, -2),
            sliceCount: 1,
            ChunkMaterializationStage.SurfaceDecorations);
        var externalSection = new ChunkCoord(40, 0, 40);
        var requirement = new ChunkGenerationSectionRequirement(
            externalSection,
            ChunkGenerationStepId.ColumnBiomeData,
            ChunkGenerationStatus.Biomes);
        var source = new TaskCompletionSource<ChunkGenerationFutureResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var futureRequests = 0;
        var producerRequests = 0;
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [lower, upper],
            initializeLighting: _ => { },
            getHolderStatusRequirements: (_, _) => [requirement],
            getHolderStatusFuture: _ =>
            {
                futureRequests++;
                return source.Task;
            },
            requestHolderProducer: _ => producerRequests++);

        var initial = task.Advance([], CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForFutures, initial.Kind);
        var operation = Assert.Single(initial.FutureOperations);
        Assert.Equal(0, lower.AdvanceCount);
        Assert.Equal(0, upper.AdvanceCount);
        Assert.Equal(1, futureRequests);

        operation.RequestProducer?.Invoke();
        Assert.Equal(1, producerRequests);
        source.SetResult(ChunkGenerationFutureResult.Success(
            ChunkGenerationStatus.Biomes,
            revision: 5));

        var futureCompletion = Completion(
            column,
            operation.DependencyId,
            source.Task.Result);
        var resumed = task.Advance(
            [futureCompletion],
            CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, resumed.Kind);
        Assert.Equal(0, lower.AdvanceCount);
        Assert.Equal(0, upper.AdvanceCount);

        var lightOperations = 0;
        var final = CompleteColumn(
            task,
            column,
            task.Advance([], CancellationToken.None),
            ref lightOperations);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
        Assert.Equal(1, lower.AdvanceCount);
        Assert.Equal(1, upper.AdvanceCount);
        Assert.Equal(1, futureRequests);
        Assert.Equal(1, producerRequests);
        Assert.Equal(2, lightOperations);
    }

    [Fact]
    public void SectionDependencyReusesCompletedLowerSectionFuture()
    {
        var column = new ChunkColumnCoord(3, -1);
        var lower = new FakeMaterializationJob(
            new ChunkCoord(3, 0, -1),
            sliceCount: 1,
            ChunkMaterializationStage.TerrainLayer);
        var upper = new FakeMaterializationJob(
            new ChunkCoord(3, 1, -1),
            sliceCount: 2,
            ChunkMaterializationStage.GravitySupport,
            pendingDependencyCoord: lower.Coord);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [lower, upper],
            initializeLighting: _ => { });

        var lightOperations = 0;
        var final = CompleteColumn(
            task,
            column,
            task.Advance([], CancellationToken.None),
            ref lightOperations);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
        Assert.NotNull(upper.CreatedDependency);
        Assert.Equal(0, upper.CreatedDependency!.AdvanceCount);
        Assert.True(upper.CreatedDependency.IsDisposed);
        Assert.Same(lower.Result, upper.ResolvedDependency);
        Assert.Equal(2, upper.AdvanceCount);
        Assert.Equal(1, lower.AdvanceCount);
    }

    [Fact]
    public void ColumnCarriesExactVerticalSectionStatusTransitions()
    {
        var column = new ChunkColumnCoord(8, -5);
        var lower = new FakeMaterializationJob(
            new ChunkCoord(8, 2, -5),
            sliceCount: 1,
            ChunkMaterializationStage.TerrainLayer);
        var upper = new FakeMaterializationJob(
            new ChunkCoord(8, 3, -5),
            sliceCount: 2,
            ChunkMaterializationStage.TerrainLayer);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [lower, upper],
            initializeLighting: _ => { });

        var firstBatch = task.Advance([], CancellationToken.None);
        var completions = firstBatch.WorkerOperations
            .Select(operation => Completion(
                column,
                operation.DependencyId,
                operation.Execute(CancellationToken.None)))
            .ToArray();

        var afterFirstSections = task.Advance(
            completions, CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, afterFirstSections.Kind);
        Assert.Null(afterFirstSections.ProgressStatus);
        Assert.Contains(
            new ChunkSectionGenerationProgress(
                lower.Coord, ChunkGenerationStatus.Terrain),
            afterFirstSections.SectionProgress);
        Assert.DoesNotContain(
            afterFirstSections.SectionProgress,
            progress => progress.Coord == upper.Coord);
    }

    [Fact]
    public void ColumnSchedulesOutOfBatchGravityDependencyAsDynamicSectionTask()
    {
        var column = new ChunkColumnCoord(5, -2);
        var lower = new ChunkCoord(5, 0, -2);
        var upper = new FakeMaterializationJob(
            new ChunkCoord(5, 1, -2),
            sliceCount: 2,
            ChunkMaterializationStage.GravitySupport,
            pendingDependencyCoord: lower);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [upper],
            initializeLighting: _ => { });

        var lightOperations = 0;
        var final = CompleteColumn(
            task,
            column,
            task.Advance([], CancellationToken.None),
            ref lightOperations);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
        var output = Assert.IsType<ChunkColumnMaterializationTaskOutput>(
            final.CompletionPayload);
        Assert.Collection(
            output.Sections,
            section => Assert.Equal(new ChunkCoord(5, 1, -2), section.Coord));
        Assert.Equal(2, upper.AdvanceCount);
        Assert.NotNull(upper.CreatedDependency);
        Assert.Equal(1, upper.CreatedDependency!.AdvanceCount);
        Assert.True(upper.CreatedDependency.IsDisposed);
        Assert.Same(upper.CreatedDependency.Result, upper.ResolvedDependency);
        Assert.Equal(1, lightOperations);
    }

    [Fact]
    public void MultipleSectionsShareOneDynamicGravityDependencyFuture()
    {
        var column = new ChunkColumnCoord(5, -2);
        var dependency = new ChunkCoord(5, 0, -2);
        var upper = new FakeMaterializationJob(
            new ChunkCoord(5, 1, -2),
            sliceCount: 2,
            ChunkMaterializationStage.GravitySupport,
            pendingDependencyCoord: dependency);
        var top = new FakeMaterializationJob(
            new ChunkCoord(5, 2, -2),
            sliceCount: 2,
            ChunkMaterializationStage.GravitySupport,
            pendingDependencyCoord: dependency);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [upper, top],
            initializeLighting: _ => { });

        var lightOperations = 0;
        var final = CompleteColumn(
            task,
            column,
            task.Advance([], CancellationToken.None),
            ref lightOperations);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
        Assert.Equal(2, upper.AdvanceCount);
        Assert.Equal(2, top.AdvanceCount);
        Assert.NotNull(upper.CreatedDependency);
        Assert.NotNull(top.CreatedDependency);
        Assert.Equal(1,
            upper.CreatedDependency!.AdvanceCount + top.CreatedDependency!.AdvanceCount);
        Assert.True(upper.CreatedDependency.IsDisposed);
        Assert.True(top.CreatedDependency.IsDisposed);
        Assert.NotNull(upper.ResolvedDependency);
        Assert.Same(upper.ResolvedDependency, top.ResolvedDependency);
        Assert.Equal(2, lightOperations);

        var output = Assert.IsType<ChunkColumnMaterializationTaskOutput>(
            final.CompletionPayload);
        Assert.Equal(2, output.Sections.Length);
        Assert.DoesNotContain(output.Sections, section => section.Coord == dependency);
    }

    [Fact]
    public void OrderedMutationAdvancesRotateAcrossReadyVerticalSections()
    {
        var column = new ChunkColumnCoord(9, -6);
        var lower = new FakeMaterializationJob(
            new ChunkCoord(9, 1, -6),
            sliceCount: 3,
            ChunkMaterializationStage.SurfaceDecorations);
        var upper = new FakeMaterializationJob(
            new ChunkCoord(9, 2, -6),
            sliceCount: 3,
            ChunkMaterializationStage.SurfaceDecorations);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [lower, upper],
            initializeLighting: _ => { });

        var first = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, first.Kind);
        Assert.Equal(1, lower.AdvanceCount);
        Assert.Equal(0, upper.AdvanceCount);

        var second = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, second.Kind);
        Assert.Equal(1, lower.AdvanceCount);
        Assert.Equal(1, upper.AdvanceCount);

        var third = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, third.Kind);
        Assert.Equal(2, lower.AdvanceCount);
        Assert.Equal(1, upper.AdvanceCount);
    }

    [Fact]
    public void ColumnPreservesLightExecutorPolicyForEverySection()
    {
        var column = new ChunkColumnCoord(5, -3);
        var firstJob = new FakeMaterializationJob(
            new ChunkCoord(5, 0, -3),
            sliceCount: 1,
            ChunkMaterializationStage.TerrainLayer);
        var secondJob = new FakeMaterializationJob(
            new ChunkCoord(5, 1, -3),
            sliceCount: 1,
            ChunkMaterializationStage.TerrainLayer);
        using var task = new ChunkColumnMaterializationGenerationTask(
            column,
            [firstJob, secondJob],
            initializeLighting: _ => { });

        var terrain = task.Advance([], CancellationToken.None);
        Assert.Equal(2, terrain.WorkerOperations.Length);

        var lightOperations = 0;
        var final = CompleteColumn(task, column, terrain, ref lightOperations);

        Assert.Equal(2, lightOperations);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
    }

    private static ChunkGenerationTaskAdvanceResult CompleteColumn(
        ChunkColumnMaterializationGenerationTask task,
        ChunkColumnCoord column,
        ChunkGenerationTaskAdvanceResult advance,
        ref int lightOperations)
    {
        while (advance.Kind != ChunkGenerationTaskAdvanceKind.Completed)
        {
            if (advance.Kind == ChunkGenerationTaskAdvanceKind.Yield)
            {
                advance = task.Advance([], CancellationToken.None);
                continue;
            }

            if (advance.Kind != ChunkGenerationTaskAdvanceKind.WaitForWorkers)
                throw new InvalidOperationException(
                    $"Unexpected column advance kind: {advance.Kind}");

            var completions = new List<ChunkGenerationDependencyCompletion>(
                advance.WorkerOperations.Length);
            foreach (var operation in advance.WorkerOperations)
            {
                if (operation.ExecutionPolicy ==
                    ChunkGenerationExecutionPolicy.LightExecutor)
                    lightOperations++;

                completions.Add(Completion(
                    column,
                    operation.DependencyId,
                    operation.Execute(CancellationToken.None)));
            }

            advance = task.Advance(completions, CancellationToken.None);
        }

        return advance;
    }

    private static ChunkGenerationDependencyCompletion Completion(
        ChunkColumnCoord column,
        ulong dependencyId,
        object? result) =>
        new(
            new ChunkGenerationTaskInstance(
                new ChunkGenerationTaskKey(
                    column,
                    ChunkGenerationStepId.SectionReady),
                Generation: 1),
            WaitToken: 1,
            dependencyId,
            result);

    private sealed class FakeMaterializationJob :
        IChunkMaterializationJob,
        IChunkMaterializationDependencyReceiver
    {
        private readonly int _sliceCount;
        private readonly ChunkMaterializationStage _stage;
        private readonly ChunkCoord? _pendingDependencyCoord;
        private IChunkMaterializationJob? _pendingDependency;

        public FakeMaterializationJob(
            ChunkCoord coord,
            int sliceCount,
            ChunkMaterializationStage stage,
            ChunkCoord? pendingDependencyCoord = null)
        {
            Coord = coord;
            _sliceCount = sliceCount;
            _stage = stage;
            _pendingDependencyCoord = pendingDependencyCoord;
            Result = new Chunk();
        }

        public ChunkCoord Coord { get; }

        public ChunkMaterializationStage Stage =>
            IsComplete ? ChunkMaterializationStage.Complete : _stage;

        public bool IsComplete => AdvanceCount >= _sliceCount && _pendingDependency is null;

        public Chunk? Result { get; }

        public IChunkMaterializationJob? PendingDependency => _pendingDependency;

        public FakeMaterializationJob? CreatedDependency { get; private set; }

        public Chunk? ResolvedDependency { get; private set; }

        public int AdvanceCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Advance()
        {
            if (IsComplete)
                throw new InvalidOperationException("The fake materialization job is already complete.");
            AdvanceCount++;
            if (AdvanceCount == 1 && _pendingDependencyCoord is { } dependencyCoord)
            {
                CreatedDependency = new FakeMaterializationJob(
                    dependencyCoord,
                    sliceCount: 1,
                    _stage);
                _pendingDependency = CreatedDependency;
            }
        }

        public void ResolveDependency(ChunkCoord dependencyCoord, Chunk completedChunk)
        {
            ArgumentNullException.ThrowIfNull(completedChunk);
            if (_pendingDependency is null || _pendingDependency.Coord != dependencyCoord)
                throw new InvalidOperationException("The fake job received an unexpected dependency.");
            ResolvedDependency = completedChunk;
            _pendingDependency.Dispose();
            _pendingDependency = null;
        }

        public void Dispose()
        {
            IsDisposed = true;
            _pendingDependency?.Dispose();
            _pendingDependency = null;
        }
    }
}
