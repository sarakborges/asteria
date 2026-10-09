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
        var helm = InventoryEntry.FromItem("asteria:test_helm", maxStackSize: 1);
        Assert.True(states.Player.Inventory.TryCreativePick(helm));
        Assert.True(states.Player.Inventory.ClickEquipment(
            EquipmentSlot.Helmet, (entry, slot) =>
                entry.Equals(helm) && slot == EquipmentSlot.Helmet));
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
        Assert.Equal("asteria:test_helm",
            restored.Player.Inventory.EquipmentAt(EquipmentSlot.Helmet)!.Id);
        Assert.Null(restored.Player.Inventory.EquipmentAt(EquipmentSlot.Chest));
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
    public void PlayerHealthRoundTripsAndDeadStatePersists()
    {
        var content = Content(false);
        var creation = new WorldCreationOptions("Health World", 4321UL);
        var states = new DimensionSessionStateStore(creation, Dimensions());
        states.GetOrCreate(DimensionId.Overworld);
        Assert.Equal(PlayerDamageResult.Killed,
            states.Player.Health.Damage(20f, PlayerGameMode.Survival));
        var saved = GameplaySessionSaveCodec.Capture(states,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);
        var data = Encode(saved, content);
        Assert.Equal((byte)3, data[4]);
        var restored = GameplaySessionSaveCodec.Restore(
            creation, Dimensions(), Decode(data, content),
            content.Blocks, content.Fluids, content.Dyes, content.Layers);
        Assert.True(restored.Player.Health.IsDead);
        Assert.False(restored.Player.CanInteract);
        Assert.Equal(0f, restored.Player.Health.Current);
    }

    [Fact]
    public void LegacyV2LoadsWithMaximumHealth()
    {
        var content = Content(false);
        var creation = new WorldCreationOptions("Legacy Health", 1234UL);
        var states = new DimensionSessionStateStore(creation, Dimensions());
        states.GetOrCreate(DimensionId.Overworld);
        var encoded = Encode(GameplaySessionSaveCodec.Capture(states,
            content.Blocks, content.Fluids, content.Dyes, content.Layers), content);
        using var stream = new MemoryStream(encoded, writable: false);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        reader.ReadUInt32();
        reader.ReadUInt16();
        PortableStackSaveCodec.ReadString(reader, 512);
        reader.ReadUInt32();
        reader.ReadBoolean();
        if (reader.ReadBoolean())
            PortableStackSaveCodec.ReadString(reader, 512);
        reader.ReadByte();
        reader.ReadBoolean();
        reader.ReadByte();
        for (var i = 0; i < PlayerInventory.TotalSlots + 1 +
            PlayerInventory.EquipmentSlots; i++)
            PortableStackSaveCodec.Read(reader,
                content.Blocks, content.Dyes, content.Layers);
        var healthOffset = checked((int)stream.Position);
        var legacy = encoded.AsSpan(0, healthOffset).ToArray()
            .Concat(encoded.AsSpan(healthOffset + sizeof(float)).ToArray())
            .ToArray();
        legacy[4] = 2;
        legacy[5] = 0;
        var restored = GameplaySessionSaveCodec.Restore(
            creation, Dimensions(), Decode(legacy, content),
            content.Blocks, content.Fluids, content.Dyes, content.Layers);
        Assert.Equal(PlayerHealth.DefaultMaximum, restored.Player.Health.Current);
    }

    [Fact]
    public void LegacyV1PlayerInventoryLoadsWithFourEmptyEquipmentSlots()
    {
        var content = Content(false);
        var creation = new WorldCreationOptions("Legacy World", 4321UL);
        var states = new DimensionSessionStateStore(creation, Dimensions());
        states.GetOrCreate(DimensionId.Overworld);
        Assert.True(states.Player.Inventory.TryInsert(new InventoryStack(
            InventoryEntry.FromItem("asteria:stick"), 2)));
        var saved = GameplaySessionSaveCodec.Capture(
            states, content.Blocks, content.Fluids,
            content.Dyes, content.Layers);
        var version3 = Encode(saved, content);

        // A v1 payload did not contain equipment or health fields.
        // Synthesize the exact v1 shape from a v3 payload with empty gear,
        // without modifying any spatial or sphere snapshot bytes.
        using var source = new MemoryStream(version3, writable: false);
        using var reader = new BinaryReader(source, System.Text.Encoding.UTF8,
            leaveOpen: true);
        reader.ReadUInt32();
        reader.ReadUInt16();
        PortableStackSaveCodec.ReadString(reader, 512);
        reader.ReadUInt32();
        reader.ReadBoolean();
        if (reader.ReadBoolean())
            PortableStackSaveCodec.ReadString(reader, 512);
        reader.ReadByte();
        reader.ReadBoolean();
        reader.ReadByte();
        for (var i = 0; i < PlayerInventory.TotalSlots + 1; i++)
            PortableStackSaveCodec.Read(
                reader, content.Blocks, content.Dyes, content.Layers);
        var equipmentStart = checked((int)source.Position);
        Assert.Equal(new byte[PlayerInventory.EquipmentSlots],
            version3.AsSpan(equipmentStart, PlayerInventory.EquipmentSlots).ToArray());
        var version1 = version3.AsSpan(0, equipmentStart).ToArray()
            .Concat(version3.AsSpan(
                equipmentStart + PlayerInventory.EquipmentSlots + sizeof(float)).ToArray())
            .ToArray();
        version1[4] = 1;
        version1[5] = 0;

        var restored = GameplaySessionSaveCodec.Restore(
            creation, Dimensions(), Decode(version1, content),
            content.Blocks, content.Fluids,
            content.Dyes, content.Layers);
        Assert.Equal(2, restored.Player.Inventory.SelectedStack!.Quantity);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            Assert.Null(restored.Player.Inventory.EquipmentAt(slot));
    }

    [Fact]
    public void CreatureTagWithExplicitEmptyValueSurvivesSessionSerialization()
    {
        var content = Content(false);
        var creation = new WorldCreationOptions("World", 98765UL);
        var source = new DimensionSessionStateStore(creation, Dimensions());
        var sphere = source.GetOrCreate(DimensionId.Overworld);

        var tags = default(CreatureMetaTags);
        Assert.True(tags.TryChange(
            CreatureMetaTagAction.Add, CreatureMetaTags.PersistentTag,
            "", out var persistent, out _));
        var creature = new CreatureInstanceState(
            new CreatureInstanceId(1), "asteria:slime_aqua",
            new Vector3(0, 10, 0), 5, 0,
            CreatureHopMotion.Initial)
        {
            MetaTags = persistent
        };
        sphere.Creatures = new CreatureRuntimeSnapshot(1, [creature]);
        var snapshot = GameplaySessionSaveCodec.Capture(
            source, content.Blocks, content.Fluids, content.Dyes, content.Layers);
        var result = Decode(Encode(snapshot, content), content);
        var saved = Assert.Single(result.Spheres[0].Creatures!.Creatures);
        Assert.True(saved.MetaTags.Persistent);
        Assert.Equal("", saved.MetaTags.PersistentValue);
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
