using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkMaterializationGenerationTaskTests
{
    [Fact]
    public void WorkerOperationBatchesSeveralAdvancesWithinBound()
    {
        var job = new FakeMaterializationJob(sliceCount: 100);
        var lightingCalls = 0;
        using var task = new ChunkMaterializationGenerationTask(
            job, _ => lightingCalls++);

        var advance = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, advance.Kind);
        Assert.Single(advance.WorkerOperations);
        Assert.Equal(0, job.AdvanceCount);

        ulong waitToken = 1;
        while (true)
        {
            var previousAdvanceCount = job.AdvanceCount;
            advance = RunSlice(task, advance, job, waitToken++);
            var advancesInBatch = job.AdvanceCount - previousAdvanceCount;

            Assert.InRange(
                advancesInBatch,
                1,
                ChunkMaterializationGenerationTask.MaximumAdvancesPerWorkerOperation);

            if (advance.Kind == ChunkGenerationTaskAdvanceKind.Completed)
                break;

            Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, advance.Kind);
            Assert.Single(advance.WorkerOperations);
            Assert.Equal(0, lightingCalls);
        }

        Assert.Equal(100, job.AdvanceCount);
        Assert.Equal(1, lightingCalls);
        var output = Assert.IsType<ChunkMaterializationTaskOutput>(
            advance.CompletionPayload);
        Assert.Equal(100, output.StageTimings.Length);
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
        var final = RunSlice(task, second, job, 2);

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
    public void CompletedSliceCannotBeAdvancedAgain()
    {
        var job = new FakeMaterializationJob(sliceCount: 1);
        using var task = new ChunkMaterializationGenerationTask(job, _ => { });

        var first = task.Advance([], CancellationToken.None);
        var final = RunSlice(task, first, job, 1);

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

        public FakeMaterializationJob(int sliceCount)
        {
            _sliceCount = sliceCount;
            Result = new Chunk();
        }

        public ChunkCoord Coord { get; } = new(2, 1, -3);

        public ChunkMaterializationStage Stage =>
            IsComplete
                ? ChunkMaterializationStage.Complete
                : (ChunkMaterializationStage)((byte)ChunkMaterializationStage.TerrainSetup + AdvanceCount);

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
