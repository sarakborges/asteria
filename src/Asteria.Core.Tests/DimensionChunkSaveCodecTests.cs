using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DimensionChunkSaveCodecTests
{
    private static DimensionDefinition Dimension(string id) => new(
        new DimensionId(id),
        [id + "/plains"],
        90,
        18f,
        new DimensionSpawnDefinition(0, 0),
        new DimensionEnvironmentDefinition(
            new DimensionColor(0, 0, 0),
            new DimensionColor(255, 255, 255),
            1f,
            new DimensionColor(0, 0, 0),
            0f));

    private static DimensionRegistry Dimensions() => new([
        Dimension("asteria:overworld"),
        Dimension("asteria:umbral"),
        Dimension("asteria:unused"),
    ]);

    private static (BlockRegistry Blocks, FluidRegistry Fluids,
        DyeRegistry Dyes, AttachedLayerRegistry Layers) Content() => (
        new BlockRegistry([new BlockDefinition("asteria:stone")]),
        new FluidRegistry([]),
        new DyeRegistry([]),
        new AttachedLayerRegistry([]));

    private static DimensionChunkSaveSnapshot Capture(
        DimensionSessionStateStore store,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        DimensionChunkSaveCodec.Capture(
            store, content.Blocks, content.Fluids, content.Dyes, content.Layers);

    private static DimensionSessionStateStore Restore(
        WorldCreationOptions creation,
        DimensionRegistry definitions,
        DimensionChunkSaveSnapshot snapshot,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        DimensionChunkSaveCodec.Restore(
            creation, definitions, snapshot,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    [Fact]
    public void MultipleSpheresRoundTripWithoutMixingOrRegeneratingChunks()
    {
        var definitions = Dimensions();
        var content = Content();
        var creation = new WorldCreationOptions("World", 54321UL,
            generation: new WorldGenerationOptions(biomeSizeTenths: 13));
        var source = new DimensionSessionStateStore(creation, definitions);
        var overworld = source.GetOrCreate(DimensionId.Overworld);
        var umbral = source.GetOrCreate(new DimensionId("asteria:umbral"));
        var stone = content.Blocks.GetId("asteria:stone");

        var overworldCoord = new ChunkCoord(0, 0, 0);
        overworld.World.InsertChunk(overworldCoord, new Chunk());
        Assert.True(overworld.World.SetBlockAt(
            new WorldVoxelCoord(1, 3, 2), stone, out _));

        var archivedCoord = new ChunkCoord(-3, 1, 2);
        umbral.World.InsertChunk(archivedCoord, new Chunk());
        Assert.Equal(ChunkArchiveResult.ArchivedPristine,
            umbral.World.ArchiveChunk(archivedCoord));
        var originalOverworld = overworld.World;
        var originalUmbral = umbral.World;
        var snapshot = Capture(source, content);
        Assert.Equal(new[] {"asteria:overworld", "asteria:umbral"},
            snapshot.Spheres.Select(x => x.Dimension.Value));

        var restored = Restore(creation, definitions, snapshot, content);
        Assert.Equal(2, restored.Count);
        var newOverworld = restored.GetOrCreate(DimensionId.Overworld);
        var newUmbral = restored.GetOrCreate(new DimensionId("asteria:umbral"));
        Assert.NotSame(originalOverworld, newOverworld.World);
        Assert.NotSame(originalUmbral, newUmbral.World);
        Assert.Equal(overworld.DimensionSeed, newOverworld.DimensionSeed);
        Assert.Equal(umbral.DimensionSeed, newUmbral.DimensionSeed);
        Assert.Equal(stone,
            newOverworld.World.GetChunk(overworldCoord).GetBlock(1, 3, 2));
        Assert.False(newUmbral.World.ContainsChunk(overworldCoord));
        Assert.True(newUmbral.World.HasArchivedChunk(archivedCoord));
        Assert.False(newOverworld.World.HasArchivedChunk(archivedCoord));

        // Other authored Spheres remain lazy and generate only on first use.
        Assert.Equal(2, restored.Count);
        Assert.False(restored.TryGet(
            new DimensionId("asteria:unused"), out _));
        Assert.Equal(2, source.Count);
    }

    [Fact]
    public void RestoreRejectsDifferentSeedAndGenerationSettings()
    {
        var defs = Dimensions();
        var content = Content();
        var creation = new WorldCreationOptions("World", 123UL);
        var snapshot = Capture(new DimensionSessionStateStore(creation, defs), content);

        Assert.Throws<InvalidDataException>(() => Restore(
            new WorldCreationOptions("World", 124UL),
            defs, snapshot, content));
        Assert.Throws<InvalidDataException>(() => Restore(
            new WorldCreationOptions("World", 123UL,
                generation: new WorldGenerationOptions(spawnCaves: false)),
            defs, snapshot, content));
    }

    [Fact]
    public void RestoreRejectsUnknownSphereAndInvalidChunkPayload()
    {
        var defs = Dimensions();
        var content = Content();
        var creation = new WorldCreationOptions("World", 55UL);
        var source = new DimensionSessionStateStore(creation, defs);
        var state = source.GetOrCreate(DimensionId.Overworld);
        state.World.InsertChunk(ChunkCoord.Zero, new Chunk());
        var valid = Capture(source, content);

        var badSphere = new DimensionChunkSaveSnapshot(
            creation.Seed, creation.Generation,
            [new SavedSphereChunks(new DimensionId("asteria:unknown"),
                valid.Spheres[0].Chunks)]);
        Assert.Throws<KeyNotFoundException>(() => Restore(
            creation, defs, badSphere, content));

        var bytes = valid.Spheres[0].Chunks.Chunks[0].CopyEncodedChunk();
        bytes[^1] ^= 1;
        var invalidWorld = new VoxelWorldSaveSnapshot([
            new SavedWorldChunk(ChunkCoord.Zero, true, false, bytes),
        ]);
        var broken = new DimensionChunkSaveSnapshot(
            creation.Seed, creation.Generation,
            [new SavedSphereChunks(DimensionId.Overworld, invalidWorld)]);
        Assert.Throws<InvalidDataException>(() => Restore(
            creation, defs, broken, content));

        Assert.True(state.World.ContainsChunk(ChunkCoord.Zero));
        Assert.Equal(1, source.Count);
        Assert.Throws<InvalidDataException>(() =>
            new DimensionChunkSaveSnapshot(55UL, creation.Generation,
            [
                valid.Spheres[0],
                valid.Spheres[0],
            ]));
    }
}
