using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PortableBlockLightingTests
{
    [Fact]
    public void PortableBlockRelightsFromItsActualWorldPosition()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var chunk = new Chunk();
        chunk.FillLight(new VoxelLight(3, 0, 0, 0));
        for (var z = 4; z <= 6; z++)
        for (var x = 4; x <= 6; x++)
            chunk.SetLight(x, 6, z, new VoxelLight(12, 0, 0, 0));

        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var geometry = CreatePortableTopFace();

        Assert.True(ChunkMeshLightingUpdater.TryRelightPortableBlock(
            world,
            new WorldVoxelCoord(5, 5, 5),
            blocks,
            geometry,
            out var bright));
        Assert.All(
            bright.RenderBatches.Single().Vertices,
            vertex => Assert.InRange(
                MathF.Abs(vertex.VoxelLight.X - 12f / VoxelLight.MaxLevel),
                0f,
                0.001f));

        Assert.True(ChunkMeshLightingUpdater.TryRelightPortableBlock(
            world,
            new WorldVoxelCoord(10, 10, 10),
            blocks,
            geometry,
            out var dim));
        Assert.All(
            dim.RenderBatches.Single().Vertices,
            vertex => Assert.InRange(
                MathF.Abs(vertex.VoxelLight.X - 3f / VoxelLight.MaxLevel),
                0f,
                0.001f));
    }

    [Fact]
    public void LightingNeighborhoodStampChangesWhenItsChunkLightingChanges()
    {
        var position = new WorldVoxelCoord(5, 5, 5);
        var destination = new VoxelWorld();
        destination.InsertChunk(ChunkCoord.Zero, new Chunk());
        var sourceChunk = new Chunk();
        sourceChunk.SetLight(5, 5, 5, new VoxelLight(9, 0, 0, 0));
        var source = new VoxelWorld();
        source.InsertChunk(ChunkCoord.Zero, sourceChunk);

        Span<VoxelLightChunkStamp> before = stackalloc VoxelLightChunkStamp[8];
        var beforeCount = destination.CaptureLightingNeighborhood(
            position, before);
        destination.CopyLightPositionsFrom(source, [position], 0, 1);
        Span<VoxelLightChunkStamp> after = stackalloc VoxelLightChunkStamp[8];
        var afterCount = destination.CaptureLightingNeighborhood(
            position, after);

        Assert.Equal(beforeCount, afterCount);
        Assert.NotEqual(before[0], after[0]);

        destination.CopyLightPositionsFrom(source, [position], 0, 1);
        Span<VoxelLightChunkStamp> unchanged = stackalloc VoxelLightChunkStamp[8];
        var unchangedCount = destination.CaptureLightingNeighborhood(
            position, unchanged);
        Assert.Equal(afterCount, unchangedCount);
        Assert.Equal(after[0], unchanged[0]);
    }

    private static ChunkMeshData CreatePortableTopFace()
    {
        byte[] corners = [0, 1, 2, 0, 2, 3];
        var vertices = new ChunkMeshVertex[corners.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index] = new ChunkMeshVertex(
                Vector3.Zero,
                Vector3.UnitY,
                Vector2.Zero,
                Vector2.Zero,
                Vector4.One,
                Vector4.Zero,
                new ChunkMeshLightSample(
                    new LocalVoxelCoord(0, 0, 0),
                    BlockFace.Top,
                    corners[index]));
        }

        return new ChunkMeshData(
        [
            new ChunkRenderBatchData(
                new TerrainRenderBatch(BlockRenderMode.Opaque, CastsShadow: true),
                vertices),
        ],
        Array.Empty<Vector3>(),
        CanRelightWithoutRemesh: true);
    }
}
