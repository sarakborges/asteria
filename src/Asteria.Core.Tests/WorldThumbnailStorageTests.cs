using System.Buffers.Binary;
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
            var one = PngHeader();
            WorldThumbnailStorage.Publish(directory, one);
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(one),
                WorldThumbnailStorage.ReadDataUri(directory));
            var two = PngHeader();
            two[^1] = 23;
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
            var valid = PngHeader();
            var wrongDimensions = PngHeader();
            BinaryPrimitives.WriteUInt32BigEndian(wrongDimensions.AsSpan(16, 4), 2048);
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
            WorldThumbnailStorage.Publish(missing, PngHeader()));
        Assert.False(Directory.Exists(missing));
    }

    // Sufficient valid PNG signature and IHDR for transport boundary tests;
    // actual image encoding belongs to the Godot viewport adapter.
    private static byte[] PngHeader()
    {
        var bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4),
            WorldThumbnailStorage.Width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4),
            WorldThumbnailStorage.Height);
        return bytes;
    }

    private static void WithWorld(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "asteria-thumbnail-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
