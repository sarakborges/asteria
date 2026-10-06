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
                new WorldVoxelCoord(0, 1, 0),
                1,
                0,
                0);

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(1, 0, 0),
                blocks.GetId("asteria:stone"),
                out _));

        var decision =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                hit,
                new VoxelCell(layer),
                new WorldAabb(
                    new Vector3(
                        1.1f,
                        1.5f,
                        0.1f),
                    new Vector3(
                        1.9f,
                        2.8f,
                        0.9f)));

        Assert.True(decision.Accepted);
        Assert.Equal(
            new WorldVoxelCoord(1, 1, 0),
            decision.Position);
    }

    [Fact]
    public void SupportBelowPlacementRequiresOccupiedSupport()
    {
        var blocks = CreateBlocks();
        var stone =
            blocks.GetId("asteria:stone");
        var layer =
            blocks.GetId("asteria:snow_layer");
        var world = LoadedWorld();
        var hit =
            new VoxelWorldHit(
                new WorldVoxelCoord(0, 1, 0),
                1,
                0,
                0);

        var unsupported =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                hit,
                new VoxelCell(layer),
                FarPlayerBounds());

        Assert.False(unsupported.Accepted);
        Assert.Equal(
            BlockPlacementRejection.MissingSupport,
            unsupported.Rejection);

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(1, 0, 0),
                stone,
                out _));

        var supported =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                hit,
                new VoxelCell(layer),
                FarPlayerBounds());

        Assert.True(supported.Accepted);
    }

    [Fact]
    public void SupportBelowPlacementRejectsThinLayerThatDoesNotReachTopFace()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:base_layer",
                    shape:
                        BlockShapeDefinition.SurfaceLayer(
                            0.125f,
                            "asteria:stone")),
                new BlockDefinition(
                    "asteria:snow_layer",
                    tags:
                    [
                        BlockPhysicsCapabilities.SupportBelow,
                    ],
                    shape:
                        BlockShapeDefinition.SurfaceLayer(
                            0.125f,
                            "asteria:stone")),
            ]);
        var world = LoadedWorld();
        var target =
            new WorldVoxelCoord(
                1,
                1,
                0);

        Assert.True(
            world.SetBlockAt(
                target + (0, -1, 0),
                blocks.GetId(
                    "asteria:base_layer"),
                out _));

        var decision =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                new VoxelWorldHit(
                    new WorldVoxelCoord(
                        0,
                        1,
                        0),
                    1,
                    0,
                    0),
                new VoxelCell(
                    blocks.GetId(
                        "asteria:snow_layer")),
                FarPlayerBounds());

        Assert.False(decision.Accepted);
        Assert.Equal(
            BlockPlacementRejection.MissingSupport,
            decision.Rejection);
    }

    [Fact]
    public void SupportBelowPlacementRequiresFullFootprintCoverage()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:micro"),
                new BlockDefinition(
                    "asteria:snow_layer",
                    tags:
                    [
                        BlockPhysicsCapabilities.SupportBelow,
                    ],
                    shape:
                        BlockShapeDefinition.SurfaceLayer(
                            0.125f,
                            "asteria:micro")),
            ]);
        var world = LoadedWorld();
        var support =
            new WorldVoxelCoord(
                1,
                0,
                0);

        Assert.True(
            world.SetBlockAt(
                support,
                blocks.GetId("asteria:micro"),
                out _));

        var address =
            VoxelCoordinates.FromWorld(
                support.X,
                support.Y,
                support.Z);
        var chunk =
            world.GetChunk(address.Chunk);

        Assert.True(
            chunk.SetMicroblockMask(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                MicroblockMask.Empty.Edit(
                    0,
                    MicroblockMask.Edge - 1,
                    0,
                    MicroblockResolution.ExtraThin,
                    occupied: true)));

        var hit =
            new VoxelWorldHit(
                new WorldVoxelCoord(
                    0,
                    1,
                    0),
                1,
                0,
                0);
        var candidate =
            new VoxelCell(
                blocks.GetId(
                    "asteria:snow_layer"));

        var partial =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                hit,
                candidate,
                FarPlayerBounds());

        Assert.False(partial.Accepted);
        Assert.Equal(
            BlockPlacementRejection.MissingSupport,
            partial.Rejection);

        Assert.True(
            chunk.SetMicroblockMask(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                MicroblockMask.Full));

        var full =
            BlockInteractionResolver.ResolvePlacement(
                world,
                blocks,
                hit,
                candidate,
                FarPlayerBounds());

        Assert.True(full.Accepted);
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
                tags:
                [
                    BlockPhysicsCapabilities.SupportBelow,
                ],
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
