using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockInteractionResolverTests
{
    [Fact]
    public void BreakDecisionReturnsAuthoritativeTargetCell()
    {
        var blocks = CreateBlocks();
        var stone = blocks.GetId("asteria:stone");
        var world = LoadedWorld();
        var position =
            new WorldVoxelCoord(2, 3, 4);

        Assert.True(
            world.SetBlockAt(
                position,
                stone,
                out _));

        var decision =
            BlockInteractionResolver.ResolveBreak(
                world,
                new VoxelWorldHit(
                    position,
                    0,
                    0,
                    0));

        Assert.True(decision.Accepted);
        Assert.Equal(position, decision.Position);
        Assert.Equal(stone, decision.Cell.Block);
    }

    [Fact]
    public void PlacementRequiresCardinalSurfaceAndEmptyLoadedTarget()
    {
        var blocks = CreateBlocks();
        var stone = blocks.GetId("asteria:stone");
        var world = LoadedWorld();
        var hit =
            new VoxelWorldHit(
                new WorldVoxelCoord(1, 1, 1),
                0,
                0,
                0);

        var invalid =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                hit,
                new VoxelCell(stone),
                FarPlayerBounds());

        Assert.False(invalid.Accepted);
        Assert.Equal(
            BlockPlacementRejection.InvalidSurfaceNormal,
            invalid.Rejection);

        hit =
            new VoxelWorldHit(
                new WorldVoxelCoord(1, 1, 1),
                1,
                0,
                0);
        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(2, 1, 1),
                stone,
                out _));

        var occupied =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                hit,
                new VoxelCell(stone),
                FarPlayerBounds());

        Assert.False(occupied.Accepted);
        Assert.Equal(
            BlockPlacementRejection.Occupied,
            occupied.Rejection);
    }

    [Fact]
    public void CubePlacementRejectsPlayerIntersection()
    {
        var blocks = CreateBlocks();
        var stone = blocks.GetId("asteria:stone");
        var world = LoadedWorld();
        var hit =
            new VoxelWorldHit(
                new WorldVoxelCoord(0, 0, 0),
                1,
                0,
                0);

        var decision =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                hit,
                new VoxelCell(stone),
                new WorldAabb(
                    new Vector3(
                        1.2f,
                        0f,
                        0.2f),
                    new Vector3(
                        1.8f,
                        1.8f,
                        0.8f)));

        Assert.False(decision.Accepted);
        Assert.Equal(
            BlockPlacementRejection.PlayerIntersection,
            decision.Rejection);
    }

    [Fact]
    public void SurfaceLayerUsesActualGeometryForPlayerOverlap()
    {
        var blocks = CreateBlocks();
        var layer =
            blocks.GetId("asteria:snow_layer");
        var world = LoadedWorld();
        var hit =
            new VoxelWorldHit(
                new WorldVoxelCoord(0, 0, 0),
                1,
                0,
                0);

        var decision =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                hit,
                new VoxelCell(layer),
                new WorldAabb(
                    new Vector3(
                        1.1f,
                        0.5f,
                        0.1f),
                    new Vector3(
                        1.9f,
                        1.8f,
                        0.9f)));

        Assert.True(decision.Accepted);
        Assert.Equal(
            new WorldVoxelCoord(1, 0, 0),
            decision.Position);
    }

    [Fact]
    public void FluidDoesNotMakeOtherwiseEmptyPlacementVoxelOccupied()
    {
        var blocks = CreateBlocks();
        var stone = blocks.GetId("asteria:stone");
        var world = LoadedWorld();
        var target =
            new WorldVoxelCoord(1, 1, 0);

        Assert.True(
            world.SetFluidAt(
                target,
                FluidCell.Source(
                    new FluidRuntimeId(1)),
                out _));

        var decision =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                new VoxelWorldHit(
                    new WorldVoxelCoord(0, 1, 0),
                    1,
                    0,
                    0),
                new VoxelCell(stone),
                FarPlayerBounds());

        Assert.True(decision.Accepted);
    }

    private static VoxelWorld LoadedWorld()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        return world;
    }

    private static BlockRegistry CreateBlocks() =>
        new(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition(
                "asteria:snow_layer",
                shape:
                    BlockShapeDefinition.SurfaceLayer(
                        0.125f,
                        "asteria:stone")),
        ]);

    private static WorldAabb FarPlayerBounds() =>
        new(
            new Vector3(20f, 20f, 20f),
            new Vector3(21f, 22f, 21f));
}
