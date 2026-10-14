namespace Asteria.Core.Content;

/// <summary>
/// Locates external packs in the source checkout and user-data directory.
/// User-data packs win name collisions. The built-in default is not selectable.
/// </summary>
public sealed class PackDirectoryCatalog
{
    private readonly string _userRoot;
    private readonly string _projectRoot;

    public PackDirectoryCatalog(string userPacksDirectory, string projectPacksDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPacksDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPacksDirectory);
        _userRoot = Path.GetFullPath(userPacksDirectory);
        _projectRoot = Path.GetFullPath(projectPacksDirectory);
    }

    public IReadOnlyList<PackDirectoryLocation> Discover()
    {
        var folders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        AddFolders(_projectRoot, folders);
        AddFolders(_userRoot, folders);
        return folders.Select(pair => new PackDirectoryLocation(pair.Key, pair.Value))
            .ToArray();
    }

    public string Resolve(string folderName)
    {
        _ = new PackOverlayEntry(folderName, PackDomains.Both);
        foreach (var root in new[] { _userRoot, _projectRoot })
        {
            var path = Path.Combine(root, folderName);
            if (Directory.Exists(path))
                return path;
        }
        throw new DirectoryNotFoundException($"Pack '{folderName}' is not installed.");
    }

    private static void AddFolders(string root, IDictionary<string, string> folders)
    {
        if (!Directory.Exists(root))
            return;
        foreach (var path in Directory.EnumerateDirectories(root)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name != PackSelection.DefaultPackName)
                folders[name] = path;
        }
    }
}

public sealed record PackDirectoryLocation(string Name, string Directory);
