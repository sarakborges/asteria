using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldSaveDeletionTests
{
    [Fact]
    public void DeletesOnlySelectedWorldAndKeepsItsNeighbors()
    {
        WithRoot(root =>
        {
            var first = CreateSave(root, "First");
            var second = CreateSave(root, "Second");
            File.WriteAllText(Path.Combine(first, "world.data"), "test");
            WorldSaveDeletion.Delete(root, "First");
            Assert.False(Directory.Exists(first));
            Assert.True(File.Exists(Path.Combine(second, WorldSaveCatalog.ManifestFileName)));
        });
    }

    [Theory]
    [InlineData("../Outside")]
    [InlineData(" Second ")]
    [InlineData("Nested/Child")]
    [InlineData(".")]
    [InlineData("CON")]
    public void RefusesNoncanonicalOrUnsafeIds(string id)
    {
        WithRoot(root =>
        {
            CreateSave(root, "Second");
            Assert.Throws<ArgumentException>(() => WorldSaveDeletion.Delete(root, id));
            Assert.True(Directory.Exists(Path.Combine(root, "Second")));
        });
    }

    [Fact]
    public void DoesNotDeleteUnrelatedDirectoryWithoutManifest()
    {
        WithRoot(root =>
        {
            var unrelated = Path.Combine(root, "Unrelated");
            Directory.CreateDirectory(unrelated);
            Assert.Throws<IOException>(() => WorldSaveDeletion.Delete(root, "Unrelated"));
            Assert.True(Directory.Exists(unrelated));
        });
    }

    [Fact]
    public void AllowsRemovalOfBrokenManifestSave()
    {
        WithRoot(root =>
        {
            var save = CreateSave(root, "Broken");
            File.WriteAllText(Path.Combine(save, WorldSaveCatalog.ManifestFileName), "{invalid");
            WorldSaveDeletion.Delete(root, "Broken");
            Assert.False(Directory.Exists(save));
        });
    }

    [Fact]
    public void RejectsLinkedChildWithoutDeletingExternalContent()
    {
        WithRoot(root =>
        {
            var target = Path.Combine(root, "External");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "keep.txt"), "safe");
            var save = CreateSave(root, "Linked");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(save, "external-link"), target);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return; // Filesystem disallows creation; checked where supported.
            }

            Assert.Throws<IOException>(() => WorldSaveDeletion.Delete(root, "Linked"));
            Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
            Assert.True(Directory.Exists(save));
        });
    }

    private static string CreateSave(string root, string name)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, WorldSaveCatalog.ManifestFileName), "{}");
        return folder;
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "asteria-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
