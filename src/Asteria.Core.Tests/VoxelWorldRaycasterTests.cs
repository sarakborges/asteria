using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelWorldRaycasterTests
{
    [Fact]
    public void RaycastCrossesChunkBoundary()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var stone = blocks.GetId("asteria:stone");
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        world.SetBlockAt(
            new WorldVoxelCoord(16, 4, 4),
            stone,
            out _);

        var hit = VoxelWorldRaycaster.Raycast(
            world,
            blocks,
            new Vector3(14.5f, 4.5f, 4.5f),
            Vector3.UnitX,
            4f);

        Assert.NotNull(hit);
        Assert.Equal(
            new WorldVoxelCoord(16, 4, 4),
            hit.Value.Voxel);
        Assert.Equal(-1, hit.Value.NormalX);
    }
}
