using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelWorldTests
{
    [Fact]
    public void WorldReadsAndWritesAcrossNegativeChunkBoundaries()
    {
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(-1, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());

        var stone = new BlockRuntimeId(1);

        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(-1, 3, 4),
            stone,
            out var edit));

        Assert.Equal(new ChunkCoord(-1, 0, 0), edit.Chunk);
        Assert.Equal(new LocalVoxelCoord(15, 3, 4), edit.Local);
        Assert.Equal(
            stone,
            world.GetCellOrEmpty(
                new WorldVoxelCoord(-1, 3, 4)).Block);
    }

    [Fact]
    public void EditedChunkMovesToSessionArchiveAndRestoresExactRuntimeState()
    {
        var world = new VoxelWorld();
        var coord = ChunkCoord.Zero;
        var original = new Chunk();
        world.InsertChunk(coord, original);

        var block = new BlockRuntimeId(1);
        var position = new WorldVoxelCoord(2, 3, 4);
        var cell = new VoxelCell(
            block,
            textureRotation: TextureRotation.Degrees90,
            orientation: BlockOrientation.X,
            facing: HorizontalFacing.West,
            state: 37);

        Assert.True(world.SetCellAt(position, cell, out _));

        var local = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z).Local;
        var mask = MicroblockMask.Full.Edit(
            0,
            0,
            0,
            MicroblockResolution.Thick,
            occupied: false);
        original.SetMicroblockMask(
            local.X,
            local.Y,
            local.Z,
            mask);

        Assert.Equal(
            ChunkArchiveResult.ArchivedDirty,
            world.ArchiveChunk(coord));
        Assert.False(world.ContainsChunk(coord));
        Assert.True(world.HasArchivedChunk(coord));
        Assert.Equal(1, world.ArchivedChunkCount);

        Assert.Equal(
            ChunkRestoreResult.Restored,
            world.RestoreChunk(coord));

        Assert.True(world.ContainsChunk(coord));
        Assert.False(world.HasArchivedChunk(coord));
        Assert.Same(original, world.GetChunk(coord));

        var restored = world.GetCellOrEmpty(position);
        Assert.Equal(block, restored.Block);
        Assert.Equal(TextureRotation.Degrees90, restored.TextureRotation);
        Assert.Equal(BlockOrientation.X, restored.Orientation);
        Assert.Equal(HorizontalFacing.West, restored.Facing);
        Assert.Equal((ushort)37, restored.State);
        Assert.Equal(
            mask,
            world.GetMicroblockMaskOrEmpty(position));
    }

    [Fact]
    public void BlockAndFluidCannotOccupySameVoxel()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var position =
            new WorldVoxelCoord(2, 3, 4);
        var water =
            new FluidRuntimeId(1);

        Assert.True(
            world.SetFluidAt(
                position,
                FluidCell.Source(water),
                out _));
        Assert.True(
            world.SetBlockAt(
                position,
                new BlockRuntimeId(1),
                out _));
        Assert.True(
            world.GetFluidOrEmpty(position).IsEmpty);

        Assert.False(
            world.SetFluidAt(
                position,
                FluidCell.Source(water),
                out _));
    }

    [Fact]
    public void PristineChunkIsDroppedInsteadOfConsumingArchiveMemory()
    {
        var world = new VoxelWorld();
        var coord = ChunkCoord.Zero;
        world.InsertChunk(coord, new Chunk());

        Assert.Equal(
            ChunkArchiveResult.DroppedPristine,
            world.ArchiveChunk(coord));

        Assert.False(world.ContainsChunk(coord));
        Assert.False(world.HasArchivedChunk(coord));
        Assert.Equal(0, world.ArchivedChunkCount);
        Assert.Equal(
            ChunkRestoreResult.Missing,
            world.RestoreChunk(coord));
    }

    [Fact]
    public void DirectChunkMutationAfterMaterializationIsDetectedAtArchiveBoundary()
    {
        var world = new VoxelWorld();
        var coord = ChunkCoord.Zero;
        var chunk = new Chunk();
        world.InsertChunk(coord, chunk);

        chunk.SetBlock(
            3,
            3,
            3,
            new BlockRuntimeId(1));

        Assert.Equal(
            ChunkArchiveResult.ArchivedDirty,
            world.ArchiveChunk(coord));
        Assert.True(world.HasArchivedChunk(coord));
    }

    [Fact]
    public void MaterializationCannotOverwriteArchivedSessionState()
    {
        var world = new VoxelWorld();
        var coord = ChunkCoord.Zero;
        world.InsertChunk(coord, new Chunk());
        world.SetBlockAt(
            new WorldVoxelCoord(1, 1, 1),
            new BlockRuntimeId(1),
            out _);
        world.ArchiveChunk(coord);

        Assert.Throws<InvalidOperationException>(
            () => world.InsertChunk(coord, new Chunk()));
    }

    [Fact]
    public void ContentStampRejectsMutationAndReloadedResidency()
    {
        var world = new VoxelWorld();
        var coord = ChunkCoord.Zero;
        world.InsertChunk(coord, new Chunk());

        var initial =
            world.GetContentRevision(coord);
        var stamp =
            world.CaptureContentStamp([coord]);

        Assert.True(
            world.IsContentStampCurrent(stamp));

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(1, 1, 1),
                new BlockRuntimeId(1),
                out _));

        var edited =
            world.GetContentRevision(coord);

        Assert.Equal(
            initial.ResidencyEpoch,
            edited.ResidencyEpoch);
        Assert.NotEqual(
            initial.ChunkRevision,
            edited.ChunkRevision);
        Assert.False(
            world.IsContentStampCurrent(stamp));

        var editedStamp =
            world.CaptureContentStamp([coord]);

        Assert.Equal(
            ChunkArchiveResult.ArchivedDirty,
            world.ArchiveChunk(coord));
        Assert.False(
            world.TryGetContentRevision(
                coord,
                out _));

        Assert.Equal(
            ChunkRestoreResult.Restored,
            world.RestoreChunk(coord));

        var restored =
            world.GetContentRevision(coord);

        Assert.NotEqual(
            edited.ResidencyEpoch,
            restored.ResidencyEpoch);
        Assert.Equal(
            edited.ChunkRevision,
            restored.ChunkRevision);
        Assert.False(
            world.IsContentStampCurrent(
                editedStamp));
    }

    [Fact]
    public void WorkerCloneKeepsIndependentChunkStateAndRevision()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var revision = world.Revision;

        var clone = world.CloneForWorker();
        clone.SetBlockAt(
            new WorldVoxelCoord(1, 1, 1),
            new BlockRuntimeId(1),
            out _);

        Assert.Equal(revision, world.Revision);
        Assert.True(world.GetCellOrEmpty(
            new WorldVoxelCoord(1, 1, 1)).IsEmpty);
    }
}
