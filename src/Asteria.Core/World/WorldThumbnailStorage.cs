using System.Buffers.Binary;

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

    public static bool IsValidPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length is >= 33 and <= MaximumBytes &&
        bytes[..8].SequenceEqual(PngSignature) &&
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4)) == 13 &&
        bytes.Slice(12, 4).SequenceEqual("IHDR"u8) &&
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4)) == Width &&
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4)) == Height;

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
