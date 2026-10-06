using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkStreamingStateTests
{
    [Fact]
    public void DesiredSelectionUsesCircularHorizontalRadius()
    {
        var desired =
            ChunkStreamingSelection.DesiredQaChunks(
                ChunkCoord.Zero,
                2,
                0,
                1);

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
            ChunkStreamingSelection.DesiredQaChunks(
                ChunkCoord.Zero,
                1,
                0,
                0));

        var moved = new ChunkCoord(2, 0, 0);
        state.RebuildSelection(
            moved,
            1,
            3,
            ChunkStreamingSelection.DesiredQaChunks(
                moved,
                1,
                0,
                0));

        Assert.True(
            state.KeepsLoaded(
                new ChunkCoord(0, 0, 0)));

        state.RebuildSelection(
            new ChunkCoord(10, 0, 0),
            1,
            3,
            ChunkStreamingSelection.DesiredQaChunks(
                new ChunkCoord(10, 0, 0),
                1,
                0,
                0));

        Assert.False(
            state.KeepsLoaded(
                new ChunkCoord(0, 0, 0)));
        Assert.Equal(
            new ChunkCoord(0, 0, 0),
            state.PopRetiredOutsideHorizontalRadius(
                new ChunkCoord(10, 0, 0),
                3));
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
}
