using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationAccessRegionTests
{
    [Fact]
    public void ReadEnvelopeAllowsItsInclusiveHorizontalAndVerticalRange()
    {
        var center = new ChunkCoord(12, 5, -8);
        var region = CreateRegion(
            center,
            read: new ChunkGenerationDependencyEnvelope(1, -1, 1),
            write: ChunkGenerationDependencyEnvelope.None);

        Assert.True(region.CanReadGeneratorData(center));
        Assert.True(region.CanReadGeneratorData(new ChunkCoord(13, 6, -9)));
        Assert.True(region.CanReadGeneratorData(new ChunkCoord(11, 4, -7)));

        Assert.False(region.CanReadGeneratorData(new ChunkCoord(14, 5, -8)));
        Assert.False(region.CanReadGeneratorData(new ChunkCoord(12, 7, -8)));
        Assert.False(region.CanReadGeneratorData(new ChunkCoord(12, 3, -8)));
    }

    [Fact]
    public void WorldPositionReadsAreTranslatedToTheirOwningSections()
    {
        var center = new ChunkCoord(12, 5, -8);
        var origin = VoxelCoordinates.ChunkOrigin(center);
        var region = CreateRegion(
            center,
            read: new ChunkGenerationDependencyEnvelope(1, -1, 1),
            write: ChunkGenerationDependencyEnvelope.None);

        Assert.True(region.CanReadWorldPosition(origin.X, origin.Y, origin.Z));
        Assert.True(region.CanReadWorldPosition(
            origin.X + Chunk.Size, origin.Y + Chunk.Size, origin.Z - Chunk.Size));

        Assert.False(region.CanReadWorldPosition(
            origin.X + 2 * Chunk.Size, origin.Y, origin.Z));
        Assert.False(region.CanReadWorldPosition(
            origin.X, origin.Y + 2 * Chunk.Size, origin.Z));
        Assert.False(region.CanReadWorldPosition(origin.X, -1, origin.Z));
    }

    [Fact]
    public void ColumnDataReadsUseHorizontalRadiusWithoutInventingVerticalSections()
    {
        var target = new ChunkCoord(12, 5, -8);
        var origin = VoxelCoordinates.ChunkOrigin(target);
        var region = CreateRegion(
            target,
            read: new ChunkGenerationDependencyEnvelope(2, 0, 0),
            write: ChunkGenerationDependencyEnvelope.None);

        Assert.True(region.CanReadColumnData(
            origin.X + 2 * Chunk.Size,
            origin.Z - 2 * Chunk.Size));
        Assert.False(region.CanReadColumnData(
            origin.X + 3 * Chunk.Size,
            origin.Z));
        Assert.Throws<InvalidOperationException>(
            () => region.EnsureCanReadColumnData(
                origin.X, origin.Z + 3 * Chunk.Size));
    }

    [Fact]
    public void ReadAndWriteEnvelopesAreIndependent()
    {
        var center = new ChunkCoord(4, 2, 7);
        var neighbor = new ChunkCoord(5, 2, 7);
        var region = CreateRegion(
            center,
            read: new ChunkGenerationDependencyEnvelope(1, 0, 0),
            write: ChunkGenerationDependencyEnvelope.None);

        Assert.True(region.CanReadGeneratorData(neighbor));
        Assert.False(region.CanWrite(neighbor));
        Assert.True(region.CanWrite(center));
    }

    [Fact]
    public void WriteEnvelopeControlsOnlyTheWriteBoundary()
    {
        var center = new ChunkCoord(-4, 3, 6);
        var neighbor = new ChunkCoord(-5, 3, 6);
        var region = CreateRegion(
            center,
            read: ChunkGenerationDependencyEnvelope.None,
            write: new ChunkGenerationDependencyEnvelope(1, 0, 0));

        Assert.False(region.CanReadGeneratorData(neighbor));
        Assert.True(region.CanWrite(neighbor));
        Assert.False(region.CanWrite(new ChunkCoord(-4, 4, 6)));
    }

    [Fact]
    public void AccessChecksUseWideCoordinateDeltas()
    {
        var region = CreateRegion(
            new ChunkCoord(int.MaxValue, 2, int.MinValue),
            read: new ChunkGenerationDependencyEnvelope(1, 0, 0),
            write: ChunkGenerationDependencyEnvelope.None);

        Assert.False(region.CanReadGeneratorData(
            new ChunkCoord(int.MinValue, 2, int.MinValue)));
    }

    [Fact]
    public void EnsureMethodsIdentifyTheStepAndRejectedAccess()
    {
        var region = CreateRegion(
            new ChunkCoord(0, 1, 0),
            read: ChunkGenerationDependencyEnvelope.None,
            write: ChunkGenerationDependencyEnvelope.None);

        var readError = Assert.Throws<InvalidOperationException>(
            () => region.EnsureCanReadWorldPosition(Chunk.Size, Chunk.Size, 0));
        Assert.Contains(nameof(ChunkGenerationStepId.DirectionalFacePalettes), readError.Message);
        Assert.Contains("read immutable generator data", readError.Message);

        var columnError = Assert.Throws<InvalidOperationException>(
            () => region.EnsureCanReadColumnData(Chunk.Size, 0));
        Assert.Contains("read immutable generator column data", columnError.Message);

        var writeError = Assert.Throws<InvalidOperationException>(
            () => region.EnsureCanWriteWorldPosition(0, Chunk.Size * 2, 0));
        Assert.Contains("write", writeError.Message);
        Assert.Contains("write envelope", writeError.Message);
    }

    [Fact]
    public void ConstructorRejectsSectionsBelowTheWorldFloor()
    {
        var plan = CreatePlan(
            ChunkGenerationDependencyEnvelope.None,
            ChunkGenerationDependencyEnvelope.None);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkGenerationAccessRegion(
                plan, new ChunkCoord(0, -1, 0),
                ChunkGenerationStepId.DirectionalFacePalettes));
    }

    private static ChunkGenerationAccessRegion CreateRegion(
        ChunkCoord center,
        ChunkGenerationDependencyEnvelope read,
        ChunkGenerationDependencyEnvelope write)
    {
        var plan = CreatePlan(read, write);
        return new ChunkGenerationAccessRegion(
            plan, center, ChunkGenerationStepId.DirectionalFacePalettes);
    }

    private static ChunkGenerationPlan CreatePlan(
        ChunkGenerationDependencyEnvelope read,
        ChunkGenerationDependencyEnvelope write) =>
        new(
        [
            new ChunkGenerationStepDefinition(
                ChunkGenerationStepId.DirectionalFacePalettes,
                ChunkGenerationWorkScope.Section,
                ChunkGenerationExecutionPolicy.OrderedMutation,
                Array.Empty<ChunkGenerationStepDependency>(),
                read,
                write),
        ]);
}
