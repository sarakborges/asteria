using System.Buffers.Binary;
using System.IO.Compression;

namespace Asteria.Core.World;

/// <summary>
/// Optional fixed-size world-card imagery, kept separate from authoritative
/// checksummed save generations. A damaged preview can never break loading.
/// </summary>
public static class WorldThumbnailStorage
{
    public const string FileName = "thumbnail.png";
    public const int Width = 320;
    public const int Height = 180;
    public const int MaximumBytes = 256 * 1024;

    private static readonly byte[] PngSignature =
        [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly uint[] PngCrcTable = BuildPngCrcTable();

    /// <summary>
    /// Validate the complete bounded PNG stream before it reaches the browser.
    /// A signature and IHDR alone are not an image: require sound chunks,
    /// checksums, an image payload, exactly 180 decoded scanlines and IEND.
    /// Godot's Rgba8 capture uses noninterlaced, 8-bit RGB(A).
    /// </summary>
    public static bool IsValidPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 57 or > MaximumBytes ||
            !bytes[..8].SequenceEqual(PngSignature) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4)) != 13 ||
            !bytes.Slice(12, 4).SequenceEqual("IHDR"u8) ||
            !ValidPngCrc(bytes.Slice(12, 17), bytes.Slice(29, 4)) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4)) != Width ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4)) != Height ||
            bytes[24] != 8 || bytes[26] != 0 || bytes[27] != 0 ||
            bytes[28] != 0)
            return false;

        var channels = bytes[25] switch
        {
            2 => 3, // 8-bit RGB
            6 => 4, // 8-bit RGBA
            _ => 0,
        };
        if (channels == 0) return false;

        using var compressed = new MemoryStream();
        var offset = 33;
        var hasImageData = false;
        var afterImageData = false;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 12) return false;
            var length = BinaryPrimitives.ReadUInt32BigEndian(
                bytes.Slice(offset, 4));
            if (length > bytes.Length - offset - 12) return false;

            var type = bytes.Slice(offset + 4, 4);
            var payload = bytes.Slice(offset + 8, (int)length);
            var crc = bytes.Slice(offset + 8 + (int)length, 4);
            if (!ValidChunkName(type) ||
                !ValidPngCrc(bytes.Slice(offset + 4, (int)length + 4), crc))
                return false;
            offset += (int)length + 12;

            if (type.SequenceEqual("IDAT"u8))
            {
                if (afterImageData) return false;
                compressed.Write(payload);
                hasImageData = true;
                continue;
            }
            if (hasImageData) afterImageData = true;

            if (type.SequenceEqual("IEND"u8))
                return length == 0 && hasImageData &&
                       offset == bytes.Length &&
                       ValidScanlines(compressed, channels);

            // Ancillary PNG metadata may precede or follow IDAT, but no
            // unknown critical chunk may be silently ignored.
            if (type.SequenceEqual("PLTE"u8))
            {
                if (hasImageData || length is < 3 or > 768 ||
                    length % 3 != 0)
                    return false;
            }
            else if ((type[0] & 0x20) == 0)
                return false;
        }
        return false;
    }

    private static bool ValidScanlines(MemoryStream compressed, int channels)
    {
        compressed.Position = 0;
        try
        {
            using var inflater = new ZLibStream(
                compressed, CompressionMode.Decompress, leaveOpen: true);
            var scanline = new byte[1 + Width * channels];
            for (var row = 0; row < Height; row++)
            {
                inflater.ReadExactly(scanline);
                // PNG supports exactly these five per-row filter methods.
                if (scanline[0] > 4) return false;
            }
            // Reject an image with extra decoded rows/pixels, rather than
            // allowing compressed payloads of unbounded decoded length.
            return inflater.ReadByte() == -1;
        }
        catch (Exception error) when (
            error is InvalidDataException or IOException)
        {
            return false;
        }
    }

    private static bool ValidChunkName(ReadOnlySpan<byte> name)
    {
        for (var i = 0; i < name.Length; i++)
            if (name[i] is not (>= (byte)'A' and <= (byte)'Z') &&
                name[i] is not (>= (byte)'a' and <= (byte)'z'))
                return false;
        // The third character is the PNG reserved bit.
        return (name[2] & 0x20) == 0;
    }

    private static bool ValidPngCrc(
        ReadOnlySpan<byte> data, ReadOnlySpan<byte> stored) =>
        PngCrc32(data) == BinaryPrimitives.ReadUInt32BigEndian(stored);

    private static uint PngCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
            crc = PngCrcTable[(int)((crc ^ value) & 255)] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] BuildPngCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) == 0
                    ? value >> 1 : (value >> 1) ^ 0xEDB88320u;
            table[i] = value;
        }
        return table;
    }

    public static void Publish(string worldDirectory, byte[] png)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldDirectory);
        ArgumentNullException.ThrowIfNull(png);
        if (!IsValidPng(png))
            throw new InvalidDataException("World thumbnail must be a bounded 320x180 PNG.");

        // The committed save already owns this directory. Never create a new
        // world or follow an unexpected folder link merely for a thumbnail.
        ValidateWorldDirectory(worldDirectory);
        var final = Path.Combine(worldDirectory, FileName);
        if (File.Exists(final) &&
            (File.GetAttributes(final) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("World thumbnail must not be a link.");

        var temporary = final + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                stream.Write(png);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, final, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static string? ReadDataUri(string worldDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldDirectory);
        ValidateWorldDirectory(worldDirectory);
        var path = Path.Combine(worldDirectory, FileName);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            info.Length is < 33 or > MaximumBytes)
            return null;

        using var stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read);
        if (stream.Length is < 33 or > MaximumBytes) return null;
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return IsValidPng(bytes)
            ? "data:image/png;base64," + Convert.ToBase64String(bytes)
            : null;
    }

    private static void ValidateWorldDirectory(string directory)
    {
        var attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("World thumbnail directory must be a real directory.");
    }
}
