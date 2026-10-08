using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SpatialSaveStorageTests
{
    private static readonly WorldGenerationOptions Generation =
        new(WorldGenerationMode.Flat, "asteria:overworld/plains",
            biomeSizeTenths: 18, spawnStructures: false,
            singleBiome: true, spawnCaves: false, spawnOceans: true);

    private static DimensionChunkSaveSnapshot Snapshot(ulong seed, bool withChunk = true)
    {
        var chunks = withChunk ? new SavedWorldChunk[]
        {
            new(new ChunkCoord(-4, 0, 2), resident: false, dirty: true,
                ChunkSaveCodec.Encode(new Chunk(),
                    new BlockRegistry([]), new FluidRegistry([]),
                    new DyeRegistry([]), new AttachedLayerRegistry([])))
        } : [];

        return new DimensionChunkSaveSnapshot(seed, Generation,
        [
            new SavedSphereChunks(DimensionId.Overworld,
                new VoxelWorldSaveSnapshot(chunks)),
            new SavedSphereChunks(new DimensionId("asteria:umbral"),
                new VoxelWorldSaveSnapshot([]))
        ]);
    }

    [Fact]
    public void FileCodecRoundTripsDeterministicMultipleSphereSpatialData()
    {
        var source = Snapshot(ulong.MaxValue);
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        DimensionChunkFileCodec.Write(first, source);
        DimensionChunkFileCodec.Write(second, source);
        Assert.Equal(first.ToArray(), second.ToArray());

        first.Position = 0;
        var loaded = DimensionChunkFileCodec.Read(first, first.Length);
        Assert.Equal(source.WorldSeed, loaded.WorldSeed);
        Assert.Equal(source.Generation, loaded.Generation);
        Assert.Equal(
            source.Spheres.Select(sphere => sphere.Dimension.Value),
            loaded.Spheres.Select(sphere => sphere.Dimension.Value));
        var saved = Assert.Single(loaded.Spheres[0].Chunks.Chunks);
        Assert.Equal(new ChunkCoord(-4, 0, 2), saved.Coordinate);
        Assert.False(saved.Resident);
        Assert.True(saved.Dirty);
        Assert.Equal(source.Spheres[0].Chunks.Chunks[0].CopyEncodedChunk(),
            saved.CopyEncodedChunk());
    }

    [Fact]
    public void PublishCreatesGenerationsAndRecoversFromDamagedLatest()
    {
        WithDirectory(dir =>
        {
            Assert.Equal(1UL, SpatialSaveStorage.Publish(dir, Snapshot(123)));
            Assert.Equal(2UL, SpatialSaveStorage.Publish(dir, Snapshot(456)));
            Assert.Equal(456UL, SpatialSaveStorage.ReadLatest(dir).WorldSeed);

            var newest = Path.Combine(dir, SpatialSaveStorage.GenerationFileName(2));
            var bytes = File.ReadAllBytes(newest);
            bytes[12] ^= 0x02;
            File.WriteAllBytes(newest, bytes);

            Assert.Equal(123UL, SpatialSaveStorage.ReadLatest(dir).WorldSeed);
            Assert.True(File.Exists(Path.Combine(
                dir, SpatialSaveStorage.GenerationFileName(1))));
        });
    }

    [Fact]
    public void TruncatedLatestAndUnpublishedTemporaryAreIgnored()
    {
        WithDirectory(dir =>
        {
            SpatialSaveStorage.Publish(dir, Snapshot(111));
            SpatialSaveStorage.Publish(dir, Snapshot(222));
            var newest = Path.Combine(dir, SpatialSaveStorage.GenerationFileName(2));
            File.WriteAllBytes(newest, [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(dir,
                SpatialSaveStorage.GenerationFileName(9) + ".crash.tmp"), [1]);

            Assert.Equal(111UL, SpatialSaveStorage.ReadLatest(dir).WorldSeed);
            Assert.False(File.Exists(Path.Combine(dir,
                SpatialSaveStorage.GenerationFileName(9))));
        });
    }

    [Fact]
    public void RetainsOnlyFourCommittedGenerationsAndLeavesNoStagingFiles()
    {
        WithDirectory(dir =>
        {
            for (ulong i = 1; i <= 6; i++)
                Assert.Equal(i, SpatialSaveStorage.Publish(dir, Snapshot(i, withChunk: false)));

            Assert.Equal(6UL, SpatialSaveStorage.ReadLatest(dir).WorldSeed);
            Assert.False(File.Exists(Path.Combine(
                dir, SpatialSaveStorage.GenerationFileName(1))));
            Assert.False(File.Exists(Path.Combine(
                dir, SpatialSaveStorage.GenerationFileName(2))));
            for (ulong i = 3; i <= 6; i++)
                Assert.True(File.Exists(Path.Combine(
                    dir, SpatialSaveStorage.GenerationFileName(i))));
            Assert.Empty(Directory.EnumerateFiles(dir, "*.tmp"));
        });
    }

    [Fact]
    public void ReaderRejectsMissingOrFullyDamagedGenerations()
    {
        WithDirectory(dir =>
        {
            Assert.Throws<FileNotFoundException>(() => SpatialSaveStorage.ReadLatest(dir));
            SpatialSaveStorage.Publish(dir, Snapshot(1));
            var file = Path.Combine(dir, SpatialSaveStorage.GenerationFileName(1));
            File.WriteAllBytes(file, [1, 2, 3]);
            Assert.Throws<InvalidDataException>(() => SpatialSaveStorage.ReadLatest(dir));
        });
    }

    [Fact]
    public void FileCodecRejectsInvalidBooleanTrailingBytesAndTruncation()
    {
        var source = Snapshot(32);
        using var stream = new MemoryStream();
        DimensionChunkFileCodec.Write(stream, source);
        var bytes = stream.ToArray();

        bytes[0] ^= 1;
        using var invalidMagic = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() =>
            DimensionChunkFileCodec.Read(invalidMagic, invalidMagic.Length));

        using var padded = new MemoryStream();
        DimensionChunkFileCodec.Write(padded, source);
        padded.WriteByte(0);
        padded.Position = 0;
        Assert.Throws<InvalidDataException>(() =>
            DimensionChunkFileCodec.Read(padded, padded.Length));

        using var truncated = new MemoryStream(padded.ToArray()[..^20]);
        Assert.ThrowsAny<InvalidDataException>(() =>
            DimensionChunkFileCodec.Read(truncated, truncated.Length));
    }

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "asteria-spatial-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
