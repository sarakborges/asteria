using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelWorldSaveCodecTests
{
    private static (BlockRegistry Blocks, FluidRegistry Fluids,
        DyeRegistry Dyes, AttachedLayerRegistry Layers) Content() =>
        (
            new BlockRegistry([new BlockDefinition("asteria:stone")]),
            new FluidRegistry([
                new FluidDefinition("asteria:water", new FluidColor(5, 100, 240), 0.7f),
            ]),
            new DyeRegistry([]),
            new AttachedLayerRegistry([])
        );

    private static VoxelWorldSaveSnapshot Capture(
        VoxelWorld world,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        VoxelWorldSaveCodec.Capture(world,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    private static VoxelWorld Restore(
        VoxelWorldSaveSnapshot snapshot,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        VoxelWorldSaveCodec.Restore(snapshot,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    [Fact]
    public void CaptureAndRestorePreservesResidentArchivedDirtyAndPristineChunks()
    {
        var content = Content();
        var stone = content.Blocks.GetId("asteria:stone");
        var water = content.Fluids.GetId("asteria:water");

        var world = new VoxelWorld();
        var edited = new ChunkCoord(0, 0, 0);
        var pristineArchived = new ChunkCoord(1, 0, 0);
        var editedArchived = new ChunkCoord(2, 0, 0);
        var pristineResident = new ChunkCoord(3, 0, 0);

        world.InsertChunk(edited, new Chunk());
        var sourceRevision = world.Revision;
        Assert.True(world.SetBlockAt(new WorldVoxelCoord(1, 2, 3), stone, out _));
        world.InsertChunk(pristineArchived, new Chunk());
        world.InsertChunk(editedArchived, new Chunk());
        Assert.True(world.SetFluidAt(
            new WorldVoxelCoord(2 * Chunk.Size + 4, 1, 5),
            FluidCell.Spreading(water, 4, 2), out _));

        var generated = new Chunk();
        generated.SetBlock(5, 6, 7, stone);
        world.InsertChunk(pristineResident, generated);
        Assert.Equal(ChunkArchiveResult.ArchivedPristine, world.ArchiveChunk(pristineArchived));
        Assert.Equal(ChunkArchiveResult.ArchivedDirty, world.ArchiveChunk(editedArchived));

        var snapshot = Capture(world, content);
        Assert.Equal(4, snapshot.Chunks.Count);
        Assert.Equal(new[] { edited, pristineArchived, editedArchived, pristineResident },
            snapshot.Chunks.Select(x => x.Coordinate));
        Assert.Equal(new[] { true, false, false, true },
            snapshot.Chunks.Select(x => x.Resident));
        Assert.Equal(new[] { true, false, true, false },
            snapshot.Chunks.Select(x => x.Dirty));

        var restored = Restore(snapshot, content);
        Assert.Equal(2, restored.ChunkCount);
        Assert.Equal(2, restored.ArchivedChunkCount);
        Assert.Equal(2, restored.DirtyChunkCount);
        Assert.Equal(stone, restored.GetChunk(edited).GetBlock(1, 2, 3));
        Assert.Equal(stone, restored.GetChunk(pristineResident).GetBlock(5, 6, 7));
        Assert.True(restored.HasArchivedChunk(pristineArchived));
        Assert.True(restored.HasArchivedChunk(editedArchived));
        Assert.Throws<InvalidOperationException>(() =>
            restored.InsertChunk(pristineArchived, new Chunk()));
        Assert.Equal(ChunkRestoreResult.Restored, restored.RestoreChunk(pristineArchived));
        Assert.True(restored.GetChunk(pristineArchived).IsEmpty);
        Assert.Equal(ChunkRestoreResult.Restored, restored.RestoreChunk(editedArchived));
        Assert.Equal(FluidCell.Spreading(water, 4, 2),
            restored.GetChunk(editedArchived).GetFluid(4, 1, 5));

        // Capturing is observational; it must not change either source chunk.
        Assert.Equal(sourceRevision + 5, world.Revision);
        Assert.Equal(2, world.ChunkCount);
        Assert.Equal(2, world.ArchivedChunkCount);
        Assert.Equal(2, world.DirtyChunkCount);
    }

    [Fact]
    public void CaptureDoesNotMaterializeMissingChunkOrForgetArchivedEmpty()
    {
        var content = Content();
        var world = new VoxelWorld();
        var empty = new ChunkCoord(-3, 0, 5);
        world.InsertChunk(empty, new Chunk());
        world.ArchiveChunk(empty);

        var snapshot = Capture(world, content);
        Assert.Single(snapshot.Chunks);
        Assert.Equal(empty, snapshot.Chunks[0].Coordinate);
        Assert.False(snapshot.Chunks[0].Resident);
        Assert.False(snapshot.Chunks[0].Dirty);
        Assert.Equal(0, world.ChunkCount);

        var restored = Restore(snapshot, content);
        Assert.False(restored.ContainsChunk(empty));
        Assert.True(restored.HasArchivedChunk(empty));
        Assert.False(restored.ContainsChunk(new ChunkCoord(5, 0, 5)));
        Assert.Equal(ChunkRestoreResult.Restored, restored.RestoreChunk(empty));
        Assert.True(restored.GetChunk(empty).IsEmpty);
    }

    [Fact]
    public void RestoreRejectsCorruptedPayloadAndDuplicateLocationsWithoutWorldMutation()
    {
        var content = Content();
        var world = new VoxelWorld();
        var coord = new ChunkCoord(0, 0, 0);
        world.InsertChunk(coord, new Chunk());
        var original = Capture(world, content);
        var payload = original.Chunks[0].CopyEncodedChunk();
        payload[^1] ^= 1;
        var broken = new VoxelWorldSaveSnapshot([
            new SavedWorldChunk(coord, true, false, payload),
        ]);

        Assert.Throws<InvalidDataException>(() => Restore(broken, content));
        Assert.Throws<InvalidDataException>(() => new VoxelWorldSaveSnapshot([
            original.Chunks[0], original.Chunks[0],
        ]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SavedWorldChunk(new ChunkCoord(0, -1, 0), true, false,
                original.Chunks[0].CopyEncodedChunk()));

        Assert.Same(world.GetChunk(coord), world.GetChunk(coord));
        Assert.Equal(1, world.ChunkCount);
        Assert.Equal(0, world.ArchivedChunkCount);
    }

    [Fact]
    public void SnapshotIsDeterministicAndDoesNotExposeWritablePayload()
    {
        var content = Content();
        var coords = new[] {
            new ChunkCoord(1, 0, 0),
            new ChunkCoord(-1, 0, 0),
            new ChunkCoord(0, 1, 0),
        };
        var first = new VoxelWorld();
        var second = new VoxelWorld();

        foreach (var coord in coords)
            first.InsertChunk(coord, new Chunk());
        foreach (var coord in coords.Reverse())
            second.InsertChunk(coord, new Chunk());

        var a = Capture(first, content);
        var b = Capture(second, content);
        Assert.Equal(a.Chunks.Select(x => x.Coordinate),
            b.Chunks.Select(x => x.Coordinate));
        for (var i = 0; i < a.Chunks.Count; i++)
            Assert.Equal(a.Chunks[i].CopyEncodedChunk(), b.Chunks[i].CopyEncodedChunk());

        var detached = a.Chunks[0].CopyEncodedChunk();
        detached[0] ^= 1;
        Assert.Equal(a.Chunks[0].CopyEncodedChunk(), b.Chunks[0].CopyEncodedChunk());
        Assert.Equal(3, Restore(a, content).ChunkCount);
    }
}
