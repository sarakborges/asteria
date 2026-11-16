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
                    width: 0.68f, targetHeight: 0.12f,
                    positionJitter: Vector2.Zero)),
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


    [Fact]
    public void CrossedSpriteRaycastUsesVisualWidthHeightAndBaseOffset()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:flower",
                visual: BlockVisualDefinition.CrossedSprite(
                    new BlockTextureLayer("textures/objects/flower.png"),
                    width: 0.4f, height: 0.48f,
                    baseOffset: 0.15f, positionJitter: Vector2.Zero),
                isCollidable: false),
            new BlockDefinition("asteria:stone"),
        ]);
        var world = LoadedWorld();
        var flowerPosition = new WorldVoxelCoord(2, 2, 2);
        var stonePosition = new WorldVoxelCoord(3, 2, 2);
        Assert.True(world.SetBlockAt(
            flowerPosition, blocks.GetId("asteria:flower"), out _));
        Assert.True(world.SetBlockAt(
            stonePosition, blocks.GetId("asteria:stone"), out _));

        foreach (var origin in new[]
        {
            new Vector3(1.5f, 2.9f, 2.5f), // Above the sprite.
            new Vector3(1.5f, 2.04f, 2.5f), // Below its base offset.
            new Vector3(1.5f, 2.4f, 2.95f), // Outside its width.
        })
        {
            var hit = VoxelWorldRaycaster.Raycast(
                world, blocks, origin, Vector3.UnitX, 4f);
            Assert.Equal(stonePosition, hit!.Value.Voxel);
        }

        var bodyHit = VoxelWorldRaycaster.Raycast(
            world, blocks, new Vector3(1.5f, 2.4f, 2.5f),
            Vector3.UnitX, 4f);
        Assert.Equal(flowerPosition, bodyHit!.Value.Voxel);
        Assert.Equal(-1, bodyHit.Value.NormalX);

        var topHit = VoxelWorldRaycaster.Raycast(
            world, blocks, new Vector3(2.5f, 3.5f, 2.5f),
            -Vector3.UnitY, 3f);
        Assert.Equal(flowerPosition, topHit!.Value.Voxel);
        Assert.Equal(1, topHit.Value.NormalY);
    }


    [Fact]
    public void FluidHudSkipsTheSourceContainingTheCamera()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = LoadedWorld();
        var water = new FluidRuntimeId(1);
        var cameraVoxel = new WorldVoxelCoord(2, 4, 4);
        var nextVoxel = new WorldVoxelCoord(3, 4, 4);
        Assert.True(world.SetFluidAt(cameraVoxel, FluidCell.Source(water), out _));
        var camera = new Vector3(2.5f, 4.5f, 4.5f);
        Assert.Null(VoxelWorldRaycaster.Raycast(world, blocks, camera,
            Vector3.UnitX, 4f, includeFluidSources: true));

        Assert.True(world.SetFluidAt(nextVoxel, FluidCell.Source(water), out _));
        var hit = VoxelWorldRaycaster.Raycast(world, blocks, camera,
            Vector3.UnitX, 4f, includeFluidSources: true);
        Assert.Equal(nextVoxel, hit!.Value.Voxel);
        Assert.Equal(-1, hit.Value.NormalX);
    }

    [Fact]
    public void FlowingWaterBeforeSourceStopsOnlyFluidSelectionNotBlockRay()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = LoadedWorld();
        var water = new FluidRuntimeId(1);
        var flowing = new WorldVoxelCoord(2, 4, 4);
        var source = new WorldVoxelCoord(4, 4, 4);
        var wall = new WorldVoxelCoord(6, 4, 4);
        Assert.True(world.SetFluidAt(
            flowing, FluidCell.Spreading(water, 4, 1), out _));
        Assert.True(world.SetFluidAt(source, FluidCell.Source(water), out _));
        Assert.True(world.SetBlockAt(wall, blocks.GetId("asteria:stone"), out _));
        var origin = new Vector3(1.5f, 4.5f, 4.5f);

        Assert.Equal(wall, VoxelWorldRaycaster.Raycast(world, blocks, origin,
            Vector3.UnitX, 6f, includeFluidSources: true)!.Value.Voxel);
        Assert.True(world.SetFluidAt(flowing, FluidCell.Empty, out _));
        Assert.Equal(source, VoxelWorldRaycaster.Raycast(world, blocks, origin,
            Vector3.UnitX, 6f, includeFluidSources: true)!.Value.Voxel);
        Assert.Equal(wall, VoxelWorldRaycaster.Raycast(world, blocks, origin,
            Vector3.UnitX, 6f)!.Value.Voxel);
    }

    [Fact]
    public void FluidSourceHudRaycastStopsAtNearerOpaqueBlocks()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(30, 100, 205), 0.75f),
        ]);
        var water = fluids.GetId("asteria:water");
        var world = LoadedWorld();
        var source = new WorldVoxelCoord(3, 4, 4);
        var wall = new WorldVoxelCoord(5, 4, 4);
        Assert.True(world.SetFluidAt(source, FluidCell.Source(water), out _));
        Assert.True(world.SetBlockAt(wall, blocks.GetId("asteria:stone"), out _));
        var origin = new Vector3(1.5f, 4.5f, 4.5f);
        Assert.Equal(wall, VoxelWorldRaycaster.Raycast(
            world, blocks, origin, Vector3.UnitX, 6f)!.Value.Voxel);
        Assert.Equal(source, VoxelWorldRaycaster.Raycast(
            world, blocks, origin, Vector3.UnitX, 6f,
            includeFluidSources: true)!.Value.Voxel);

        var obstruction = new WorldVoxelCoord(2, 4, 4);
        Assert.True(world.SetBlockAt(
            obstruction, blocks.GetId("asteria:stone"), out _));
        Assert.Equal(obstruction, VoxelWorldRaycaster.Raycast(
            world, blocks, origin, Vector3.UnitX, 6f,
            includeFluidSources: true)!.Value.Voxel);
    }

    [Fact]
    public void FluidHudRaycastSkipsFlowingWaterWithoutChangingSurfacePlacement()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(30, 100, 205), 0.75f),
        ]);
        var water = fluids.GetId("asteria:water");
        var world = LoadedWorld();
        var voxel = new WorldVoxelCoord(3, 4, 4);
        Assert.True(world.SetFluidAt(
            voxel, FluidCell.Spreading(water, 5, 1), out _));
        Assert.Null(VoxelWorldRaycaster.Raycast(
            world, blocks, new Vector3(1.5f, 4.5f, 4.5f),
            Vector3.UnitX, 4f, includeFluidSources: true));

        Assert.True(world.SetFluidAt(voxel, FluidCell.Source(water), out _));
        var surface = VoxelWorldRaycaster.Raycast(
            world, blocks, new Vector3(3.5f, 6.5f, 4.5f),
            -Vector3.UnitY, 4f, includeFluidSurface: true);
        Assert.Equal(voxel, surface!.Value.Voxel);
        Assert.Equal(1, surface.Value.NormalY);
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
