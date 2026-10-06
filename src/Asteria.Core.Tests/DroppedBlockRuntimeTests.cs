using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DroppedBlockRuntimeTests
{
    [Fact]
    public void DroppedBlockFallsSettlesAndWakesWhenSupportChanges()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var world = LoadedWorld();
        var support =
            new WorldVoxelCoord(2, 0, 2);

        Assert.True(
            world.SetBlockAt(
                support,
                stone,
                out _));

        var runtime =
            new DroppedBlockRuntime(
                world,
                blocks);
        runtime.Spawn(
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone)),
            new Vector3(
                2.5f,
                3.5f,
                2.5f));

        for (var frame = 0;
             frame < 240;
             frame++)
        {
            runtime.Advance(
                1.0 / 60.0,
                18.0);

            if (runtime.ActiveBlocks
                .Single()
                .IsSettled)
            {
                break;
            }
        }

        var settled =
            Assert.Single(
                runtime.ActiveBlocks);

        Assert.True(settled.IsSettled);
        Assert.InRange(
            settled.Position.Y,
            1.17f,
            1.30f);

        Assert.True(
            world.SetBlockAt(
                support,
                BlockRuntimeId.Air,
                out _));
        runtime.NotifyVoxelEdit(support);

        var awakened =
            Assert.Single(
                runtime.ActiveBlocks);

        Assert.False(awakened.IsSettled);

        var previousY =
            awakened.Position.Y;
        runtime.Advance(
            1.0 / 60.0,
            18.0);

        Assert.True(
            runtime.ActiveBlocks
                .Single()
                .Position.Y <
            previousY);
    }

    [Fact]
    public void OverlappingDropsSeparateDeterministically()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var runtime =
            new DroppedBlockRuntime(
                LoadedWorld(),
                blocks);
        var block =
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone));
        var center =
            new Vector3(
                4.5f,
                4.5f,
                4.5f);

        var first =
            runtime.Spawn(
                block,
                center);
        var second =
            runtime.Spawn(
                block,
                center);

        runtime.Advance(
            1.0 / 60.0,
            gravityStrength: 0.0);

        var states =
            runtime.ActiveBlocks
                .ToDictionary(
                    state => state.Id);

        Assert.False(
            DroppedBlockRuntime.BoundsAt(
                    states[first].Position)
                .Intersects(
                    DroppedBlockRuntime.BoundsAt(
                        states[second].Position)));
        Assert.True(
            states[first].Position.X <
            states[second].Position.X);
    }

    [Fact]
    public void ContactResolutionDoesNotPushDropIntoSolidVoxel()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var world =
            LoadedWorld();

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(
                    3,
                    4,
                    4),
                stone,
                out _));

        var runtime =
            new DroppedBlockRuntime(
                world,
                blocks);
        var block =
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone));

        runtime.Spawn(
            block,
            new Vector3(
                4.18f,
                4.5f,
                4.5f));
        runtime.Spawn(
            block,
            new Vector3(
                4.34f,
                4.5f,
                4.5f));

        runtime.Advance(
            1.0 / 60.0,
            gravityStrength: 0.0);

        foreach (var state in
                 runtime.ActiveBlocks)
        {
            Assert.True(
                VoxelWorldCollision
                    .QueryDetailed(
                        world,
                        blocks,
                        DroppedBlockRuntime.BoundsAt(
                            state.Position))
                    .IsClear);
        }
    }

    [Fact]
    public void CapacityEvictsOldestDropDeterministically()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var runtime =
            new DroppedBlockRuntime(
                LoadedWorld(),
                blocks,
                maximumActive: 2);
        var block =
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone));

        var first =
            runtime.Spawn(
                block,
                new Vector3(1.5f, 1.5f, 1.5f));
        var second =
            runtime.Spawn(
                block,
                new Vector3(2.5f, 1.5f, 1.5f));
        var third =
            runtime.Spawn(
                block,
                new Vector3(3.5f, 1.5f, 1.5f));

        var ids =
            runtime.ActiveBlocks
                .Select(state => state.Id)
                .ToArray();

        Assert.Equal(2, ids.Length);
        Assert.DoesNotContain(first, ids);
        Assert.Equal(
            [second, third],
            ids);
    }

    [Fact]
    public void LifetimeExpiresDropsEvenWhenWorldIsUnloaded()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var runtime =
            new DroppedBlockRuntime(
                new VoxelWorld(),
                blocks,
                lifetimeSeconds: 0.1);

        runtime.Spawn(
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone)),
            new Vector3(
                20.5f,
                20.5f,
                20.5f));

        var result =
            runtime.Advance(
                0.2,
                18.0);

        Assert.Equal(1, result.Expired);
        Assert.Equal(0, runtime.ActiveCount);
    }

    private static VoxelWorld LoadedWorld()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        return world;
    }
}
