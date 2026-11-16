using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class PackDirectoryCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "asteria-pack-discovery-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DiscoversUncommittedProjectPacksWithoutUserInstallation()
    {
        var project = Path.Combine(_root, "project");
        var user = Path.Combine(_root, "user");
        Directory.CreateDirectory(Path.Combine(project, "default"));
        Directory.CreateDirectory(Path.Combine(project, "my_pack"));
        var catalog = new PackDirectoryCatalog(user, project);

        var entry = Assert.Single(catalog.Discover());

        Assert.Equal("my_pack", entry.Name);
        Assert.Equal(Path.Combine(project, "my_pack"), catalog.Resolve(entry.Name));
    }

    [Fact]
    public void UserPacksTakePrecedenceOnNameCollision()
    {
        var project = Path.Combine(_root, "project");
        var user = Path.Combine(_root, "user");
        Directory.CreateDirectory(Path.Combine(project, "custom"));
        Directory.CreateDirectory(Path.Combine(user, "custom"));
        var catalog = new PackDirectoryCatalog(user, project);

        var entry = Assert.Single(catalog.Discover());

        Assert.Equal(Path.Combine(user, "custom"), entry.Directory);
        Assert.Equal(entry.Directory, catalog.Resolve("custom"));
    }

    [Fact]
    public void DiscoveryIsOrderedAndDefaultIsExcluded()
    {
        var project = Path.Combine(_root, "project");
        var user = Path.Combine(_root, "user");
        Directory.CreateDirectory(Path.Combine(project, "zeta"));
        Directory.CreateDirectory(Path.Combine(user, "alpha"));
        Directory.CreateDirectory(Path.Combine(user, "default"));

        var names = new PackDirectoryCatalog(user, project).Discover()
            .Select(entry => entry.Name).ToArray();

        Assert.Equal(new[] { "alpha", "zeta" }, names);
    }

    [Fact]
    public void UnsafeAndMissingPackFoldersCannotResolve()
    {
        var catalog = new PackDirectoryCatalog(
            Path.Combine(_root, "user"), Path.Combine(_root, "project"));

        Assert.Throws<DirectoryNotFoundException>(() => catalog.Resolve("missing"));
        Assert.Throws<ArgumentException>(() => catalog.Resolve("../outside"));
        Assert.Throws<ArgumentException>(() => catalog.Resolve("default"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
