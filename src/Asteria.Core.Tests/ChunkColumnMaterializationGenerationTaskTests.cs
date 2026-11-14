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
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, afterTerrain.Kind);
        var terrainLighting = Assert.Single(afterTerrain.WorkerOperations);
        Assert.Equal(
            ChunkGenerationExecutionPolicy.LightExecutor,
            terrainLighting.ExecutionPolicy);

        var afterTerrainLighting = task.Advance(
        [
            Completion(
                column,
                terrainLighting.DependencyId,
                terrainLighting.Execute(CancellationToken.None)),
        ], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.Yield, afterTerrainLighting.Kind);
        Assert.Empty(afterTerrainLighting.WorkerOperations);

        var orderedLighting = task.Advance([], CancellationToken.None);
        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, orderedLighting.Kind);
        var orderedLightOperation = Assert.Single(orderedLighting.WorkerOperations);
        Assert.Equal(
            ChunkGenerationExecutionPolicy.LightExecutor,
            orderedLightOperation.ExecutionPolicy);

        var final = task.Advance(
        [
            Completion(
                column,
                orderedLightOperation.DependencyId,
                orderedLightOperation.Execute(CancellationToken.None)),
        ], CancellationToken.None);

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

        var terrainCompletions = terrain.WorkerOperations
            .Select(operation => Completion(
                column,
                operation.DependencyId,
                operation.Execute(CancellationToken.None)))
            .ToArray();
        var lighting = task.Advance(terrainCompletions, CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, lighting.Kind);
        Assert.Equal(2, lighting.WorkerOperations.Length);
        Assert.All(
            lighting.WorkerOperations,
            operation => Assert.Equal(
                ChunkGenerationExecutionPolicy.LightExecutor,
                operation.ExecutionPolicy));
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
