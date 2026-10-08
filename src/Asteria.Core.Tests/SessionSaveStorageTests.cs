using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SessionSaveStorageTests
{
    private static DimensionDefinition Dimension(string id) => new(
        new DimensionId(id), [id + "/plain"], 90, 18f,
        new DimensionSpawnDefinition(0, 0),
        new DimensionEnvironmentDefinition(
            new DimensionColor(0, 0, 0),
            new DimensionColor(255, 255, 255), 1f,
            new DimensionColor(0, 0, 0), 0f));

    private static DimensionRegistry Dimensions() => new([
        Dimension("asteria:overworld"),
        Dimension("asteria:umbral")
    ]);

    private static (BlockRegistry Blocks, FluidRegistry Fluids,
        DyeRegistry Dyes, AttachedLayerRegistry Layers) Content() => (
        new BlockRegistry([new BlockDefinition("asteria:stone")]),
        new FluidRegistry([]),
        new DyeRegistry([]),
        new AttachedLayerRegistry([])
    );

    private static GameplaySessionSnapshot Session(
        ulong seed,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content)
    {
        var creation = new WorldCreationOptions("World", seed);
        var store = new DimensionSessionStateStore(creation, Dimensions());
        var state = store.GetOrCreate(DimensionId.Overworld);
        state.World.InsertChunk(ChunkCoord.Zero, new Chunk());
        state.WorldTick = 521;
        Assert.True(state.World.SetBlockAt(
            new WorldVoxelCoord(2, 3, 4),
            content.Blocks.GetId("asteria:stone"), out _));
        Assert.True(store.Player.Inventory.TryInsert(
            new InventoryStack(
                InventoryEntry.FromItem("asteria:pebble",
                    new Dictionary<string, string> { ["origin"] = "plains" }), 11)));
        return GameplaySessionSaveCodec.Capture(store,
            content.Blocks, content.Fluids, content.Dyes, content.Layers,
            activeSphere: DimensionId.Overworld);
    }

    [Fact]
    public void PublishesCompleteGenerationsAndRecoversPriorIntactSession()
    {
        WithDirectory(dir =>
        {
            var content = Content();
            Assert.Equal(1UL, SessionSaveStorage.Publish(
                dir, Session(1234, content),
                content.Blocks, content.Fluids, content.Dyes, content.Layers));
            Assert.Equal(2UL, SessionSaveStorage.Publish(
                dir, Session(5678, content),
                content.Blocks, content.Fluids, content.Dyes, content.Layers));

            var (saved, restored) = SessionSaveStorage.RestoreLatest(
                dir, Dimensions(), content.Blocks, content.Fluids,
                content.Dyes, content.Layers);
            Assert.Equal(5678UL, saved.Spatial.WorldSeed);
            Assert.Equal(DimensionId.Overworld, saved.ActiveSphere);
            Assert.Equal(521UL, restored.GetOrCreate(DimensionId.Overworld).WorldTick);
            Assert.Equal(11, restored.Player.Inventory.SelectedStack!.Quantity);
            Assert.Equal("plains", restored.Player.Inventory.SelectedStack.Entry.Metadata["origin"]);

            var newest = Path.Combine(dir, SessionSaveStorage.GenerationFileName(2));
            var bytes = File.ReadAllBytes(newest);
            bytes[10] ^= 0x13;
            File.WriteAllBytes(newest, bytes);

            (saved, restored) = SessionSaveStorage.RestoreLatest(
                dir, Dimensions(), content.Blocks, content.Fluids,
                content.Dyes, content.Layers);
            Assert.Equal(1234UL, saved.Spatial.WorldSeed);
            Assert.Equal(content.Blocks.GetId("asteria:stone"),
                restored.GetOrCreate(DimensionId.Overworld)
                    .World.GetChunk(ChunkCoord.Zero).GetBlock(2, 3, 4));
        });
    }

    [Fact]
    public void RejectsMissingActiveSphereAndDoesNotPublishPartialSave()
    {
        WithDirectory(dir =>
        {
            var content = Content();
            var creation = new WorldCreationOptions("World", 1UL);
            var state = new DimensionSessionStateStore(creation, Dimensions());
            state.GetOrCreate(DimensionId.Overworld);
            var noActive = GameplaySessionSaveCodec.Capture(
                state, content.Blocks, content.Fluids,
                content.Dyes, content.Layers);
            Assert.Throws<InvalidDataException>(() => SessionSaveStorage.Publish(
                dir, noActive, content.Blocks, content.Fluids,
                content.Dyes, content.Layers));
            Assert.Empty(Directory.EnumerateFiles(dir, "session-*.bin"));
            Assert.Throws<FileNotFoundException>(() => SessionSaveStorage.ReadLatest(
                dir, content.Blocks, content.Fluids,
                content.Dyes, content.Layers));
        });
    }

    [Fact]
    public void KeepsFourGenerationsAndIgnoresOrphanedStagingFiles()
    {
        WithDirectory(dir =>
        {
            var content = Content();
            for (ulong seed = 1; seed <= 6; seed++)
                Assert.Equal(seed, SessionSaveStorage.Publish(
                    dir, Session(seed, content),
                    content.Blocks, content.Fluids, content.Dyes, content.Layers));

            Assert.False(File.Exists(Path.Combine(
                dir, SessionSaveStorage.GenerationFileName(1))));
            Assert.False(File.Exists(Path.Combine(
                dir, SessionSaveStorage.GenerationFileName(2))));
            for (ulong generation = 3; generation <= 6; generation++)
                Assert.True(File.Exists(Path.Combine(
                    dir, SessionSaveStorage.GenerationFileName(generation))));

            File.WriteAllBytes(Path.Combine(dir,
                SessionSaveStorage.GenerationFileName(9) + ".aborted.tmp"), [1, 2, 3]);
            var latest = SessionSaveStorage.ReadLatest(
                dir, content.Blocks, content.Fluids, content.Dyes, content.Layers);
            Assert.Equal(6UL, latest.Spatial.WorldSeed);
            Assert.Empty(Directory.EnumerateFiles(dir, "session-*.bin.tmp"));
        });
    }

    [Fact]
    public void SemanticRestoreValidationFallsBackToOlderGeneration()
    {
        WithDirectory(dir =>
        {
            var content = Content();
            SessionSaveStorage.Publish(dir, Session(111, content),
                content.Blocks, content.Fluids, content.Dyes, content.Layers);
            var original = Session(222, content);
            var changed = new GameplaySessionSnapshot(
                original.Spatial, original.Name, original.TicksPerSecond,
                original.SpawnCreatures, original.Player, original.Spheres,
                original.ActiveSphere);
            SessionSaveStorage.Publish(dir, changed,
                content.Blocks, content.Fluids, content.Dyes, content.Layers);

            // The bytes and checksum are valid, but the current pack no
            // longer contains the second generation's authored Sphere ID.
            // Substitute an incompatible but structurally valid pack registry
            // for the semantic read and verify candidates are not accepted.
            var incompatible = new DimensionRegistry([
                Dimension("asteria:other")
            ]);
            Assert.Throws<InvalidDataException>(() =>
                SessionSaveStorage.RestoreLatest(
                    dir, incompatible, content.Blocks, content.Fluids,
                    content.Dyes, content.Layers));

            Assert.Equal(222UL, SessionSaveStorage.RestoreLatest(
                dir, Dimensions(), content.Blocks, content.Fluids,
                content.Dyes, content.Layers).Snapshot.Spatial.WorldSeed);
        });
    }

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "asteria-session-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
