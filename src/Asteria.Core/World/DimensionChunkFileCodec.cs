using System.Text;

namespace Asteria.Core.World;

/// <summary>
/// Deterministic, bounded wire format for spatial snapshots. No file system
/// effects or mutable gameplay state belong to this codec.
/// </summary>
public static class DimensionChunkFileCodec
{
    private const uint Magic = 0x43535041; // APSC
    private const ushort Version = 1;
    private const int MaximumSphereCount = 256;
    private const int MaximumIdBytes = 256;
    internal const long MaximumPayloadBytes = 512L * 1024 * 1024;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static void Write(Stream target, DimensionChunkSaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!target.CanWrite || !target.CanSeek)
            throw new ArgumentException("Snapshot output must be writable and seekable.", nameof(target));

        var start = target.Position;
        using var writer = new BinaryWriter(target, StrictUtf8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(snapshot.WorldSeed);
        WriteGeneration(writer, snapshot.Generation);
        writer.Write(checked((ushort)snapshot.Spheres.Count));

        foreach (var sphere in snapshot.Spheres)
        {
            WriteId(writer, sphere.Dimension.Value);
            writer.Write(sphere.Chunks.Chunks.Count);
            foreach (var chunk in sphere.Chunks.Chunks)
            {
                writer.Write(chunk.Coordinate.X);
                writer.Write(chunk.Coordinate.Y);
                writer.Write(chunk.Coordinate.Z);
                writer.Write(chunk.Resident);
                writer.Write(chunk.Dirty);
                writer.Write(chunk.ByteLength);
                writer.Write(chunk.EncodedChunk);
                if (target.Position - start > MaximumPayloadBytes)
                    throw new InvalidDataException("Spatial save exceeds maximum size.");
            }
        }

        if (target.Position - start > MaximumPayloadBytes)
            throw new InvalidDataException("Spatial save exceeds maximum size.");
    }

    public static DimensionChunkSaveSnapshot Read(Stream source, long payloadLength)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("Snapshot input must be readable and seekable.", nameof(source));
        if (payloadLength < 16 || payloadLength > MaximumPayloadBytes ||
            source.Length - source.Position < payloadLength)
            throw new InvalidDataException("Invalid spatial snapshot payload size.");

        var end = checked(source.Position + payloadLength);
        using var reader = new BinaryReader(source, StrictUtf8, leaveOpen: true);
        try
        {
            if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version)
                throw new InvalidDataException("Unsupported spatial snapshot format.");

            var seed = reader.ReadUInt64();
            var generation = ReadGeneration(reader);
            var sphereCount = reader.ReadUInt16();
            if (sphereCount > MaximumSphereCount)
                throw new InvalidDataException("Spatial snapshot has too many Spheres.");

            var spheres = new List<SavedSphereChunks>(sphereCount);
            var totalChunks = 0;
            for (var i = 0; i < sphereCount; i++)
            {
                var dimension = new DimensionId(ReadId(reader));
                var chunkCount = reader.ReadInt32();
                if (chunkCount < 0 ||
                    chunkCount > VoxelWorldSaveSnapshot.MaximumChunks ||
                    (long)totalChunks + chunkCount > VoxelWorldSaveSnapshot.MaximumChunks)
                    throw new InvalidDataException("Invalid saved chunk count.");
                totalChunks += chunkCount;
                var chunks = new List<SavedWorldChunk>(chunkCount);
                for (var j = 0; j < chunkCount; j++)
                {
                    var coord = new ChunkCoord(
                        reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                    var resident = ReadBoolean(reader);
                    var dirty = ReadBoolean(reader);
                    var length = reader.ReadInt32();
                    if (length is < 42 or > 8 * 1024 * 1024 ||
                        end - source.Position < length)
                        throw new InvalidDataException("Invalid encoded chunk length.");
                    var bytes = reader.ReadBytes(length);
                    if (bytes.Length != length)
                        throw new EndOfStreamException("Truncated spatial chunk.");
                    chunks.Add(new SavedWorldChunk(coord, resident, dirty, bytes));
                }
                spheres.Add(new SavedSphereChunks(
                    dimension, new VoxelWorldSaveSnapshot(chunks)));
            }

            if (source.Position != end)
                throw new InvalidDataException("Unexpected trailing snapshot bytes.");
            return new DimensionChunkSaveSnapshot(seed, generation, spheres);
        }
        catch (Exception error) when (error is EndOfStreamException or
            ArgumentException or OverflowException or DecoderFallbackException)
        {
            throw new InvalidDataException("Malformed spatial snapshot.", error);
        }
    }

    private static void WriteGeneration(BinaryWriter writer, WorldGenerationOptions options)
    {
        writer.Write((byte)options.Mode);
        writer.Write(options.SpawnBiome is not null);
        if (options.SpawnBiome is { } spawnBiome)
            WriteId(writer, spawnBiome);
        writer.Write(options.BiomeSizeTenths);
        writer.Write(options.SpawnStructures);
        writer.Write(options.SingleBiome);
        writer.Write(options.SpawnCaves);
        writer.Write(options.SpawnOceans);
    }

    private static WorldGenerationOptions ReadGeneration(BinaryReader reader)
    {
        var mode = (WorldGenerationMode)reader.ReadByte();
        var spawnBiome = ReadBoolean(reader) ? ReadId(reader) : null;
        var size = reader.ReadInt32();
        var structures = ReadBoolean(reader);
        var singleBiome = ReadBoolean(reader);
        var caves = ReadBoolean(reader);
        var oceans = ReadBoolean(reader);
        return new WorldGenerationOptions(mode, spawnBiome, size,
            structures, singleBiome, caves, oceans);
    }

    private static void WriteId(BinaryWriter writer, string id)
    {
        var bytes = StrictUtf8.GetBytes(id);
        if (bytes.Length is 0 or > MaximumIdBytes)
            throw new InvalidDataException("Saved content ID is missing or too long.");
        writer.Write(checked((ushort)bytes.Length));
        writer.Write(bytes);
    }

    private static string ReadId(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        if (length is 0 or > MaximumIdBytes)
            throw new InvalidDataException("Invalid saved content ID length.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("Truncated saved content ID.");
        return StrictUtf8.GetString(bytes);
    }

    private static bool ReadBoolean(BinaryReader reader) =>
        reader.ReadByte() switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Invalid boolean in spatial snapshot.")
        };
}
