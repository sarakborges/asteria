using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GameplaySessionFileCodecTests
{
    private static DimensionDefinition Dimension(string id) => new(
        new DimensionId(id), [id + "/plains"], 64, 18f,
        new DimensionSpawnDefinition(0, 0),
        new DimensionEnvironmentDefinition(
            new DimensionColor(0, 0, 0), new DimensionColor(255, 255, 255),
            1f, new DimensionColor(0, 0, 0), 0f));

    private static DimensionRegistry Dimensions() => new([
        Dimension("asteria:overworld"),
        Dimension("asteria:umbral")
    ]);

    private static (BlockRegistry Blocks, FluidRegistry Fluids,
        DyeRegistry Dyes, AttachedLayerRegistry Layers) Content(bool reorder) => (
        new BlockRegistry(reorder ? [
            new BlockDefinition("asteria:storage_box"),
            new BlockDefinition("asteria:granite"),
            new BlockDefinition("asteria:stone")
        ] : [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:granite"),
            new BlockDefinition("asteria:storage_box")
        ]),
        new FluidRegistry(reorder ? [
            new FluidDefinition("asteria:lava", new FluidColor(255, 40, 0), 0.9f),
            new FluidDefinition("asteria:water", new FluidColor(0, 80, 255), 0.5f)
        ] : [
            new FluidDefinition("asteria:water", new FluidColor(0, 80, 255), 0.5f),
            new FluidDefinition("asteria:lava", new FluidColor(255, 40, 0), 0.9f)
        ]),
        new DyeRegistry([]), new AttachedLayerRegistry([])
    );

    private static byte[] Encode(
        GameplaySessionSnapshot snapshot,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content)
    {
        using var output = new MemoryStream();
        GameplaySessionFileCodec.Write(output, snapshot,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);
        return output.ToArray();
    }

    private static GameplaySessionSnapshot Decode(
        byte[] data,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content)
    {
        using var input = new MemoryStream(data, writable: false);
        return GameplaySessionFileCodec.Read(input, input.Length,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);
    }

    [Fact]
    public void CompleteSessionRoundTripsAcrossRegistryReordering()
    {
        var original = Content(false);
        var reordered = Content(true);
        var creation = new WorldCreationOptions("Example", 9842UL);
        var states = new DimensionSessionStateStore(creation, Dimensions());
        var surface = states.GetOrCreate(DimensionId.Overworld);
        var umbral = states.GetOrCreate(new DimensionId("asteria:umbral"));
        var granite = original.Blocks.GetId("asteria:granite");
        var water = original.Fluids.GetId("asteria:water");
        var block = new BlockStateSnapshot(
            new VoxelCell(granite, orientation: BlockOrientation.X,
                facing: HorizontalFacing.West, state: 9),
            MicroblockMask.Empty);
        var stack = new InventoryStack(InventoryEntry.FromBlock("asteria:granite", block), 3);

        Assert.True(states.Player.Inventory.TryInsert(stack));
        Assert.True(states.Player.Inventory.TryCreativePick(
            InventoryEntry.FromItem("asteria:stick",
                new Dictionary<string, string> { ["source"] = "tree" }), 7));
        surface.World.InsertChunk(ChunkCoord.Zero, new Chunk());
        Assert.True(surface.World.SetBlockAt(
            new WorldVoxelCoord(2, 4, 3),
            original.Blocks.GetId("asteria:storage_box"), out _));
        Assert.True(surface.StorageBoxes.TryOpen(
            new WorldVoxelCoord(2, 4, 3), surface.World, original.Blocks));
        Assert.True(surface.StorageBoxes.TryInsertActive(stack,
            surface.World, original.Blocks));
        surface.StorageBoxes.Close();

        umbral.World.InsertChunk(ChunkCoord.Zero, new Chunk());
        Assert.True(umbral.World.SetFluidAt(
            new WorldVoxelCoord(1, 1, 1),
            FluidCell.Source(water), out _));
        umbral.World.ArchiveChunk(ChunkCoord.Zero);
        var work = new FluidUpdateQueue();
        work.ScheduleAt(new FluidTickKey(
            water, new WorldVoxelCoord(1, 1, 1)), 123);
        work.DeferUnloaded(new FluidTickKey(
            water, new WorldVoxelCoord(1, 2, 1)));
        umbral.PendingFluidWork = work.CaptureState();

        surface.WorldTick = 43;
        surface.DayNight = new DayNightClockState(3, 22);
        surface.PlayerPosition = new Vector3(4.5f, 20, 2.5f);
        surface.ManualStructures.RecordCommitted(
            new ManualStructurePlacementFootprint(
                "asteria:oak_tree", 15, 10, 14, 16, 1, 5, 9, 11));
        surface.BlockPhysics = new BlockPhysicsRuntimeSnapshot(1, [
            new FallingBlockState(new FallingBlockId(1), block,
                5, 6, 12.5, -2.25)
        ]);
        surface.DroppedBlocks = new DroppedBlockRuntimeSnapshot(1, [
            new DroppedBlockRuntimeEntry(new DroppedBlockState(
                new DroppedBlockId(1),
                new InventoryStack(InventoryEntry.FromBlock("asteria:granite", block)),
                new Vector3(3, 6, 4), Vector3.UnitY, 3.25, false), null)
        ]);
        surface.Creatures = new CreatureRuntimeSnapshot(1, [
            new CreatureInstanceState(new CreatureInstanceId(1),
                "asteria:slime_aqua", new Vector3(5, 6, 7),
                5f, 3.5, CreatureHopMotion.Initial)
        ]);

        var snapshot = GameplaySessionSaveCodec.Capture(
            states, original.Blocks, original.Fluids, original.Dyes, original.Layers);
        var encoded = Encode(snapshot, original);
        var decoded = Decode(encoded, reordered);
        var restored = GameplaySessionSaveCodec.Restore(
            creation, Dimensions(), decoded,
            reordered.Blocks, reordered.Fluids, reordered.Dyes, reordered.Layers);

        Assert.Equal(encoded, Encode(decoded, reordered));
        Assert.Equal(9842UL, decoded.Spatial.WorldSeed);
        Assert.Equal(7, restored.Player.Inventory.Cursor!.Quantity);
        Assert.Equal(reordered.Blocks.GetId("asteria:granite"),
            restored.Player.Inventory.SelectedStack!.Block!.Cell.Block);
        var resumedSurface = restored.GetOrCreate(DimensionId.Overworld);
        Assert.Equal(43UL, resumedSurface.WorldTick);
        Assert.Equal(new DayNightClockState(3, 22), resumedSurface.DayNight);
        Assert.Equal(reordered.Blocks.GetId("asteria:granite"),
            resumedSurface.BlockPhysics!.ActiveBlocks[0].Cell.Block);
        Assert.Equal(reordered.Blocks.GetId("asteria:granite"),
            resumedSurface.DroppedBlocks!.ActiveBlocks[0].State.Block!.Cell.Block);
        Assert.Equal(5f, resumedSurface.Creatures!.Creatures[0].Health);
        Assert.Equal("asteria:granite",
            resumedSurface.StorageBoxes.CaptureOccupied()[0].Slots[0]!.Id);
        Assert.True(resumedSurface.ManualStructures.Overlaps(
            new ManualStructurePlacementFootprint(
                "asteria:other", 15, 10, 14, 16, 1, 5, 9, 11)));

        var resumedUmbral = restored.GetOrCreate(new DimensionId("asteria:umbral"));
        Assert.True(resumedUmbral.World.HasArchivedChunk(ChunkCoord.Zero));
        Assert.Single(resumedUmbral.PendingFluidWork!.Scheduled);
        Assert.Equal(reordered.Fluids.GetId("asteria:water"),
            resumedUmbral.PendingFluidWork.Scheduled[0].Tick.Fluid);
        Assert.Equal(reordered.Fluids.GetId("asteria:water"),
            resumedUmbral.PendingFluidWork.Dormant[0].Fluid);
        Assert.Equal(ChunkRestoreResult.Restored,
            resumedUmbral.World.RestoreChunk(ChunkCoord.Zero));
        Assert.Equal(reordered.Fluids.GetId("asteria:water"),
            resumedUmbral.World.GetChunk(ChunkCoord.Zero).GetFluid(1, 1, 1).Fluid);
    }

    [Fact]
    public void SessionCodecRejectsCorruptionTrailingDataAndMissingBlockDefinition()
    {
        var content = Content(false);
        var creation = new WorldCreationOptions("World", 1234UL);
        var source = new DimensionSessionStateStore(creation, Dimensions());
        source.GetOrCreate(DimensionId.Overworld);
        var saved = GameplaySessionSaveCodec.Capture(
            source, content.Blocks, content.Fluids, content.Dyes, content.Layers);
        var bytes = Encode(saved, content);
        var invalid = (byte[])bytes.Clone();
        invalid[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => Decode(invalid, content));
        Assert.Throws<InvalidDataException>(() =>
            Decode(bytes[..^1], content));
        Assert.Throws<InvalidDataException>(() =>
            Decode(bytes.Concat(new byte[] { 0 }).ToArray(), content));

        var larger = (byte[])bytes.Clone();
        // Version tag is always the two bytes immediately after magic.
        larger[4] = 22;
        Assert.Throws<InvalidDataException>(() => Decode(larger, content));
    }
}
