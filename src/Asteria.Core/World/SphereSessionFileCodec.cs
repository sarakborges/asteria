using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// One Sphere's detached non-voxel session state. All runtime-indexed
/// block/fluid fields use portable namespaced authored IDs on disk.
/// </summary>
internal static class SphereSessionFileCodec
{
    private const int MaxBoxes = 65536;
    private const int MaxStructures = 65536;
    private const int MaxQueue = 262144;

    internal static void Write(BinaryWriter writer, SphereClockSnapshot sphere,
        BlockRegistry blocks, FluidRegistry fluids, DyeRegistry dyes,
        AttachedLayerRegistry layers)
    {
        PortableStackSaveCodec.WriteString(writer, sphere.Dimension.Value, 512);
        writer.Write(sphere.WorldTick);
        writer.Write(sphere.DayNight.HasValue);
        if (sphere.DayNight is { } clock)
        {
            writer.Write(clock.Day);
            writer.Write(clock.TickInDay);
        }
        writer.Write(sphere.Position.HasValue);
        if (sphere.Position is { } position) WriteVector(writer, position);
        writer.Write(sphere.NaturalSpawnNextAttemptTick);

        writer.Write(sphere.StorageBoxes.Count);
        foreach (var box in sphere.StorageBoxes)
        {
            WritePosition(writer, box.Position);
            foreach (var slot in box.Slots)
                PortableStackSaveCodec.Write(writer, slot, blocks, dyes, layers);
        }

        writer.Write(sphere.ManualStructures.Count);
        foreach (var record in sphere.ManualStructures)
        {
            PortableStackSaveCodec.WriteString(writer, record.Reference, 512);
            writer.Write(record.AnchorX);
            writer.Write(record.AnchorZ);
            writer.Write(record.MinimumX);
            writer.Write(record.MaximumX);
            writer.Write(record.MinimumY);
            writer.Write(record.MaximumY);
            writer.Write(record.MinimumZ);
            writer.Write(record.MaximumZ);
        }

        WritePhysics(writer, sphere.BlockPhysics, blocks, dyes, layers);
        WriteDrops(writer, sphere.DroppedBlocks, blocks, dyes, layers);
        WriteCreatures(writer, sphere.Creatures);
        WriteFluidQueue(writer, sphere.FluidWork, fluids);
        WritePhysicsQueue(writer, sphere.PhysicsWork);
    }

    internal static SphereClockSnapshot Read(BinaryReader reader,
        BlockRegistry blocks, FluidRegistry fluids, DyeRegistry dyes,
        AttachedLayerRegistry layers)
    {
        var id = new DimensionId(PortableStackSaveCodec.ReadString(reader, 512));
        var tick = reader.ReadUInt64();
        var dayNight = PortableStackSaveCodec.ReadBool(reader)
            ? new DayNightClockState(reader.ReadUInt64(), reader.ReadUInt64()) : (DayNightClockState?)null;
        var position = PortableStackSaveCodec.ReadBool(reader)
            ? ReadVector(reader) : (Vector3?)null;
        var nextSpawn = reader.ReadUInt64();

        var boxesCount = ReadCount(reader, MaxBoxes);
        var boxes = new SavedStorageBoxContents[boxesCount];
        for (var i = 0; i < boxes.Length; i++)
        {
            var where = ReadPosition(reader);
            var slots = new InventoryStack?[StorageBoxRuntime.SlotCount];
            for (var j = 0; j < slots.Length; j++)
                slots[j] = PortableStackSaveCodec.Read(reader, blocks, dyes, layers);
            boxes[i] = new SavedStorageBoxContents(where, slots);
        }

        var structureCount = ReadCount(reader, MaxStructures);
        var structures = new ManualStructurePlacementFootprint[structureCount];
        for (var i = 0; i < structures.Length; i++)
            structures[i] = new ManualStructurePlacementFootprint(
                PortableStackSaveCodec.ReadString(reader, 512),
                reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32());

        return new SphereClockSnapshot(
            id, tick, dayNight, position, nextSpawn, boxes, structures,
            ReadPhysics(reader, blocks, dyes, layers),
            ReadDrops(reader, blocks, dyes, layers),
            ReadCreatures(reader),
            ReadFluidQueue(reader, fluids),
            ReadPhysicsQueue(reader));
    }

    private static void WritePhysics(BinaryWriter writer,
        BlockPhysicsRuntimeSnapshot? snapshot,
        BlockRegistry blocks, DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        writer.Write(snapshot is not null);
        if (snapshot is null) return;
        writer.Write(snapshot.NextId);
        writer.Write(snapshot.ActiveBlocks.Count);
        foreach (var block in snapshot.ActiveBlocks)
        {
            writer.Write(block.Id.Value);
            var id = blocks.GetDefinition(block.Block.Cell.Block).Id;
            PortableStackSaveCodec.Write(writer, new InventoryStack(
                InventoryEntry.FromBlock(id, block.Block)), blocks, dyes, layers);
            writer.Write(block.ColumnX);
            writer.Write(block.ColumnZ);
            writer.Write(block.CenterY);
            writer.Write(block.VelocityY);
        }
    }

    private static BlockPhysicsRuntimeSnapshot? ReadPhysics(BinaryReader reader,
        BlockRegistry blocks, DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        if (!PortableStackSaveCodec.ReadBool(reader)) return null;
        var nextId = reader.ReadUInt64();
        var count = ReadCount(reader, 2048);
        var states = new FallingBlockState[count];
        for (var i = 0; i < count; i++)
        {
            var id = new FallingBlockId(reader.ReadUInt64());
            var stack = PortableStackSaveCodec.Read(reader, blocks, dyes, layers);
            if (stack is not { Kind: InventoryEntryKind.Block, Quantity: 1 } ||
                stack.Block is null)
                throw new InvalidDataException("Falling block is not a portable block.");
            states[i] = new FallingBlockState(id, stack.Block,
                reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadDouble(), reader.ReadDouble());
        }
        return new BlockPhysicsRuntimeSnapshot(nextId, states);
    }

    private static void WriteDrops(BinaryWriter writer,
        DroppedBlockRuntimeSnapshot? snapshot,
        BlockRegistry blocks, DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        writer.Write(snapshot is not null);
        if (snapshot is null) return;
        writer.Write(snapshot.NextId);
        writer.Write(snapshot.ActiveBlocks.Count);
        foreach (var entry in snapshot.ActiveBlocks)
        {
            var state = entry.State;
            writer.Write(state.Id.Value);
            PortableStackSaveCodec.Write(writer, state.Stack, blocks, dyes, layers);
            WriteVector(writer, state.Position);
            WriteVector(writer, state.Velocity);
            writer.Write(state.AgeSeconds);
            writer.Write(state.IsSettled);
            writer.Write(entry.SettledSupport.HasValue);
            if (entry.SettledSupport is { } support) WritePosition(writer, support);
        }
    }

    private static DroppedBlockRuntimeSnapshot? ReadDrops(BinaryReader reader,
        BlockRegistry blocks, DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        if (!PortableStackSaveCodec.ReadBool(reader)) return null;
        var nextId = reader.ReadUInt64();
        var count = ReadCount(reader, 2048);
        var entries = new DroppedBlockRuntimeEntry[count];
        for (var i = 0; i < count; i++)
        {
            var id = new DroppedBlockId(reader.ReadUInt64());
            var stack = PortableStackSaveCodec.Read(reader, blocks, dyes, layers);
            if (stack is null)
                throw new InvalidDataException("A dropped item must have a portable stack.");
            var state = new DroppedBlockState(
                id, stack, ReadVector(reader), ReadVector(reader),
                reader.ReadDouble(), PortableStackSaveCodec.ReadBool(reader));
            var support = PortableStackSaveCodec.ReadBool(reader)
                ? ReadPosition(reader) : (WorldVoxelCoord?)null;
            entries[i] = new DroppedBlockRuntimeEntry(state, support);
        }
        return new DroppedBlockRuntimeSnapshot(nextId, entries);
    }

    private static void WriteCreatures(BinaryWriter writer, CreatureRuntimeSnapshot? snapshot)
    {
        writer.Write(snapshot is not null);
        if (snapshot is null) return;
        writer.Write(snapshot.NextId);
        writer.Write(snapshot.Creatures.Count);
        foreach (var c in snapshot.Creatures)
        {
            writer.Write(c.Id.Value);
            PortableStackSaveCodec.WriteString(writer, c.DefinitionId, 512);
            WriteVector(writer, c.Position);
            writer.Write(c.Health);
            writer.Write(c.AgeSeconds);
            writer.Write((byte)c.Motion.Phase);
            writer.Write(c.Motion.SecondsRemaining);
            writer.Write(c.Motion.VerticalSpeed);
            writer.Write(c.Motion.Direction.X);
            writer.Write(c.Motion.Direction.Y);
            writer.Write(c.Motion.FacingRadians);
            writer.Write(c.Motion.HopCount);
            writer.Write(c.Motion.KnockbackVelocity.X);
            writer.Write(c.Motion.KnockbackVelocity.Y);
            writer.Write(c.Motion.KnockbackSeconds);
            writer.Write(c.AttacksReceived);
            writer.Write(c.HurtSecondsRemaining);
            writer.Write(c.DeathSecondsRemaining);
            writer.Write(c.MetaTags.NoAi);
            if (c.MetaTags.NoAi) WriteNullable(writer, c.MetaTags.NoAiValue);
            writer.Write(c.MetaTags.Persistent);
            if (c.MetaTags.Persistent) WriteNullable(writer, c.MetaTags.PersistentValue);
        }
    }

    private static CreatureRuntimeSnapshot? ReadCreatures(BinaryReader reader)
    {
        if (!PortableStackSaveCodec.ReadBool(reader)) return null;
        var nextId = reader.ReadUInt64();
        var count = ReadCount(reader, CreatureRuntime.MaximumActive);
        var entries = new CreatureInstanceState[count];
        for (var i = 0; i < count; i++)
        {
            var id = new CreatureInstanceId(reader.ReadUInt64());
            var definition = PortableStackSaveCodec.ReadString(reader, 512);
            var position = ReadVector(reader);
            var health = reader.ReadSingle();
            var age = reader.ReadDouble();
            var phase = (CreatureHopPhase)reader.ReadByte();
            var seconds = reader.ReadSingle();
            var vertical = reader.ReadSingle();
            var direction = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            var facing = reader.ReadSingle();
            var hops = reader.ReadUInt32();
            var knockback = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            var knockbackSeconds = reader.ReadSingle();
            var attacks = reader.ReadUInt32();
            var hurt = reader.ReadSingle();
            var dying = reader.ReadSingle();
            var tags = default(CreatureMetaTags);
            if (PortableStackSaveCodec.ReadBool(reader))
            {
                var value = ReadNullable(reader);
                if (!tags.TryChange(CreatureMetaTagAction.Add, CreatureMetaTags.NoAiTag,
                        value, out var changed, out _))
                    throw new InvalidDataException("Invalid saved NO_AI tag.");
                tags = changed;
            }
            if (PortableStackSaveCodec.ReadBool(reader))
            {
                var value = ReadNullable(reader);
                if (!tags.TryChange(CreatureMetaTagAction.Add, CreatureMetaTags.PersistentTag,
                        value, out var changed, out _))
                    throw new InvalidDataException("Invalid saved PERSISTENT tag.");
                tags = changed;
            }

            entries[i] = new CreatureInstanceState(id, definition, position,
                health, age, new CreatureHopMotion(phase, seconds, vertical, direction, facing, hops)
                {
                    KnockbackVelocity = knockback,
                    KnockbackSeconds = knockbackSeconds
                })
            {
                AttacksReceived = attacks,
                HurtSecondsRemaining = hurt,
                DeathSecondsRemaining = dying,
                MetaTags = tags
            };
        }
        return new CreatureRuntimeSnapshot(nextId, entries);
    }

    private static void WriteFluidQueue(BinaryWriter writer,
        FluidUpdateQueueSnapshot? snapshot, FluidRegistry fluids)
    {
        writer.Write(snapshot is not null);
        if (snapshot is null) return;
        writer.Write(snapshot.Topology.Count);
        foreach (var entry in snapshot.Topology) WritePosition(writer, entry);
        writer.Write(snapshot.Scheduled.Count);
        foreach (var entry in snapshot.Scheduled)
        {
            PortableStackSaveCodec.WriteString(
                writer, fluids.GetDefinition(entry.Tick.Fluid).Id, 512);
            WritePosition(writer, entry.Tick.Position);
            writer.Write(entry.DueTick);
        }
        writer.Write(snapshot.Dormant.Count);
        foreach (var entry in snapshot.Dormant)
        {
            PortableStackSaveCodec.WriteString(
                writer, fluids.GetDefinition(entry.Fluid).Id, 512);
            WritePosition(writer, entry.Position);
        }
    }

    private static FluidUpdateQueueSnapshot? ReadFluidQueue(
        BinaryReader reader, FluidRegistry fluids)
    {
        if (!PortableStackSaveCodec.ReadBool(reader)) return null;
        var count = ReadCount(reader, MaxQueue);
        var topology = new WorldVoxelCoord[count];
        for (var i = 0; i < count; i++) topology[i] = ReadPosition(reader);

        count = ReadCount(reader, MaxQueue);
        var scheduled = new ScheduledFluidTick[count];
        for (var i = 0; i < count; i++)
        {
            var fluid = ReadFluidId(reader, fluids);
            var pos = ReadPosition(reader);
            scheduled[i] = new ScheduledFluidTick(
                new FluidTickKey(fluid, pos), reader.ReadUInt64());
        }

        count = ReadCount(reader, MaxQueue);
        var dormant = new FluidTickKey[count];
        for (var i = 0; i < count; i++)
            dormant[i] = new FluidTickKey(
                ReadFluidId(reader, fluids), ReadPosition(reader));
        return new FluidUpdateQueueSnapshot(topology, scheduled, dormant);
    }

    private static void WritePhysicsQueue(BinaryWriter writer,
        BlockPhysicsUpdateQueueSnapshot? snapshot)
    {
        writer.Write(snapshot is not null);
        if (snapshot is null) return;
        writer.Write(snapshot.Positions.Count);
        foreach (var position in snapshot.Positions) WritePosition(writer, position);
    }

    private static BlockPhysicsUpdateQueueSnapshot? ReadPhysicsQueue(BinaryReader reader)
    {
        if (!PortableStackSaveCodec.ReadBool(reader)) return null;
        var count = ReadCount(reader, MaxQueue);
        var entries = new WorldVoxelCoord[count];
        for (var i = 0; i < count; i++) entries[i] = ReadPosition(reader);
        return new BlockPhysicsUpdateQueueSnapshot(entries);
    }

    private static FluidRuntimeId ReadFluidId(BinaryReader reader, FluidRegistry fluids)
    {
        var id = PortableStackSaveCodec.ReadString(reader, 512);
        if (!fluids.TryGetId(id, out var runtime) || runtime.IsNone)
            throw new InvalidDataException("Unknown saved fluid content ID.");
        return runtime;
    }

    private static void WriteNullable(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
            PortableStackSaveCodec.WriteString(
                writer, value, CreatureMetaTags.MaximumValueLength, allowEmpty: true);
    }

    private static string? ReadNullable(BinaryReader reader) =>
        PortableStackSaveCodec.ReadBool(reader)
            ? PortableStackSaveCodec.ReadString(
                reader, CreatureMetaTags.MaximumValueLength, allowEmpty: true)
            : null;

    internal static void WritePosition(BinaryWriter writer, WorldVoxelCoord position)
    {
        writer.Write(position.X);
        writer.Write(position.Y);
        writer.Write(position.Z);
    }

    internal static WorldVoxelCoord ReadPosition(BinaryReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

    private static void WriteVector(BinaryWriter writer, Vector3 position)
    {
        writer.Write(position.X);
        writer.Write(position.Y);
        writer.Write(position.Z);
    }

    private static Vector3 ReadVector(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    internal static int ReadCount(BinaryReader reader, int max)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > max)
            throw new InvalidDataException("Saved session collection count exceeds bounds.");
        return count;
    }
}
