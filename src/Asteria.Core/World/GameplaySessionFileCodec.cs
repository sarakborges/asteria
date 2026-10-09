using System.Text;

namespace Asteria.Core.World;

/// <summary>
/// Single versioned stream containing complete in-memory gameplay session
/// snapshots plus all materialized chunks. This codec performs no disk writes;
/// publication/rollback belongs to the storage layer.
/// </summary>
public static class GameplaySessionFileCodec
{
    private const uint Magic = 0x53534741; // AGSS
    private const ushort Version = 3;
    public const long MaximumBytes = 640L * 1024 * 1024;

    public static void Write(
        Stream output, GameplaySessionSnapshot session,
        BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(session);
        if (!output.CanWrite || !output.CanSeek)
            throw new ArgumentException("Session output must be writable and seekable.", nameof(output));

        var start = output.Position;
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        PortableStackSaveCodec.WriteString(writer, session.Name, 512);
        writer.Write(session.TicksPerSecond);
        writer.Write(session.SpawnCreatures);
        writer.Write(session.ActiveSphere.HasValue);
        if (session.ActiveSphere is { } activeSphere)
            PortableStackSaveCodec.WriteString(writer, activeSphere.Value, 512);
        writer.Write((byte)session.Player.GameMode);
        writer.Write(session.Player.Flying);
        var inventory = session.Player.Inventory;
        writer.Write((byte)inventory.SelectedSlot);
        foreach (var slot in inventory.Backpack)
            PortableStackSaveCodec.Write(writer, slot, blocks, dyes, layers);
        foreach (var slot in inventory.Hotbar)
            PortableStackSaveCodec.Write(writer, slot, blocks, dyes, layers);
        PortableStackSaveCodec.Write(writer, inventory.Cursor, blocks, dyes, layers);
        foreach (var equipped in inventory.Equipment ??
            new InventoryStack?[PlayerInventory.EquipmentSlots])
            PortableStackSaveCodec.Write(writer, equipped, blocks, dyes, layers);
        writer.Write(session.Player.Health);

        // Embed the spatial snapshot in the very same generation. No
        // manifest may point to only one of these two halves.
        var spatialLengthOffset = output.Position;
        writer.Write(0L);
        var spatialOffset = output.Position;
        DimensionChunkFileCodec.Write(output, session.Spatial);
        var spatialLength = output.Position - spatialOffset;
        var afterSpatial = output.Position;
        output.Position = spatialLengthOffset;
        writer.Write(spatialLength);
        output.Position = afterSpatial;

        writer.Write(session.Spheres.Count);
        foreach (var sphere in session.Spheres)
        {
            SphereSessionFileCodec.Write(writer, sphere,
                blocks, fluids, dyes, layers);
            if (output.Position - start > MaximumBytes)
                throw new InvalidDataException("Saved gameplay session exceeds size limit.");
        }

        if (output.Position - start > MaximumBytes)
            throw new InvalidDataException("Saved gameplay session exceeds size limit.");
    }

    public static GameplaySessionSnapshot Read(
        Stream source, long payloadLength,
        BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("Saved session input must be readable and seekable.", nameof(source));
        if (payloadLength < 32 || payloadLength > MaximumBytes ||
            source.Length - source.Position < payloadLength)
            throw new InvalidDataException("Invalid gameplay session payload length.");

        var end = checked(source.Position + payloadLength);
        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Unknown gameplay session magic.");
            var version = reader.ReadUInt16();
            if (version is not (1 or 2 or Version))
                throw new InvalidDataException("Unknown gameplay session format.");
            var name = PortableStackSaveCodec.ReadString(reader, 512);
            var tickRate = reader.ReadUInt32();
            var spawnCreatures = PortableStackSaveCodec.ReadBool(reader);
            var activeSphere = PortableStackSaveCodec.ReadBool(reader)
                ? new DimensionId(PortableStackSaveCodec.ReadString(reader, 512))
                : (DimensionId?)null;
            var mode = (PlayerGameMode)reader.ReadByte();
            var flying = PortableStackSaveCodec.ReadBool(reader);
            var selection = reader.ReadByte();
            var backpack = new InventoryStack?[PlayerInventory.BackpackSlots];
            var hotbar = new InventoryStack?[PlayerInventory.HotbarSlots];
            for (var i = 0; i < backpack.Length; i++)
                backpack[i] = PortableStackSaveCodec.Read(reader, blocks, dyes, layers);
            for (var i = 0; i < hotbar.Length; i++)
                hotbar[i] = PortableStackSaveCodec.Read(reader, blocks, dyes, layers);
            var cursor = PortableStackSaveCodec.Read(reader, blocks, dyes, layers);
            var equipment = new InventoryStack?[PlayerInventory.EquipmentSlots];
            if (version >= 2)
                for (var i = 0; i < equipment.Length; i++)
                    equipment[i] = PortableStackSaveCodec.Read(reader, blocks, dyes, layers);
            var health = version >= 3
                ? reader.ReadSingle() : PlayerHealth.DefaultMaximum;
            var player = new PlayerSessionSnapshot(mode, flying,
                new PlayerInventorySnapshot(selection, backpack, hotbar, cursor, equipment),
                health);

            var spatialLength = reader.ReadInt64();
            if (spatialLength < 16 ||
                spatialLength > DimensionChunkFileCodec.MaximumPayloadBytes ||
                spatialLength > end - source.Position)
                throw new InvalidDataException("Invalid embedded spatial save size.");

            var spatial = DimensionChunkFileCodec.Read(source, spatialLength);
            var count = SphereSessionFileCodec.ReadCount(reader, 256);
            var spheres = new SphereClockSnapshot[count];
            for (var i = 0; i < count; i++)
                spheres[i] = SphereSessionFileCodec.Read(reader,
                    blocks, fluids, dyes, layers);
            if (source.Position != end)
                throw new InvalidDataException("Unexpected gameplay session trailing bytes.");
            return new GameplaySessionSnapshot(
                spatial, name, tickRate, spawnCreatures, player, spheres, activeSphere);
        }
        catch (Exception error) when (error is ArgumentException or EndOfStreamException or
            OverflowException or DecoderFallbackException or FormatException)
        {
            throw new InvalidDataException("Malformed gameplay session data.", error);
        }
    }
}
