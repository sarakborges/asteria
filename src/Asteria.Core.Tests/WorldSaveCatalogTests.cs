using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldSaveCatalogTests
{
    [Fact]
    public void MissingDirectoryReturnsEmptyListWithoutCreatingFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "asteria-catalog-" + Guid.NewGuid());
        Assert.Empty(WorldSaveCatalog.Scan(path));
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void OnlyActualManifestsAreDiscoveredAndAreNotClaimedRestorable()
    {
        WithRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "unrelated"));
            var saved = Path.Combine(root, "Saved World");
            Directory.CreateDirectory(saved);
            File.WriteAllText(Path.Combine(saved, WorldSaveCatalog.ManifestFileName),
                """{"formatVersion":1,"name":"Saved World","seed":"18446744073709551615","dimensionId":"asteria:umbral","day":4,"lastSavedUnixMs":1000000000,"playerPosition":[2.2,70.5,-3.1]}""");

            var entry = Assert.Single(WorldSaveCatalog.Scan(root));
            Assert.Equal("Saved World", entry.Id);
            Assert.Equal("18446744073709551615", entry.Seed);
            Assert.Equal("3", entry.DaysPassed);
            Assert.Equal("asteria:umbral", entry.Sphere);
            Assert.Equal("X: 2 · Z: -4 · Y: 70", entry.Coordinates);
            Assert.False(entry.Compatible);
        });
    }

    [Fact]
    public void MalformedAndOversizedManifestsAreVisibleButCannotLoad()
    {
        WithRoot(root =>
        {
            var broken = Path.Combine(root, "Broken");
            var oversized = Path.Combine(root, "Oversized");
            Directory.CreateDirectory(broken);
            Directory.CreateDirectory(oversized);
            File.WriteAllText(Path.Combine(broken, WorldSaveCatalog.ManifestFileName), "{broken");
            File.WriteAllText(Path.Combine(oversized, WorldSaveCatalog.ManifestFileName),
                new string('x', 33 * 1024));

            var saves = WorldSaveCatalog.Scan(root);
            Assert.Equal(new[] { "Broken", "Oversized" }, saves.Select(entry => entry.Id));
            Assert.All(saves, entry => Assert.False(entry.Compatible));
            Assert.All(saves, entry => Assert.Empty(entry.Seed));
        });
    }

    [Fact]
    public void ListingIsStableAndDoesNotFollowUnrelatedFolders()
    {
        WithRoot(root =>
        {
            foreach (var id in new[] { "Z", "A", "M" })
            {
                var world = Path.Combine(root, id);
                Directory.CreateDirectory(world);
                File.WriteAllText(Path.Combine(world, WorldSaveCatalog.ManifestFileName), "{}");
            }
            Assert.Equal(new[] { "A", "M", "Z" },
                WorldSaveCatalog.Scan(root).Select(entry => entry.Id));
        });
    }

    [Fact]
    public void CatalogLimitFailsExplicitlyInsteadOfSilentlyTruncating()
    {
        WithRoot(root =>
        {
            for (var i = 0; i < 257; i++)
                Directory.CreateDirectory(Path.Combine(root, i.ToString("D3")));
            Assert.Throws<IOException>(() => WorldSaveCatalog.Scan(root));
        });
    }

    private static void WithRoot(Action<string> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "asteria-catalog-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try { run(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
