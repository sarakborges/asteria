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
        var stone =
            blocks.GetId("asteria:stone");
        var world = new VoxelWorld();
        world.InsertChunk(
            new ChunkCoord(0, 0, 0),
            new Chunk());
        world.InsertChunk(
            new ChunkCoord(1, 0, 0),
            new Chunk());
        world.SetBlockAt(
            new WorldVoxelCoord(16, 4, 4),
            stone,
            out _);

        var hit =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    14.5f,
                    4.5f,
                    4.5f),
                Vector3.UnitX,
                4f);

        Assert.NotNull(hit);
        Assert.Equal(
            new WorldVoxelCoord(16, 4, 4),
            hit.Value.Voxel);
        Assert.Equal(
            -1,
            hit.Value.NormalX);
    }

    [Fact]
    public void RaycastPassesThroughHollowCenter()
    {
        var blocks =
            CreateGeometryBlocks();
        var hollow =
            blocks.GetId("asteria:hollow");
        var stone =
            blocks.GetId("asteria:stone");
        var world = LoadedWorld();

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(2, 2, 2),
                hollow,
                out _));
        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(2, 3, 2),
                stone,
                out _));

        var hit =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    2.5f,
                    1.5f,
                    2.5f),
                Vector3.UnitY,
                3f);

        Assert.NotNull(hit);
        Assert.Equal(
            new WorldVoxelCoord(2, 3, 2),
            hit.Value.Voxel);
        Assert.Equal(
            -1,
            hit.Value.NormalY);
    }

    [Fact]
    public void RaycastHitsHollowWall()
    {
        var blocks =
            CreateGeometryBlocks();
        var world = LoadedWorld();

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(2, 2, 2),
                blocks.GetId("asteria:hollow"),
                out _));

        var hit =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    2.02f,
                    1.5f,
                    2.5f),
                Vector3.UnitY,
                2f);

        Assert.NotNull(hit);
        Assert.Equal(
            new WorldVoxelCoord(2, 2, 2),
            hit.Value.Voxel);
        Assert.Equal(
            -1,
            hit.Value.NormalY);
    }

    [Fact]
    public void ThinLayerReturnsInternalSurfaceNormal()
    {
        var blocks =
            CreateGeometryBlocks();
        var world = LoadedWorld();

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(2, 2, 2),
                blocks.GetId("asteria:layer"),
                out _));

        var hit =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    2.5f,
                    3.5f,
                    2.5f),
                -Vector3.UnitY,
                3f);

        Assert.NotNull(hit);
        Assert.Equal(
            new WorldVoxelCoord(2, 2, 2),
            hit.Value.Voxel);
        Assert.Equal(
            1,
            hit.Value.NormalY);
    }

    [Fact]
    public void MicroblockNarrowPhaseDistinguishesGapFromOccupiedCell()
    {
        var blocks =
            CreateGeometryBlocks();
        var stone =
            blocks.GetId("asteria:stone");
        var world = LoadedWorld();
        var chunk =
            world.GetChunk(
                ChunkCoord.Zero);

        Assert.True(
            chunk.SetBlock(
                2,
                2,
                2,
                stone));
        Assert.True(
            chunk.SetMicroblockMask(
                2,
                2,
                2,
                MicroblockMask.Empty.Edit(
                    0,
                    0,
                    0,
                    MicroblockResolution.ExtraThin,
                    occupied: true)));
        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(3, 2, 2),
                stone,
                out _));

        var gapHit =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    1.5f,
                    2.5f,
                    2.5f),
                Vector3.UnitX,
                3f);

        Assert.NotNull(gapHit);
        Assert.Equal(
            new WorldVoxelCoord(3, 2, 2),
            gapHit.Value.Voxel);

        var occupiedHit =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    1.5f,
                    2.05f,
                    2.05f),
                Vector3.UnitX,
                2f);

        Assert.NotNull(occupiedHit);
        Assert.Equal(
            new WorldVoxelCoord(2, 2, 2),
            occupiedHit.Value.Voxel);
        Assert.Equal(
            -1,
            occupiedHit.Value.NormalX);
    }

    [Fact]
    public void GroundSpriteRaycastUsesCompactHitboxInsteadOfWholeVoxel()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:stick",
                visual: BlockVisualDefinition.GroundSprite(
                    new BlockTextureLayer("textures/items/stick.png"),
                    width: 0.68f, targetHeight: 0.12f)),
            new BlockDefinition("asteria:stone"),
        ]);
        var world = LoadedWorld();
        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(2, 2, 2), blocks.GetId("asteria:stick"),
            out _));
        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(3, 2, 2), blocks.GetId("asteria:stone"),
            out _));

        var highRay = VoxelWorldRaycaster.Raycast(
            world, blocks, new Vector3(1.5f, 2.5f, 2.5f),
            Vector3.UnitX, 4f);
        Assert.Equal(new WorldVoxelCoord(3, 2, 2), highRay!.Value.Voxel);

        var lowRay = VoxelWorldRaycaster.Raycast(
            world, blocks, new Vector3(1.5f, 2.07f, 2.5f),
            Vector3.UnitX, 4f);
        Assert.Equal(new WorldVoxelCoord(2, 2, 2), lowRay!.Value.Voxel);

        var outsideFootprint = VoxelWorldRaycaster.Raycast(
            world, blocks, new Vector3(2.93f, 3.5f, 2.93f),
            -Vector3.UnitY, 3f);
        Assert.Null(outsideFootprint);
    }

    private static VoxelWorld LoadedWorld()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        return world;
    }

    private static BlockRegistry
        CreateGeometryBlocks() =>
        new(
        [
            new BlockDefinition(
                "asteria:stone"),
            new BlockDefinition(
                "asteria:hollow",
                shape:
                    BlockShapeDefinition.Hollow(
                        0.125f)),
            new BlockDefinition(
                "asteria:layer",
                shape:
                    BlockShapeDefinition.SurfaceLayer(
                        0.125f,
                        "asteria:stone")),
        ]);
}
