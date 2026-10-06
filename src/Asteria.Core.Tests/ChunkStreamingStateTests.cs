using Asteria.Core.World;

namespace Asteria.Core.Tests;

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
