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
    public void AirCellsNormalizeRuntimeState()
    {
        var cell = new VoxelCell(BlockRuntimeId.Air, orientation: 9, state: 42);

        Assert.Equal(VoxelCell.Empty, cell);
    }
}
