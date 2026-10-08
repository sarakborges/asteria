using System.Security.Cryptography;
using System.Text;

namespace Asteria.Core.World;

/// <summary>
/// Bounded, versioned disk representation of a single authoritative chunk.
/// Uses namespaced content IDs rather than ephemeral runtime IDs. Lighting
/// is deliberately excluded because the lighting runtime rebuilds it.
/// This codec does not own chunk residency, I/O or world-save publication.
/// </summary>
public static class ChunkSaveCodec
{
    private const uint Magic = 0x48435341; // "ASCH" (little-endian)
    private const ushort FormatVersion = 1;
    private const int MaximumBytes = 8 * 1024 * 1024;
    private const int DigestBytes = 32;

    public static byte[] Encode(
        Chunk chunk, BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(dyes);
        ArgumentNullException.ThrowIfNull(layers);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);

            var blockEntries = new List<(int Index, VoxelCell Cell)>();
            chunk.VisitBlockCells((x, y, z, cell) =>
                blockEntries.Add((Index(x, y, z), cell)));
            writer.Write(blockEntries.Count);

            foreach (var (index, cell) in blockEntries)
            {
                var (x, y, z) = Position(index);
                writer.Write((ushort)index);
                writer.Write(blocks.GetDefinition(cell.Block).Id);
                writer.Write((byte)cell.TextureRotation);
                writer.Write((byte)cell.Orientation);
                writer.Write((byte)cell.Facing);
                writer.Write(cell.State);

                var hasMask = cell.HasMicroblockGeometry;
                writer.Write(hasMask);
                if (hasMask)
                {
                    var mask = chunk.GetMicroblockMask(x, y, z);
                    if (mask.IsEmpty || mask.IsFull)
                        throw new InvalidOperationException("Invalid chunk microblock palette reference.");
                    writer.Write(mask.ToHexString());
                }

                var surface = chunk.GetSurfaceState(x, y, z);
                if (surface.DyeId is { } dye && !dyes.TryGet(dye, out _))
                    throw new InvalidOperationException($"Unknown chunk dye: {dye}");
                writer.Write(surface.DyeId ?? "");
                writer.Write(checked((byte)surface.Layers.Count));
                foreach (var layer in surface.Layers)
                {
                    if (!layers.TryGet(layer.LayerId, out var definition) ||
                        !definition!.Supports(layer.Face))
                        throw new InvalidOperationException(
                            $"Invalid chunk attached layer: {layer.LayerId}");
                    writer.Write((byte)layer.Face);
                    writer.Write(layer.LayerId);
                    writer.Write((byte)layer.Rotation);
                }
            }

            var fluidEntries = new List<(int Index, FluidCell Fluid)>();
            chunk.VisitFluidCells((x, y, z, fluid) =>
                fluidEntries.Add((Index(x, y, z), fluid)));
            writer.Write(fluidEntries.Count);

            foreach (var (index, fluid) in fluidEntries)
            {
                var (x, y, z) = Position(index);
                if (!chunk.GetCell(x, y, z).IsEmpty)
                    throw new InvalidOperationException("Chunk contains both block and fluid in one voxel.");
                writer.Write((ushort)index);
                writer.Write(fluids.GetDefinition(fluid.Fluid).Id);
                writer.Write(fluid.Level);
                writer.Write(fluid.IsSource);
                writer.Write(fluid.SpreadDistance);
            }
        }

        if (stream.Length + DigestBytes > MaximumBytes)
            throw new InvalidDataException("Chunk snapshot exceeds maximum encoded size.");
        var payload = stream.ToArray();
        var digest = SHA256.HashData(payload);
        Array.Resize(ref payload, payload.Length + DigestBytes);
        digest.CopyTo(payload, payload.Length - DigestBytes);
        return payload;
    }

    public static Chunk Decode(
        ReadOnlySpan<byte> encoded, BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(dyes);
        ArgumentNullException.ThrowIfNull(layers);

        if (encoded.Length < 10 + DigestBytes || encoded.Length > MaximumBytes)
            throw new InvalidDataException("Invalid chunk snapshot length.");
        var data = encoded[..^DigestBytes];
        if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(data), encoded[^DigestBytes..]))
            throw new InvalidDataException("Chunk snapshot checksum mismatch.");

        // Decode a complete isolated chunk before offering it to the
        // authoritative VoxelWorld. Malformed snapshots cannot modify it.
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (reader.ReadUInt32() != Magic ||
                reader.ReadUInt16() != FormatVersion)
                throw new InvalidDataException("Unknown chunk snapshot format.");

            var chunk = new Chunk();
            var count = ReadCount(reader);
            var previous = -1;
            for (var i = 0; i < count; i++)
            {
                var index = ReadIndex(reader, ref previous);
                var (x, y, z) = Position(index);
                if (!blocks.TryGetId(reader.ReadString(), out var blockId))
                    throw new InvalidDataException("Chunk references unknown block.");
                var rotation = (TextureRotation)reader.ReadByte();
                var orientation = (BlockOrientation)reader.ReadByte();
                var facing = (HorizontalFacing)reader.ReadByte();
                if (blockId.IsAir || !Enum.IsDefined(rotation) ||
                    !Enum.IsDefined(orientation) || !Enum.IsDefined(facing))
                    throw new InvalidDataException("Invalid saved block state.");
                var state = reader.ReadUInt16();
                var hasMask = reader.ReadBoolean();
                chunk.SetCell(x, y, z, new VoxelCell(
                    blockId, rotation, orientation, facing, state));
                if (hasMask)
                {
                    var mask = MicroblockMask.ParseHexString(reader.ReadString());
                    chunk.SetMicroblockMask(x, y, z, mask);
                }

                var dye = reader.ReadString();
                if (dye.Length != 0 && !dyes.TryGet(dye, out _))
                    throw new InvalidDataException("Chunk references unknown dye.");
                var layerCount = reader.ReadByte();
                if (layerCount > BlockSurfaceState.MaximumAttachedLayers)
                    throw new InvalidDataException("Too many saved attached layers.");

                var attached = new AttachedBlockLayer[layerCount];
                for (var j = 0; j < attached.Length; j++)
                {
                    var face = (BlockFace)reader.ReadByte();
                    var id = reader.ReadString();
                    var layerRotation = (TextureRotation)reader.ReadByte();
                    if (!Enum.IsDefined(face) ||
                        !Enum.IsDefined(layerRotation) ||
                        !layers.TryGet(id, out var layerDefinition) ||
                        !layerDefinition!.Supports(face))
                        throw new InvalidDataException("Invalid saved attached layer.");
                    attached[j] = new AttachedBlockLayer(face, id, layerRotation);
                }

                if (dye.Length != 0 || attached.Length != 0)
                    chunk.SetSurfaceState(x, y, z,
                        new BlockSurfaceState(dye.Length == 0 ? null : dye, attached));
            }

            count = ReadCount(reader);
            previous = -1;
            for (var i = 0; i < count; i++)
            {
                var index = ReadIndex(reader, ref previous);
                var (x, y, z) = Position(index);
                if (!chunk.GetCell(x, y, z).IsEmpty ||
                    !fluids.TryGetId(reader.ReadString(), out var fluidId))
                    throw new InvalidDataException("Invalid saved fluid location or type.");

                var level = reader.ReadByte();
                var isSource = reader.ReadBoolean();
                var spread = reader.ReadUInt16();
                if (level is < FluidCell.MinLevel or > FluidCell.MaxLevel ||
                    (isSource && spread != 0))
                    throw new InvalidDataException("Invalid saved fluid state.");
                chunk.SetFluid(x, y, z, isSource
                    ? FluidCell.Source(fluidId, level)
                    : FluidCell.Spreading(fluidId, level, spread));
            }

            if (stream.Position != stream.Length)
                throw new InvalidDataException("Unexpected bytes after chunk payload.");
            return chunk;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or FormatException or
                ArgumentException or OverflowException)
        {
            throw new InvalidDataException("Invalid encoded chunk content.", exception);
        }
    }

    private static int ReadCount(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > Chunk.Volume)
            throw new InvalidDataException("Chunk entry count is out of bounds.");
        return count;
    }

    private static int ReadIndex(BinaryReader reader, ref int previous)
    {
        var index = reader.ReadUInt16();
        if (index >= Chunk.Volume || index <= previous)
            throw new InvalidDataException("Chunk voxel indexes are not strictly increasing.");
        previous = index;
        return index;
    }

    private static int Index(int x, int y, int z) =>
        x + Chunk.Size * (z + Chunk.Size * y);

    private static (int X, int Y, int Z) Position(int index) =>
        (index % Chunk.Size,
         index / Chunk.Size / Chunk.Size,
         index / Chunk.Size % Chunk.Size);
}
