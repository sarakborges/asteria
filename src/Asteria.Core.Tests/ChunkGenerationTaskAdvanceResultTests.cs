using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationTaskAdvanceResultTests
{
    [Fact]
    public void CompletedAndYieldResultsDoNotDeclareWorkerOperations()
    {
        Assert.Equal(
            ChunkGenerationTaskAdvanceKind.Completed,
            ChunkGenerationTaskAdvanceResult.Completed.Kind);
        Assert.Empty(ChunkGenerationTaskAdvanceResult.Completed.WorkerOperations);

        Assert.Equal(
            ChunkGenerationTaskAdvanceKind.Yield,
            ChunkGenerationTaskAdvanceResult.Yield.Kind);
        Assert.Empty(ChunkGenerationTaskAdvanceResult.Yield.WorkerOperations);
    }

    [Fact]
    public void WaitResultPreservesWorkerOperationOrder()
    {
        var first = new ChunkGenerationWorkerOperation(11, _ => "first");
        var second = new ChunkGenerationWorkerOperation(12, _ => "second");

        var result = ChunkGenerationTaskAdvanceResult.WaitForWorkers(
            [first, second]);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForWorkers, result.Kind);
        Assert.Equal([11UL, 12UL], result.WorkerOperations
            .Select(operation => operation.DependencyId));
    }

    [Fact]
    public void WaitResultRequiresAtLeastOneWorkerOperation()
    {
        Assert.Throws<ArgumentException>(
            () => ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                Array.Empty<ChunkGenerationWorkerOperation>()));
    }

    [Fact]
    public void WorkerOperationRejectsZeroDependencyIdAndNullDelegate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkGenerationWorkerOperation(0, _ => null));
        Assert.Throws<ArgumentNullException>(
            () => new ChunkGenerationWorkerOperation(
                1, (Func<CancellationToken, object?>)null!));
    }

    [Fact]
    public void WaitResultRejectsDuplicateDependencyIds()
    {
        Assert.Throws<ArgumentException>(
            () => ChunkGenerationTaskAdvanceResult.WaitForWorkers(
            [
                new ChunkGenerationWorkerOperation(7, _ => "a"),
                new ChunkGenerationWorkerOperation(7, _ => "b"),
            ]));
    }
}
