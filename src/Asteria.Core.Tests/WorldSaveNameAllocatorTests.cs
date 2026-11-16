using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldSaveNameAllocatorTests
{
    [Fact]
    public void NewWorldNamesDoNotOverwriteExistingSavedFolders()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "asteria-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Assert.Equal("World", WorldSaveNameAllocator.Allocate(root, "World"));
            Directory.CreateDirectory(Path.Combine(root, "World"));
            Assert.Equal("Copy of World", WorldSaveNameAllocator.Allocate(root, "World"));
            Directory.CreateDirectory(Path.Combine(root, "Copy of World"));
            Assert.Equal("Copy (2) of World",
                WorldSaveNameAllocator.Allocate(root, "World"));

            Assert.True(Directory.Exists(Path.Combine(root, "World")));
            Assert.True(Directory.Exists(Path.Combine(root, "Copy of World")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LongNamesRemainWithinValidatedSingleDirectoryComponent()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "asteria-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            var name = new string('w', WorldCreationOptions.MaximumNameLength);
            Directory.CreateDirectory(Path.Combine(root, name));
            var copied = WorldSaveNameAllocator.Allocate(root, name);
            Assert.StartsWith("Copy of ", copied);
            Assert.True(copied.Length <= WorldCreationOptions.MaximumNameLength);
            Assert.NotEqual(name, copied);
            Assert.Equal(copied, new WorldCreationOptions(copied, 0).Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
