using Asteria.Core.World;

namespace Asteria.Core.Tests;

[Collection("ChunkStreamingConcurrency")]
public sealed class ChunkStreamingStateTests
{
    [Fact]
    public void DesiredSelectionUsesCircularHorizontalRadius()
    {
        var desired =
            ChunkStreamingSelection.DesiredSurfaceChunks(
                ChunkCoord.Zero,
                2,
                new ConstantSurfaceRangeProvider(
                    new ChunkSurfaceRange(
                        0,
                        15)));

        Assert.Contains(
            new ChunkCoord(2, 0, 0),
            desired);
        Assert.Contains(
            new ChunkCoord(0, 1, 2),
            desired);
        Assert.DoesNotContain(
            new ChunkCoord(2, 0, 2),
            desired);
    }

    [Fact]
    public void DesiredSelectionFollowsTallAuthoredSurface()
    {
        var desired =
            ChunkStreamingSelection.DesiredSurfaceChunks(
                ChunkCoord.Zero,
                1,
                new ConstantSurfaceRangeProvider(
                    new ChunkSurfaceRange(
                        96,
                        111)));

        Assert.Contains(
            new ChunkCoord(
                0,
                4,
                0),
            desired);
        Assert.Contains(
            new ChunkCoord(
                0,
                7,
                0),
            desired);
        Assert.DoesNotContain(
            new ChunkCoord(
                0,
                8,
                0),
            desired);
    }

    [Fact]
    public void PlayerLocalSelectionNeedsNoSurfaceRangeQueries()
    {
        var desired =
            ChunkStreamingSelection
                .DesiredPlayerLocalChunks(
                    new ChunkCoord(
                        4,
                        6,
                        -3),
                    horizontalRadius: 4);

        Assert.Contains(
            new ChunkCoord(
                4,
                6,
                -3),
            desired);
        Assert.Contains(
            new ChunkCoord(
                6,
                8,
                -1),
            desired);
        Assert.DoesNotContain(
            new ChunkCoord(
                7,
                6,
                -3),
            desired);
    }

    [Fact]
    public async Task SelectionRuntimeDiscardsStaleCenterResults()
    {
        var provider =
            new BlockingSurfaceRangeProvider();
        var runtime =
            new ChunkStreamingSelectionRuntime(
                provider);
        var callerThread =
            Environment.CurrentManagedThreadId;

        runtime.Request(
            ChunkCoord.Zero,
            horizontalRadius: 1);

        var workerThreadId =
            await provider.Started
                .Task
                .WaitAsync(
                    TimeSpan.FromSeconds(2));
        Assert.NotEqual(
            callerThread,
            workerThreadId);

        var latest =
            new ChunkCoord(
                3,
                2,
                -2);
        runtime.Request(
            latest,
            horizontalRadius: 1);
        provider.Release.Set();

        ChunkStreamingSelectionSnapshot? result =
            null;
        Exception? error =
            null;

        for (var attempt = 0;
             attempt < 400 &&
             result is null &&
             error is null;
             attempt++)
        {
            if (runtime.TryPollCompleted(
                    out var completed,
                    out var completedError))
            {
                error =
                    completedError;
                result ??=
                    completed;
            }

            if (result is null &&
                error is null)
            {
                await Task.Delay(5);
            }
        }

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(
            latest,
            result!.Center);
    }

    [Fact]
    public void SurfaceRangeWindowReusesOverlappingColumnsWhenCenterMoves()
    {
        var source =
            new CountingSurfaceRangeProvider();
        var window =
            new ChunkSurfaceRangeWindow(
                source);

        window.RetainWindow(
            0,
            0,
            horizontalRadius: 2);
        _ =
            ChunkStreamingSelection.DesiredSurfaceChunks(
                ChunkCoord.Zero,
                2,
                window);
        var firstCalls =
            source.CallCount;

        window.RetainWindow(
            1,
            0,
            horizontalRadius: 2);
        _ =
            ChunkStreamingSelection.DesiredSurfaceChunks(
                new ChunkCoord(
                    1,
                    0,
                    0),
                2,
                window);

        Assert.Equal(
            13,
            firstCalls);
        Assert.True(
            source.CallCount <
            firstCalls * 2);
        Assert.InRange(
            window.CachedColumnCount,
            1,
            25);
    }

    [Fact]
    public void SelectionRebuildPredicateIsOwnedByStreamingState()
    {
        var state =
            new ChunkStreamingState();

        Assert.True(
            state.SelectionNeedsRebuild(
                ChunkCoord.Zero,
                2));

        state.RebuildSelection(
            ChunkCoord.Zero,
            horizontalRadius: 2,
            retentionRadius: 3,
            ChunkStreamingSelection.DesiredSurfaceChunks(
                ChunkCoord.Zero,
                2,
                new ConstantSurfaceRangeProvider(
                    new ChunkSurfaceRange(
                        0,
                        15))));

        Assert.False(
            state.SelectionNeedsRebuild(
                ChunkCoord.Zero,
                2));
        Assert.True(
            state.SelectionNeedsRebuild(
                new ChunkCoord(
                    0,
                    1,
                    0),
                2));
    }

    [Fact]
    public void LoadPriorityFavorsMovementDirectionAfterDistance()
    {
        var center = ChunkCoord.Zero;
        var movement =
            new ChunkMovementDirection(1, 0);

        var forward = ChunkStreamingState.Priority(
            new ChunkCoord(2, 0, 0),
            center,
            movement);
        var backward = ChunkStreamingState.Priority(
            new ChunkCoord(-2, 0, 0),
            center,
            movement);

        Assert.True(forward.CompareTo(backward) < 0);
    }

    [Fact]
    public void ChunksLeavingDesiredRadiusRemainRetainedBeforeRetirement()
    {
        var state = new ChunkStreamingState();
        state.RebuildSelection(
            ChunkCoord.Zero,
            1,
            3,
            ChunkStreamingSelection.DesiredSurfaceChunks(
                ChunkCoord.Zero,
                1,
                new ConstantSurfaceRangeProvider(
                    new ChunkSurfaceRange(
                        0,
                        15))));

        var moved = new ChunkCoord(2, 0, 0);
        state.RebuildSelection(
            moved,
            1,
            3,
            ChunkStreamingSelection.DesiredSurfaceChunks(
                moved,
                1,
                new ConstantSurfaceRangeProvider(
                    new ChunkSurfaceRange(
                        0,
                        15))));

        Assert.True(
            state.KeepsLoaded(
                new ChunkCoord(0, 0, 0)));

        state.RebuildSelection(
            new ChunkCoord(10, 0, 0),
            1,
            3,
            ChunkStreamingSelection.DesiredSurfaceChunks(
                new ChunkCoord(10, 0, 0),
                1,
                new ConstantSurfaceRangeProvider(
                    new ChunkSurfaceRange(
                        0,
                        15))));

        var origin = new ChunkCoord(0, 0, 0);
        Assert.False(state.KeepsLoaded(origin));

        var retired = new HashSet<ChunkCoord>();
        ChunkCoord? next;
        while ((next =
                    state.PopRetiredOutsideHorizontalRadius(
                        new ChunkCoord(10, 0, 0),
                        3)) is not null)
        {
            retired.Add(next.Value);
        }

        Assert.Contains(origin, retired);
    }

    [Fact]
    public void RetiredScanContinuesAfterBoundedIneligiblePrefix()
    {
        var state =
            new ChunkStreamingState();
        var initial =
            new HashSet<ChunkCoord>();

        for (var x = 1;
             x <= 64;
             x++)
        {
            initial.Add(
                new ChunkCoord(
                    x,
                    0,
                    0));
        }

        var eligible =
            new ChunkCoord(
                1000,
                0,
                0);
        initial.Add(eligible);

        state.RebuildSelection(
            ChunkCoord.Zero,
            horizontalRadius: 1,
            retentionRadius: 0,
            initial);

        var farCenter =
            new ChunkCoord(
                2000,
                0,
                0);
        state.RebuildSelection(
            farCenter,
            horizontalRadius: 1,
            retentionRadius: 0,
            new HashSet<ChunkCoord>
            {
                farCenter,
            });

        state.RebuildSelection(
            ChunkCoord.Zero,
            horizontalRadius: 1,
            retentionRadius: 100,
            new HashSet<ChunkCoord>
            {
                ChunkCoord.Zero,
            });

        Assert.Null(
            state.PopRetiredOutsideHorizontalRadius(
                ChunkCoord.Zero,
                retentionRadius: 100));

        Assert.Equal(
            eligible,
            state.PopRetiredOutsideHorizontalRadius(
                ChunkCoord.Zero,
                retentionRadius: 100));
    }

    [Fact]
    public void PendingPrioritySelectionDoesNotDependOnHashIteration()
    {
        var state =
            new ChunkStreamingState();
        var desired =
            new HashSet<ChunkCoord>
            {
                new ChunkCoord(-2, 0, 0),
                new ChunkCoord(2, 0, 0),
            };

        state.RebuildSelection(
            ChunkCoord.Zero,
            horizontalRadius: 2,
            retentionRadius: 3,
            desired);
        state.SyncResidentState(
            Array.Empty<ChunkCoord>(),
            Array.Empty<ChunkCoord>());

        Assert.Equal(
            new ChunkCoord(-2, 0, 0),
            state.PopPendingByPriority());
    }

    [Fact]
    public void PresentationSelectionHasVisibilityHysteresis()
    {
        var selection = new ChunkPresentationSelection();
        selection.Sync(ChunkCoord.Zero, 4);
        var coord = new ChunkCoord(6, 0, 0);

        Assert.False(
            selection.ShouldBeVisible(
                coord,
                currentlyVisible: false));
        Assert.True(
            selection.ShouldBeVisible(
                coord,
                currentlyVisible: true));
    }

    private sealed class BlockingSurfaceRangeProvider :
        IChunkSurfaceRangeProvider
    {
        public TaskCompletionSource<int> Started { get; } =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim Release { get; } =
            new(false);

        public ChunkSurfaceRange GetSurfaceRange(
            int chunkX,
            int chunkZ)
        {
            Started.TrySetResult(
                Environment.CurrentManagedThreadId);
            Release.Wait(
                TimeSpan.FromSeconds(2));

            return new ChunkSurfaceRange(
                0,
                15);
        }
    }

    private sealed class CountingSurfaceRangeProvider :
        IChunkSurfaceRangeProvider
    {
        public int CallCount { get; private set; }

        public ChunkSurfaceRange GetSurfaceRange(
            int chunkX,
            int chunkZ)
        {
            CallCount++;
            return new ChunkSurfaceRange(
                0,
                15);
        }
    }

    private sealed class ConstantSurfaceRangeProvider(
        ChunkSurfaceRange range) :
        IChunkSurfaceRangeProvider
    {
        public ChunkSurfaceRange GetSurfaceRange(
            int chunkX,
            int chunkZ) =>
            range;
    }

}
