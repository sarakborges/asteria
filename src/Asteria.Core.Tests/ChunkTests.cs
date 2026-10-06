using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkTests
{
    [Fact]
    public void ChunkUsesSixteenCubedRuntimeShape()
    {
        Assert.Equal(16, Chunk.Size);
        Assert.Equal(4096, Chunk.Volume);
    }

    [Fact]
    public void PaletteDeduplicatesCellsAndTracksOccupancy()
    {
        var registry = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dirt"),
        ]);
        var stone = registry.GetId("asteria:stone");
        var dirt = registry.GetId("asteria:dirt");
        var chunk = new Chunk();

        Assert.True(chunk.SetBlock(1, 2, 3, stone));
        Assert.True(chunk.SetBlock(2, 2, 3, stone));
        Assert.True(chunk.SetBlock(3, 2, 3, dirt));

        Assert.Equal(3, chunk.NonEmptyVoxelCount);
        Assert.Equal(2, chunk.PaletteEntryCount);
        Assert.Equal((ulong)3, chunk.Revision);

        Assert.False(chunk.SetBlock(1, 2, 3, stone));
        Assert.Equal((ulong)3, chunk.Revision);

        Assert.True(chunk.SetBlock(3, 2, 3, BlockRuntimeId.Air));
        Assert.Equal(2, chunk.NonEmptyVoxelCount);
        Assert.Equal(1, chunk.PaletteEntryCount);
        Assert.True(chunk.GetCell(3, 2, 3).IsEmpty);
    }

    [Fact]
    public void PaletteReusesReleasedSlotsWithoutGrowingActiveEntries()
    {
        var registry = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dirt"),
            new BlockDefinition("asteria:clay"),
        ]);
        var chunk = new Chunk();

        Assert.True(
            chunk.SetBlock(
                1,
                1,
                1,
                registry.GetId("asteria:stone")));
        Assert.True(
            chunk.SetBlock(
                2,
                1,
                1,
                registry.GetId("asteria:dirt")));
        Assert.Equal(
            2,
            chunk.PaletteEntryCount);

        Assert.True(
            chunk.SetBlock(
                2,
                1,
                1,
                BlockRuntimeId.Air));
        Assert.Equal(
            1,
            chunk.PaletteEntryCount);

        Assert.True(
            chunk.SetBlock(
                3,
                1,
                1,
                registry.GetId("asteria:clay")));
        Assert.Equal(
            2,
            chunk.PaletteEntryCount);
    }

    [Fact]
    public void WorkerClonePreservesPaletteMicroblocksFluidsAndRevision()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var fluids = new FluidRegistry(
        [
            new FluidDefinition("asteria:water"),
        ]);
        var chunk = new Chunk();
        var stone = blocks.GetId("asteria:stone");
        var water = fluids.GetId("asteria:water");

        Assert.True(
            chunk.SetBlock(
                2,
                3,
                4,
                stone));
        var mask =
            MicroblockMask.Full.Edit(
                0,
                0,
                0,
                MicroblockResolution.Thick,
                occupied: false);
        Assert.True(
            chunk.SetMicroblockMask(
                2,
                3,
                4,
                mask));
        Assert.True(
            chunk.SetFluid(
                5,
                6,
                7,
                FluidCell.Source(water)));

        var clone =
            chunk.CloneForWorker();

        Assert.Equal(
            chunk.Revision,
            clone.Revision);
        Assert.Equal(
            chunk.PaletteEntryCount,
            clone.PaletteEntryCount);
        Assert.Equal(
            chunk.FluidPaletteEntryCount,
            clone.FluidPaletteEntryCount);
        Assert.Equal(
            mask,
            clone.GetMicroblockMask(
                2,
                3,
                4));
        Assert.Equal(
            FluidCell.Source(water),
            clone.GetFluid(
                5,
                6,
                7));

        Assert.True(
            clone.SetBlock(
                8,
                8,
                8,
                stone));
        Assert.True(
            chunk.GetCell(
                8,
                8,
                8).IsEmpty);
    }

    [Fact]
    public void FluidVisitorUsesOccupiedCellsAndPreservesCoordinates()
    {
        var fluids = new FluidRegistry(
        [
            new FluidDefinition("asteria:water"),
        ]);
        var chunk = new Chunk();
        var water = fluids.GetId("asteria:water");

        chunk.SetFluid(
            15,
            2,
            9,
            FluidCell.Source(water));
        chunk.SetFluid(
            1,
            14,
            3,
            FluidCell.Spreading(
                water,
                level: 4,
                spreadDistance: 2));

        var visited =
            new List<(int X, int Y, int Z, FluidCell Fluid)>();

        chunk.VisitFluidCells(
            (x, y, z, fluid) =>
                visited.Add(
                    (x, y, z, fluid)));

        Assert.Equal(2, visited.Count);
        Assert.Contains(
            visited,
            entry =>
                entry.X == 15 &&
                entry.Y == 2 &&
                entry.Z == 9);
        Assert.Contains(
            visited,
            entry =>
                entry.X == 1 &&
                entry.Y == 14 &&
                entry.Z == 3);
    }

    [Fact]
    public void AirCellsNormalizeRuntimeState()
    {
        var cell = new VoxelCell(
            BlockRuntimeId.Air,
            TextureRotation.Degrees270,
            BlockOrientation.X,
            HorizontalFacing.North,
            state: 42,
            microblockMaskId: 9);

        Assert.Equal(VoxelCell.Empty, cell);
    }

    [Fact]
    public void RuntimeCellCarriesIndependentOrientationFacingAndTextureRotation()
    {
        var cell = new VoxelCell(new BlockRuntimeId(7))
            .WithOrientation(BlockOrientation.Z)
            .WithFacing(HorizontalFacing.West)
            .WithTextureRotation(TextureRotation.Degrees90);

        Assert.Equal(BlockOrientation.Z, cell.Orientation);
        Assert.Equal(HorizontalFacing.West, cell.Facing);
        Assert.Equal(TextureRotation.Degrees90, cell.TextureRotation);
    }
}
