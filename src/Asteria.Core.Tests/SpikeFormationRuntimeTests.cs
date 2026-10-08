using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SpikeFormationRuntimeTests
{
    private static readonly WorldAabb AwayFromPlayer =
        new(new Vector3(100, 100, 100),
            new Vector3(101, 102, 101));

    [Fact]
    public void BuildingUpwardConnectsAndRetapersAndMiningMiddleDetachesTip()
    {
        var f = Fixture();
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:stone_spike");
        var support = new WorldVoxelCoord(2, 2, 2);
        Assert.True(f.Mutations.SetBlockAt(support, stone, out _));

        for (var i = 0; i < 3; i++)
        {
            var hit = Hit(2, 2 + i, 2, normalY: 1);
            var decision = f.Interactions.Place(hit,
                new VoxelCell(spike), AwayFromPlayer);
            Assert.True(decision.Accepted);
            Assert.Equal(3 + i, decision.Position.Y);
        }

        for (var i = 0; i < 3; i++)
        {
            var cell = f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 3 + i, 2));
            Assert.Equal(spike, cell.Block);
            Assert.Equal(i, SpikeSegmentState.Index(cell.State));
            Assert.Equal(3, SpikeSegmentState.Height(cell.State));
            Assert.False(SpikeSegmentState.IsDown(cell.State));
        }

        var root = f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 3, 2));
        var top = f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 5, 2));
        var shape = f.Blocks.GetDefinition(spike).Shape;
        Assert.True(SpikeSegmentState.RadiusAt(shape, root.State, 0f) >
                    SpikeSegmentState.RadiusAt(shape, top.State, 1f));

        Assert.True(f.Interactions.Break(Hit(2, 4, 2)).Accepted);
        var surviving = f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 3, 2));
        Assert.Equal(spike, surviving.Block);
        Assert.Equal(1, SpikeSegmentState.Height(surviving.State));
        Assert.True(f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 4, 2)).IsEmpty);
        Assert.True(f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 5, 2)).IsEmpty);
        Assert.Equal(2, f.Drops.ActiveCount);
        Assert.All(f.Drops.ActiveBlocks, drop =>
        {
            Assert.Equal(spike, drop.Block!.Cell.Block);
            Assert.Equal((ushort)0, drop.Block.Cell.State);
        });
    }

    [Fact]
    public void CeilingPlacementAndDropsIgnorePickedSegmentProfile()
    {
        var f = Fixture();
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:ice_spike");
        Assert.True(f.Mutations.SetBlockAt(
            new WorldVoxelCoord(3, 10, 3), stone, out _));

        for (var i = 0; i < 3; i++)
        {
            var held = new VoxelCell(spike,
                state: SpikeSegmentState.Encode(5, 8, false));
            var result = f.Interactions.Place(
                Hit(3, 10 - i, 3, normalY: -1),
                held, AwayFromPlayer);
            Assert.True(result.Accepted);
            Assert.True(SpikeSegmentState.IsDown(result.Cell.State));
        }

        for (var i = 0; i < 3; i++)
        {
            var cell = f.World.GetCellOrEmpty(
                new WorldVoxelCoord(3, 9 - i, 3));
            Assert.Equal(i, SpikeSegmentState.Index(cell.State));
            Assert.Equal(3, SpikeSegmentState.Height(cell.State));
            Assert.True(SpikeSegmentState.IsDown(cell.State));
        }

        Assert.True(f.Interactions.Break(Hit(3, 7, 3)).Accepted);
        Assert.Equal(2, SpikeSegmentState.Height(
            f.World.GetCellOrEmpty(new WorldVoxelCoord(3, 9, 3)).State));
        Assert.All(f.Drops.ActiveBlocks, drop =>
            Assert.Equal((ushort)0, drop.Block!.Cell.State));
    }

    [Fact]
    public void BreakingSupportViaMutationDetachesEntireFormation()
    {
        var f = Fixture();
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:basalt_spike");
        var support = new WorldVoxelCoord(4, 2, 4);
        Assert.True(f.Mutations.SetBlockAt(support, stone, out _));

        for (var i = 0; i < 3; i++)
            Assert.True(f.Interactions.Place(
                Hit(4, 2 + i, 4, normalY: 1),
                new VoxelCell(spike), AwayFromPlayer).Accepted);

        // External mutations, not only the player's Break, must invalidate
        // unsupported formations through the canonical mutation event.
        Assert.True(f.Mutations.SetCellAt(
            support, VoxelCell.Empty, out _));
        for (var y = 3; y <= 5; y++)
            Assert.True(f.World.GetCellOrEmpty(
                new WorldVoxelCoord(4, y, 4)).IsEmpty);
        Assert.Equal(3, f.Drops.ActiveCount);
        Assert.All(f.Drops.ActiveBlocks, drop =>
        {
            Assert.Equal(spike, drop.Block!.Cell.Block);
            Assert.Equal((ushort)0, drop.Block.Cell.State);
        });
    }

    [Fact]
    public void CreativeBreakOfSupportSuppressesAllDetachedDrops()
    {
        var f = Fixture();
        var support = new WorldVoxelCoord(4, 9, 4);
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:stone_spike");
        Assert.True(f.Mutations.SetBlockAt(support, stone, out _));
        Assert.True(f.Interactions.Place(
            Hit(4, 9, 4, normalY: -1),
            new VoxelCell(spike), AwayFromPlayer).Accepted);
        Assert.True(f.Interactions.Place(
            Hit(4, 8, 4, normalY: -1),
            new VoxelCell(spike), AwayFromPlayer).Accepted);

        Assert.True(f.Interactions.Break(
            Hit(4, 9, 4), BlockBreakLootPolicy.Suppress).Accepted);
        Assert.True(f.World.GetCellOrEmpty(support).IsEmpty);
        Assert.True(f.World.GetCellOrEmpty(new WorldVoxelCoord(4, 8, 4)).IsEmpty);
        Assert.True(f.World.GetCellOrEmpty(new WorldVoxelCoord(4, 7, 4)).IsEmpty);
        Assert.Equal(0, f.Drops.ActiveCount);
    }

    [Fact]
    public void ProfilesConnectAcrossVerticalChunkSeams()
    {
        var f = Fixture(verticalChunks: 2);
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:sandstone_spike");
        Assert.True(f.Mutations.SetBlockAt(
            new WorldVoxelCoord(1, 14, 1), stone, out _));

        Assert.True(f.Interactions.Place(
            Hit(1, 14, 1, normalY: 1),
            new VoxelCell(spike), AwayFromPlayer).Accepted);
        Assert.True(f.Interactions.Place(
            Hit(1, 15, 1, normalY: 1),
            new VoxelCell(spike), AwayFromPlayer).Accepted);

        var lower = f.World.GetCellOrEmpty(new WorldVoxelCoord(1, 15, 1));
        var upper = f.World.GetCellOrEmpty(new WorldVoxelCoord(1, 16, 1));
        Assert.Equal(2, SpikeSegmentState.Height(lower.State));
        Assert.Equal(2, SpikeSegmentState.Height(upper.State));
        Assert.Equal(0, SpikeSegmentState.Index(lower.State));
        Assert.Equal(1, SpikeSegmentState.Index(upper.State));

        Assert.True(f.Interactions.Break(Hit(1, 16, 1)).Accepted);
        Assert.Equal(1, SpikeSegmentState.Height(
            f.World.GetCellOrEmpty(new WorldVoxelCoord(1, 15, 1)).State));
    }

    [Fact]
    public void MissingAdjacentChunkRejectsExtensionWithoutMutation()
    {
        var f = Fixture();
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:stone_spike");
        Assert.True(f.Mutations.SetBlockAt(
            new WorldVoxelCoord(1, 14, 1), stone, out _));
        Assert.True(f.Interactions.Place(
            Hit(1, 14, 1, normalY: 1),
            new VoxelCell(spike), AwayFromPlayer).Accepted);

        var result = f.Interactions.Place(
            Hit(1, 15, 1, normalY: 1),
            new VoxelCell(spike), AwayFromPlayer);
        Assert.False(result.Accepted);
        Assert.Equal(BlockPlacementRejection.Unloaded, result.Rejection);
        Assert.Equal(1, SpikeSegmentState.Height(
            f.World.GetCellOrEmpty(new WorldVoxelCoord(1, 15, 1)).State));
    }

    [Fact]
    public void ArchivedTipRemainsUntouchedUntilItsChunkIsRestored()
    {
        var f = Fixture(verticalChunks: 2);
        var support = new WorldVoxelCoord(2, 13, 2);
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:stone_spike");
        Assert.True(f.Mutations.SetBlockAt(support, stone, out _));

        for (var i = 0; i < 3; i++)
            Assert.True(f.Interactions.Place(
                Hit(2, 13 + i, 2, normalY: 1),
                new VoxelCell(spike), AwayFromPlayer).Accepted);

        var archivedTip = new WorldVoxelCoord(2, 16, 2);
        Assert.Equal(ChunkArchiveResult.ArchivedDirty,
            f.World.ArchiveChunk(new ChunkCoord(0, 1, 0)));
        Assert.True(f.Mutations.SetCellAt(
            new WorldVoxelCoord(2, 15, 2), VoxelCell.Empty, out _));

        // Old root and archived tip must not be destroyed based on a
        // missing chunk. Recovery happens after residency is restored.
        Assert.Equal(spike, f.World.GetCellOrEmpty(
            new WorldVoxelCoord(2, 14, 2)).Block);
        Assert.Equal(0, f.Drops.ActiveCount);

        Assert.Equal(ChunkRestoreResult.Restored,
            f.World.RestoreChunk(new ChunkCoord(0, 1, 0)));
        Assert.Equal(1, SpikeSegmentState.Height(
            f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 14, 2)).State));
        Assert.True(f.World.GetCellOrEmpty(archivedTip).IsEmpty);
        Assert.Single(f.Drops.ActiveBlocks);
        Assert.Equal((ushort)0,
            Assert.Single(f.Drops.ActiveBlocks).Block!.Cell.State);
    }

    [Fact]
    public void RestoringArchivedRootDetachesWhenSupportWasRemoved()
    {
        var f = Fixture(verticalChunks: 2);
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:ice_spike");
        var support = new WorldVoxelCoord(2, 15, 2);
        Assert.True(f.Mutations.SetBlockAt(support, stone, out _));
        Assert.True(f.Interactions.Place(
            Hit(2, 15, 2, normalY: 1),
            new VoxelCell(spike), AwayFromPlayer).Accepted);

        Assert.Equal(ChunkArchiveResult.ArchivedDirty,
            f.World.ArchiveChunk(new ChunkCoord(0, 1, 0)));
        Assert.True(f.Mutations.SetCellAt(support, VoxelCell.Empty, out _));
        Assert.Equal(0, f.Drops.ActiveCount);

        Assert.Equal(ChunkRestoreResult.Restored,
            f.World.RestoreChunk(new ChunkCoord(0, 1, 0)));
        Assert.True(f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 16, 2)).IsEmpty);
        Assert.Equal(spike, Assert.Single(f.Drops.ActiveBlocks).Block!.Cell.Block);
    }

    [Fact]
    public void RestoringArchivedTipDetachesWhenRootLostItsSupport()
    {
        var f = Fixture(verticalChunks: 2);
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:stone_spike");
        var support = new WorldVoxelCoord(2, 13, 2);
        Assert.True(f.Mutations.SetBlockAt(support, stone, out _));
        for (var i = 0; i < 3; i++)
            Assert.True(f.Interactions.Place(
                Hit(2, 13 + i, 2, normalY: 1),
                new VoxelCell(spike), AwayFromPlayer).Accepted);

        Assert.Equal(ChunkArchiveResult.ArchivedDirty,
            f.World.ArchiveChunk(new ChunkCoord(0, 1, 0)));
        Assert.True(f.Mutations.SetCellAt(support, VoxelCell.Empty, out _));
        Assert.Equal(0, f.Drops.ActiveCount);

        Assert.Equal(ChunkRestoreResult.Restored,
            f.World.RestoreChunk(new ChunkCoord(0, 1, 0)));
        for (var y = 14; y <= 16; y++)
            Assert.True(f.World.GetCellOrEmpty(
                new WorldVoxelCoord(2, y, 2)).IsEmpty);
        Assert.Equal(3, f.Drops.ActiveCount);
    }

    [Fact]
    public void RestoringArchivedChunkWithIntactFormationIsNonMutating()
    {
        var f = Fixture(verticalChunks: 2);
        var stone = f.Blocks.GetId("asteria:stone");
        var spike = f.Blocks.GetId("asteria:sandstone_spike");
        Assert.True(f.Mutations.SetBlockAt(
            new WorldVoxelCoord(2, 14, 2), stone, out _));
        for (var i = 0; i < 2; i++)
            Assert.True(f.Interactions.Place(
                Hit(2, 14 + i, 2, normalY: 1),
                new VoxelCell(spike), AwayFromPlayer).Accepted);

        var before = f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 15, 2));
        Assert.Equal(ChunkArchiveResult.ArchivedDirty,
            f.World.ArchiveChunk(new ChunkCoord(0, 1, 0)));
        Assert.Equal(ChunkRestoreResult.Restored,
            f.World.RestoreChunk(new ChunkCoord(0, 1, 0)));
        Assert.Equal(before,
            f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 15, 2)));
        Assert.Equal(0, f.Drops.ActiveCount);
    }

    [Fact]
    public void DifferentMaterialDoesNotJoinExistingSpikeColumn()
    {
        var f = Fixture();
        var stone = f.Blocks.GetId("asteria:stone");
        var stoneSpike = f.Blocks.GetId("asteria:stone_spike");
        var iceSpike = f.Blocks.GetId("asteria:ice_spike");
        Assert.True(f.Mutations.SetBlockAt(
            new WorldVoxelCoord(2, 2, 2), stone, out _));
        Assert.True(f.Interactions.Place(
            Hit(2, 2, 2, normalY: 1),
            new VoxelCell(stoneSpike), AwayFromPlayer).Accepted);

        var result = f.Interactions.Place(
            Hit(2, 3, 2, normalY: 1),
            new VoxelCell(iceSpike), AwayFromPlayer);
        Assert.False(result.Accepted);
        Assert.Equal(1, SpikeSegmentState.Height(
            f.World.GetCellOrEmpty(new WorldVoxelCoord(2, 3, 2)).State));
    }

    private static VoxelWorldHit Hit(
        int x, int y, int z, int normalY = 0) =>
        new(new WorldVoxelCoord(x, y, z),
            0, normalY, 0);

    private static TestFixture Fixture(int verticalChunks = 1)
    {
        var world = new VoxelWorld();
        for (var y = 0; y < verticalChunks; y++)
            world.InsertChunk(new ChunkCoord(0, y, 0), new Chunk());

        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:stone_spike",
                shape: BlockShapeDefinition.Spike()),
            new BlockDefinition("asteria:ice_spike",
                shape: BlockShapeDefinition.Spike()),
            new BlockDefinition("asteria:basalt_spike",
                shape: BlockShapeDefinition.Spike()),
            new BlockDefinition("asteria:sandstone_spike",
                shape: BlockShapeDefinition.Spike()),
        ]);
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var interactions = new BlockInteractionRuntime(
            world, blocks, mutations, drops);
        return new TestFixture(world, blocks, mutations, drops, interactions);
    }

    private sealed record TestFixture(
        VoxelWorld World,
        BlockRegistry Blocks,
        VoxelMutationRuntime Mutations,
        DroppedBlockRuntime Drops,
        BlockInteractionRuntime Interactions);
}
