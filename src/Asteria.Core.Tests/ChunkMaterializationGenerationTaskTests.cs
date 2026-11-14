using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkMaterializationGenerationTaskTests
{
    [Fact]
    public void WorkerForkAdvancesExactlyOneTerrainQuantum()
    {
        var job = new FakeMaterializationJob(sliceCount: 100);
        var lightingCalls = 0;
        using var task = new ChunkMaterializationGenerationTask(
            job, _ => lightingCalls++);

        var advance = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, advance.Kind);
        Assert.Single(advance.WorkerOperations);
        Assert.Equal(0, job.AdvanceCount);

        advance = RunSlice(task, advance, job, waitToken: 1);

        Assert.Equal(1, job.AdvanceCount);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, advance.Kind);
        Assert.Equal(ChunkGenerationStatus.Terrain, advance.ProgressStatus);
        Assert.Empty(advance.WorkerOperations);
        Assert.Equal(0, lightingCalls);

        var next = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, next.Kind);
        Assert.Single(next.WorkerOperations);
    }

    [Fact]
    public void MaterializationContinuesThroughOneWorkerForkPerTerrainQuantum()
    {
        var job = new FakeMaterializationJob(sliceCount: 100);
        var lightingCalls = 0;
        using var task = new ChunkMaterializationGenerationTask(
            job, _ => lightingCalls++);

        var advance = task.Advance([], CancellationToken.None);
        advance = CompleteTask(task, advance, job);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, advance.Kind);
        Assert.Equal(100, job.AdvanceCount);
        Assert.Equal(1, lightingCalls);
        var output = Assert.IsType<ChunkMaterializationTaskOutput>(
            advance.CompletionPayload);
        Assert.Equal(100, output.StageTimings.Length);
        Assert.Equal(101, output.WorkerSliceCount);
        Assert.True(output.InterSliceWaitMilliseconds >= 0d);
    }

    [Fact]
    public void OrderedMutationStageRunsInlineOnSerialContinuation()
    {
        var job = new FakeMaterializationJob(
            sliceCount: 2,
            stage: ChunkMaterializationStage.SurfaceDecorations);
        using var task = new ChunkMaterializationGenerationTask(job, _ => { });

        var first = task.Advance([], CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, first.Kind);
        Assert.Empty(first.WorkerOperations);
        Assert.Equal(1, job.AdvanceCount);

        var featureProgress = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, featureProgress.Kind);
        Assert.Equal(2, job.AdvanceCount);

        var lighting = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, lighting.Kind);
        Assert.Equal(
            ChunkGenerationExecutionPolicy.LightExecutor,
            Assert.Single(lighting.WorkerOperations).ExecutionPolicy);

        var final = RunSlice(task, lighting, job, waitToken: 1);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
    }

    [Fact]
    public void FinalSliceReturnsDetachedChunkAndStageMetrics()
    {
        var job = new FakeMaterializationJob(sliceCount: 2);
        var lightingCalls = 0;
        using var task = new ChunkMaterializationGenerationTask(
            job, _ => lightingCalls++);

        var first = task.Advance([], CancellationToken.None);
        var second = RunSlice(task, first, job, 1);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, second.Kind);
        Assert.Equal(ChunkGenerationStatus.Terrain, second.ProgressStatus);
        var final = CompleteTask(task, second, job);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
        Assert.Empty(final.WorkerOperations);
        var output = Assert.IsType<ChunkMaterializationTaskOutput>(
            final.CompletionPayload);
        Assert.Same(job.Result, output.Chunk);
        Assert.Equal(job.Coord, output.Coord);
        Assert.Equal(2, output.StageTimings.Length);
        Assert.Equal(1, lightingCalls);
        Assert.True(output.WorkerMilliseconds >= output.InitialLightingMilliseconds);
    }

    [Fact]
    public void InitialLightingIsScheduledOnTheSeparateLightExecutor()
    {
        var job = new FakeMaterializationJob(sliceCount: 1);
        var lightingCalls = 0;
        using var task = new ChunkMaterializationGenerationTask(
            job, _ => lightingCalls++);

        var terrainAdvance = task.Advance([], CancellationToken.None);
        var terrainOperation = Assert.Single(terrainAdvance.WorkerOperations);
        Assert.Equal(
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            terrainOperation.ExecutionPolicy);

        var terrainResult = terrainOperation.Execute(CancellationToken.None);
        var terrainProgress = task.Advance(
        [
            new ChunkGenerationDependencyCompletion(
                TaskInstance(job),
                WaitToken: 1,
                terrainOperation.DependencyId,
                terrainResult),
        ], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, terrainProgress.Kind);
        Assert.Equal(ChunkGenerationStatus.Terrain, terrainProgress.ProgressStatus);

        var featureProgress = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, featureProgress.Kind);
        Assert.Equal(ChunkGenerationStatus.Features, featureProgress.ProgressStatus);

        var lightingAdvance = task.Advance([], CancellationToken.None);
        var lightingOperation = Assert.Single(lightingAdvance.WorkerOperations);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, lightingAdvance.Kind);
        Assert.Equal(
            ChunkGenerationExecutionPolicy.LightExecutor,
            lightingOperation.ExecutionPolicy);
        Assert.Equal(0, lightingCalls);

        var lightingResult = lightingOperation.Execute(CancellationToken.None);
        var completed = task.Advance(
        [
            new ChunkGenerationDependencyCompletion(
                TaskInstance(job),
                WaitToken: 2,
                lightingOperation.DependencyId,
                lightingResult),
        ], CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, completed.Kind);
        Assert.Equal(1, lightingCalls);
        var output = Assert.IsType<ChunkMaterializationTaskOutput>(
            completed.CompletionPayload);
        Assert.True(output.InitialLightingMilliseconds >= 0d);
    }

    [Fact]
    public void CompletedSliceCannotBeAdvancedAgain()
    {
        var job = new FakeMaterializationJob(sliceCount: 1);
        using var task = new ChunkMaterializationGenerationTask(job, _ => { });

        var first = task.Advance([], CancellationToken.None);
        var final = CompleteTask(task, first, job);

        Assert.Throws<InvalidOperationException>(
            () => task.Advance([], CancellationToken.None));
        Assert.Equal(
            ChunkGenerationTaskAdvanceKind.Completed,
            final.Kind);
    }

    [Fact]
    public void DisposeReleasesUnderlyingMaterializationJob()
    {
        var job = new FakeMaterializationJob(sliceCount: 2);
        var task = new ChunkMaterializationGenerationTask(job, _ => { });

        task.Dispose();
        task.Dispose();

        Assert.True(job.IsDisposed);
        Assert.Throws<ObjectDisposedException>(
            () => task.Advance([], CancellationToken.None));
    }

    private static ChunkGenerationTaskInstance TaskInstance(
        FakeMaterializationJob job) =>
        new(
            new ChunkGenerationTaskKey(
                new ChunkColumnCoord(job.Coord.X, job.Coord.Z),
                ChunkGenerationStepId.SectionReady),
            Generation: 1);

    private static ChunkGenerationTaskAdvanceResult CompleteTask(
        ChunkMaterializationGenerationTask task,
        ChunkGenerationTaskAdvanceResult advance,
        FakeMaterializationJob job)
    {
        ulong waitToken = 1;
        while (advance.Kind != ChunkGenerationTaskAdvanceKind.Completed)
        {
            if (advance.Kind == ChunkGenerationTaskAdvanceKind.Yield)
            {
                advance = task.Advance([], CancellationToken.None);
                continue;
            }

            if (advance.Kind != ChunkGenerationTaskAdvanceKind.WaitForWorkers)
                throw new InvalidOperationException(
                    $"Unexpected materialization advance kind: {advance.Kind}");

            advance = RunSlice(task, advance, job, waitToken++);
        }

        return advance;
    }

    private static ChunkGenerationTaskAdvanceResult RunSlice(
        ChunkMaterializationGenerationTask task,
        ChunkGenerationTaskAdvanceResult advance,
        FakeMaterializationJob job,
        ulong waitToken)
    {
        var operation = Assert.Single(advance.WorkerOperations);
        var result = operation.Execute(CancellationToken.None);
        var completion = new ChunkGenerationDependencyCompletion(
            new ChunkGenerationTaskInstance(
                new ChunkGenerationTaskKey(
                    new ChunkColumnCoord(job.Coord.X, job.Coord.Z),
                    ChunkGenerationStepId.SectionReady),
                Generation: 1),
            waitToken,
            operation.DependencyId,
            result);
        return task.Advance([completion], CancellationToken.None);
    }

    private sealed class FakeMaterializationJob : IChunkMaterializationJob
    {
        private readonly int _sliceCount;

        private readonly ChunkMaterializationStage _stage;

        public FakeMaterializationJob(
            int sliceCount,
            ChunkMaterializationStage stage = ChunkMaterializationStage.TerrainLayer)
        {
            _sliceCount = sliceCount;
            _stage = stage;
            Result = new Chunk();
        }

        public ChunkCoord Coord { get; } = new(2, 1, -3);

        public ChunkMaterializationStage Stage =>
            IsComplete
                ? ChunkMaterializationStage.Complete
                : _stage;

        public bool IsComplete => AdvanceCount >= _sliceCount;

        public Chunk? Result { get; }

        public int AdvanceCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Advance()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(FakeMaterializationJob));
            if (IsComplete)
                throw new InvalidOperationException("already complete");
            AdvanceCount++;
        }

        public void Dispose() => IsDisposed = true;
    }
}
