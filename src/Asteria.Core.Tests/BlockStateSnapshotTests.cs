using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockStateSnapshotTests
{
    [Fact]
    public void MicroblockMaskIsPortableAcrossChunkPalettes()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        world.InsertChunk(
            new ChunkCoord(1, 0, 0),
            new Chunk());

        var block =
            new BlockRuntimeId(1);
        var source =
            new WorldVoxelCoord(2, 2, 2);
        var target =
            new WorldVoxelCoord(18, 2, 2);
        var sourceMask =
            MicroblockMask.Full.Edit(
                0,
                0,
                0,
                MicroblockResolution.Thin,
                occupied: false);
        var otherMask =
            MicroblockMask.Full.Edit(
                7,
                7,
                7,
                MicroblockResolution.ExtraThin,
                occupied: false);

        Assert.True(
            world.SetBlockAt(
                source,
                block,
                out _));

        var sourceAddress =
            VoxelCoordinates.FromWorld(
                source.X,
                source.Y,
                source.Z);
        Assert.True(
            world.GetChunk(
                    sourceAddress.Chunk)
                .SetMicroblockMask(
                    sourceAddress.Local.X,
                    sourceAddress.Local.Y,
                    sourceAddress.Local.Z,
                    sourceMask));

        var destinationChunk =
            world.GetChunk(
                new ChunkCoord(1, 0, 0));
        Assert.True(
            destinationChunk.SetBlock(
                0,
                0,
                0,
                block));
        Assert.True(
            destinationChunk.SetMicroblockMask(
                0,
                0,
                0,
                otherMask));

        var sourceCell =
            world.GetCellOrEmpty(source);
        var snapshot =
            BlockStateSnapshot.Capture(
                world,
                source,
                sourceCell);

        Assert.Equal(
            0,
            snapshot.Cell.MicroblockMaskId);
        Assert.Equal(
            sourceMask,
            snapshot.MicroblockMask);

        Assert.True(
            world.SetBlockStateAt(
                target,
                snapshot,
                out _));

        var targetCell =
            world.GetCellOrEmpty(target);

        Assert.True(
            targetCell.HasMicroblockGeometry);
        Assert.Equal(
            sourceMask,
            world.GetMicroblockMaskOrEmpty(
                target));
    }
}
