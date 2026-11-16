using System.Buffers.Binary;
using System.IO.Compression;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldThumbnailStorageTests
{
    [Fact]
    public void MissingThumbnailIsOptional()
    {
        WithWorld(directory => Assert.Null(WorldThumbnailStorage.ReadDataUri(directory)));
    }

    [Fact]
    public void PublishesAndReplacesOneBoundedPreviewWithoutChangingSaveFiles()
    {
        WithWorld(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "world.json"), "save");
            var one = PngImage(17);
            WorldThumbnailStorage.Publish(directory, one);
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(one),
                WorldThumbnailStorage.ReadDataUri(directory));
            var two = PngImage(23);
            WorldThumbnailStorage.Publish(directory, two);
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(two),
                WorldThumbnailStorage.ReadDataUri(directory));
            Assert.Equal("save", File.ReadAllText(Path.Combine(directory, "world.json")));
            Assert.Single(Directory.GetFiles(directory, "*.png"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        });
    }

    [Fact]
    public void RefusesWrongDimensionsMalformedAndOversizedImages()
    {
        WithWorld(directory =>
        {
            var valid = PngImage(17);
            var wrongDimensions = PngImage(17);
            BinaryPrimitives.WriteUInt32BigEndian(wrongDimensions.AsSpan(16, 4), 2048);
            RewriteIhdrCrc(wrongDimensions);
            Assert.False(WorldThumbnailStorage.IsValidPng(wrongDimensions));
            Assert.Throws<InvalidDataException>(() =>
                WorldThumbnailStorage.Publish(directory, wrongDimensions));
            Assert.Throws<InvalidDataException>(() =>
                WorldThumbnailStorage.Publish(directory,
                    new byte[WorldThumbnailStorage.MaximumBytes + 1]));
            Assert.Throws<InvalidDataException>(() =>
                WorldThumbnailStorage.Publish(directory, []));
            File.WriteAllBytes(Path.Combine(directory, WorldThumbnailStorage.FileName),
                wrongDimensions);
            Assert.Null(WorldThumbnailStorage.ReadDataUri(directory));
            File.WriteAllBytes(Path.Combine(directory, WorldThumbnailStorage.FileName),
                new byte[WorldThumbnailStorage.MaximumBytes + 1]);
            Assert.Null(WorldThumbnailStorage.ReadDataUri(directory));
            Assert.True(WorldThumbnailStorage.IsValidPng(valid));
        });
    }

    [Fact]
    public void DoesNotCreateWorldDirectoryForOptionalImage()
    {
        var missing = Path.Combine(Path.GetTempPath(), "asteria-thumbnail-" + Guid.NewGuid());
        Assert.Throws<FileNotFoundException>(() =>
            WorldThumbnailStorage.Publish(missing, PngImage(17)));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void RejectsTruncatedAppendedOrCorruptedPngWithoutBreakingWorldCatalog()
    {
        WithWorld(directory =>
        {
            var image = PngImage(32);
            Assert.True(WorldThumbnailStorage.IsValidPng(image));
            var headerOnly = image[..33];
            Assert.False(WorldThumbnailStorage.IsValidPng(headerOnly));
            var noEnd = image[..^12];
            Assert.False(WorldThumbnailStorage.IsValidPng(noEnd));
            Assert.False(WorldThumbnailStorage.IsValidPng(
                [.. image, 1]));

            var corruptCrc = image.ToArray();
            corruptCrc[42] ^= 0x55; // Mutate IDAT data without updating its CRC.
            Assert.False(WorldThumbnailStorage.IsValidPng(corruptCrc));

            var invalidFilter = PngImage(8, filter: 5);
            Assert.False(WorldThumbnailStorage.IsValidPng(invalidFilter));
            var shortPixels = PngImage(8, incompleteRaster: true);
            Assert.False(WorldThumbnailStorage.IsValidPng(shortPixels));

            foreach (var malformed in new[]
                { headerOnly, noEnd, corruptCrc, invalidFilter, shortPixels })
            {
                File.WriteAllBytes(Path.Combine(
                    directory, WorldThumbnailStorage.FileName), malformed);
                Assert.Null(WorldThumbnailStorage.ReadDataUri(directory));
                Assert.Throws<InvalidDataException>(() =>
                    WorldThumbnailStorage.Publish(directory, malformed));
            }
        });
    }

    [Fact]
    public void RgbPngAndAncillaryMetadataRemainValid()
    {
        var png = PngImage(12, channels: 3, includeMetadata: true);
        Assert.True(WorldThumbnailStorage.IsValidPng(png));
    }

    // Use an actual zlib-compressed 320x180 PNG with valid chunk CRCs.
    // The old tests used a 33-byte header that no browser could decode.
    private static byte[] PngImage(
        byte color, byte filter = 0,
        bool incompleteRaster = false, int channels = 4,
        bool includeMetadata = false)
    {
        var stride = 1 + WorldThumbnailStorage.Width * channels;
        var scanlines = new byte[
            incompleteRaster ? 10 : WorldThumbnailStorage.Height * stride];
        for (var y = 0; y < WorldThumbnailStorage.Height && !incompleteRaster; y++)
        {
            var offset = y * stride;
            scanlines[offset] = filter;
            scanlines.AsSpan(offset + 1, stride - 1).Fill(color);
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(
                       buffer, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(scanlines);
            compressed = buffer.ToArray();
        }

        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4),
            WorldThumbnailStorage.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4),
            WorldThumbnailStorage.Height);
        header[8] = 8;
        header[9] = channels == 4 ? (byte)6 : (byte)2;
        WriteChunk(output, "IHDR"u8, header);
        if (includeMetadata)
            WriteChunk(output, "tEXt"u8, "author\\0Asteria"u8);
        WriteChunk(output, "IDAT"u8, compressed);
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void RewriteIhdrCrc(byte[] png)
    {
        var crc = Crc32(png.AsSpan(12, 17));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29, 4), crc);
    }

    private static void WriteChunk(
        Stream stream, ReadOnlySpan<byte> name, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        stream.Write(name);
        stream.Write(data);
        var checksumInput = new byte[4 + data.Length];
        name.CopyTo(checksumInput);
        data.CopyTo(checksumInput.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(length, Crc32(checksumInput));
        stream.Write(length);
    }

    // Independent bitwise CRC oracle used to construct test fixtures.
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0
                    ? crc >> 1 : (crc >> 1) ^ 0xEDB88320u;
        }
        return ~crc;
    }

    private static void WithWorld(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "asteria-thumbnail-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
