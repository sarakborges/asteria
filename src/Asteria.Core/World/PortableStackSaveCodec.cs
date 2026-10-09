using System.Text;

namespace Asteria.Core.World;

/// <summary>
/// Portable item and block-state codec shared by inventories, containers and
/// detached drops. Namespaced IDs, not volatile registry indexes, cross disk.
/// The owning inventories still validate quantities and slot constraints.
/// </summary>
public static class PortableStackSaveCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const int MaxIdBytes = 512;
    private const int MaxMetadataBytes = 2048;
    private const int MaxMetadataPairs = 64;

    public static void Write(
        BinaryWriter writer, InventoryStack? stack,
        BlockRegistry blocks, DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(dyes);
        ArgumentNullException.ThrowIfNull(layers);

        writer.Write(stack is not null);
        if (stack is null) return;

        var entry = stack.Entry;
        writer.Write((byte)entry.Kind);
        WriteString(writer, entry.Id, MaxIdBytes);
        writer.Write((byte)entry.MaxStackSize);
        writer.Write((byte)stack.Quantity);
        if (entry.Metadata.Count > MaxMetadataPairs)
            throw new InvalidDataException("Too many portable item metadata entries.");
        writer.Write(checked((byte)entry.Metadata.Count));
        foreach (var (key, value) in entry.Metadata.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            WriteString(writer, key, MaxMetadataBytes);
            WriteString(writer, value, MaxMetadataBytes);
        }

        if (entry.Kind != InventoryEntryKind.Block) return;
        var block = entry.Block ?? throw new InvalidDataException("Missing portable block.");
        if (blocks.GetDefinition(block.Cell.Block).Id != entry.Id)
            throw new InvalidDataException("Portable block ID does not match runtime block state.");
        var cell = block.Cell;
        writer.Write((byte)cell.TextureRotation);
        writer.Write((byte)cell.Orientation);
        writer.Write((byte)cell.Facing);
        writer.Write(cell.State);
        writer.Write(!block.MicroblockMask.IsEmpty);
        if (!block.MicroblockMask.IsEmpty)
            WriteString(writer, block.MicroblockMask.ToHexString(), 128);

        var surface = block.SurfaceState;
        if (surface.DyeId is { } dye && !dyes.TryGet(dye, out _))
            throw new InvalidDataException("Portable block references an unknown dye.");
        writer.Write(surface.DyeId is not null);
        if (surface.DyeId is { } dyeId)
            WriteString(writer, dyeId, MaxIdBytes);
        writer.Write((byte)surface.Layers.Count);
        foreach (var layer in surface.Layers)
        {
            if (!layers.TryGet(layer.LayerId, out var definition) ||
                !definition!.Supports(layer.Face))
                throw new InvalidDataException("Portable block references an invalid attached layer.");
            writer.Write((byte)layer.Face);
            WriteString(writer, layer.LayerId, MaxIdBytes);
            writer.Write((byte)layer.Rotation);
        }
    }

    public static InventoryStack? Read(
        BinaryReader reader, BlockRegistry blocks,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(dyes);
        ArgumentNullException.ThrowIfNull(layers);

        if (!ReadBool(reader)) return null;
        try
        {
            var kind = (InventoryEntryKind)reader.ReadByte();
            if (!Enum.IsDefined(kind))
                throw new InvalidDataException("Unknown portable inventory kind.");
            var id = ReadString(reader, MaxIdBytes);
            var maxStack = reader.ReadByte();
            var quantity = reader.ReadByte();
            var count = reader.ReadByte();
            if (count > MaxMetadataPairs)
                throw new InvalidDataException("Portable item metadata count exceeds limit.");
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = ReadString(reader, MaxMetadataBytes);
                var value = ReadString(reader, MaxMetadataBytes);
                if (!metadata.TryAdd(key, value))
                    throw new InvalidDataException("Duplicate portable item metadata key.");
            }

            InventoryEntry entry;
            switch (kind)
            {
                case InventoryEntryKind.Block:
                {
                    if (count != 0 || maxStack != 64 ||
                        !blocks.TryGetId(id, out var blockId) || blockId.IsAir)
                        throw new InvalidDataException("Invalid portable block identity.");
                    var rotation = (TextureRotation)reader.ReadByte();
                    var orientation = (BlockOrientation)reader.ReadByte();
                    var facing = (HorizontalFacing)reader.ReadByte();
                    var state = reader.ReadUInt16();
                    if (!Enum.IsDefined(rotation) || !Enum.IsDefined(orientation) ||
                        !Enum.IsDefined(facing))
                        throw new InvalidDataException("Invalid portable block orientation.");
                    var mask = ReadBool(reader)
                        ? MicroblockMask.ParseHexString(ReadString(reader, 128))
                        : MicroblockMask.Empty;
                    string? dye = ReadBool(reader) ? ReadString(reader, MaxIdBytes) : null;
                    if (dye is not null && !dyes.TryGet(dye, out _))
                        throw new InvalidDataException("Unknown portable dye.");
                    var layersCount = reader.ReadByte();
                    if (layersCount > BlockSurfaceState.MaximumAttachedLayers)
                        throw new InvalidDataException("Too many portable block layers.");
                    var attached = new AttachedBlockLayer[layersCount];
                    for (var i = 0; i < layersCount; i++)
                    {
                        var face = (BlockFace)reader.ReadByte();
                        var layerId = ReadString(reader, MaxIdBytes);
                        var layerRotation = (TextureRotation)reader.ReadByte();
                        if (!Enum.IsDefined(face) || !Enum.IsDefined(layerRotation) ||
                            !layers.TryGet(layerId, out var definition) ||
                            !definition!.Supports(face))
                            throw new InvalidDataException("Invalid portable layer.");
                        attached[i] = new AttachedBlockLayer(face, layerId, layerRotation);
                    }
                    entry = InventoryEntry.FromBlock(id,
                        new BlockStateSnapshot(
                            new VoxelCell(blockId, rotation, orientation, facing, state),
                            mask, new BlockSurfaceState(dye, attached)));
                    break;
                }
                case InventoryEntryKind.Item:
                    entry = InventoryEntry.FromItem(id, metadata, maxStack);
                    break;
                case InventoryEntryKind.Tool:
                    entry = InventoryEntry.FromTool(id, metadata, maxStack);
                    break;
                case InventoryEntryKind.Layer:
                    if (count != 0 || maxStack != 64 ||
                        !layers.TryGet(id, out _))
                        throw new InvalidDataException("Invalid portable layer item.");
                    entry = InventoryEntry.FromLayer(id);
                    break;
                default:
                    throw new InvalidDataException("Unknown portable inventory kind.");
            }
            return new InventoryStack(entry, quantity);
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or OverflowException or
                EndOfStreamException or DecoderFallbackException)
        {
            throw new InvalidDataException("Malformed portable inventory stack.", exception);
        }
    }

    internal static void WriteString(
        BinaryWriter writer, string value, int maxBytes, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Utf8.GetBytes(value);
        if ((!allowEmpty && bytes.Length == 0) ||
            bytes.Length > ushort.MaxValue || bytes.Length > maxBytes)
            throw new InvalidDataException("Invalid saved text length.");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    internal static string ReadString(
        BinaryReader reader, int maxBytes, bool allowEmpty = false)
    {
        var length = reader.ReadUInt16();
        if ((!allowEmpty && length == 0) || length > maxBytes)
            throw new InvalidDataException("Invalid saved text length.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new InvalidDataException("Truncated saved text.");
        return Utf8.GetString(bytes);
    }

    internal static bool ReadBool(BinaryReader reader) =>
        reader.ReadByte() switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Invalid saved boolean.")
        };
}
