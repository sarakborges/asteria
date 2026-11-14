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

        var final = task.Advance([], CancellationToken.None);

        Assert.Equal(2, orderedSection.AdvanceCount);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, final.Kind);
        var output = Assert.IsType<ChunkColumnMaterializationTaskOutput>(
            final.CompletionPayload);
        Assert.Equal(column, output.Column);
        Assert.Collection(
            output.Sections,
            section => Assert.Equal(0, section.Coord.Y),
            section => Assert.Equal(1, section.Coord.Y));
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

    private sealed class FakeMaterializationJob : IChunkMaterializationJob
    {
        private readonly int _sliceCount;
        private readonly ChunkMaterializationStage _stage;

        public FakeMaterializationJob(
            ChunkCoord coord,
            int sliceCount,
            ChunkMaterializationStage stage)
        {
            Coord = coord;
            _sliceCount = sliceCount;
            _stage = stage;
            Result = new Chunk();
        }

        public ChunkCoord Coord { get; }

        public ChunkMaterializationStage Stage =>
            IsComplete ? ChunkMaterializationStage.Complete : _stage;

        public bool IsComplete => AdvanceCount >= _sliceCount;

        public Chunk? Result { get; }

        public int AdvanceCount { get; private set; }

        public void Advance()
        {
            if (IsComplete)
                throw new InvalidOperationException("The fake materialization job is already complete.");
            AdvanceCount++;
        }

        public void Dispose()
        {
        }
    }
}
