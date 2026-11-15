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
        Assert.Empty(afterTerrain.WorkerOperations);

        var aggregateTerrain = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, aggregateTerrain.Kind);
        Assert.Equal(ChunkGenerationStatus.Terrain, aggregateTerrain.ProgressStatus);
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
