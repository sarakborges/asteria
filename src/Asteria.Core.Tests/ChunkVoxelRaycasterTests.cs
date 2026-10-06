using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkVoxelRaycasterTests
{
    [Fact]
    public void HitsCubeWithEntrySurfaceNormal()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var chunk = new Chunk();
        chunk.SetBlock(4, 4, 4, blocks.GetId("asteria:stone"));

        var hit = ChunkVoxelRaycaster.Raycast(
            chunk,
            blocks,
            new Vector3(4.5f, 8f, 4.5f),
            -Vector3.UnitY,
            8f);

        Assert.NotNull(hit);
        Assert.Equal(new LocalVoxelCoord(4, 4, 4), hit.Value.Voxel);
        Assert.Equal(1, hit.Value.NormalY);
    }

    [Fact]
    public void PassesThroughHollowCenter()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:hollow",
                shape: BlockShapeDefinition.Hollow(1f / 16f)),
        ]);
        var chunk = new Chunk();
        chunk.SetBlock(4, 4, 4, blocks.GetId("asteria:hollow"));

        var hit = ChunkVoxelRaycaster.Raycast(
            chunk,
            blocks,
            new Vector3(4.5f, 8f, 4.5f),
            -Vector3.UnitY,
            8f);

        Assert.Null(hit);
    }

    [Fact]
    public void UsesMicroblockOccupancyInsteadOfMacroCube()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone", tags: ["fragmentable"]),
        ]);
        var stone = blocks.GetId("asteria:stone");
        var chunk = new Chunk();
        chunk.SetBlock(4, 4, 4, stone);

        var mask = MicroblockMask.Empty.Edit(
            0,
            0,
            0,
            MicroblockResolution.Thick,
            occupied: true);
        chunk.SetMicroblockMask(4, 4, 4, mask);

        var miss = ChunkVoxelRaycaster.Raycast(
            chunk,
            blocks,
            new Vector3(4.75f, 8f, 4.75f),
            -Vector3.UnitY,
            8f);

        var hit = ChunkVoxelRaycaster.Raycast(
            chunk,
            blocks,
            new Vector3(4.1f, 8f, 4.1f),
            -Vector3.UnitY,
            8f);

        Assert.Null(miss);
        Assert.NotNull(hit);
    }
}
