using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Gameplay;

/// <summary>
/// One bounded filesystem scan at a time. Work runs off the Godot thread;
/// the caller publishes only completed results on the main thread.
/// </summary>
public sealed class WorldCatalogScanController
{
    private readonly string _directory;
    private Task<IReadOnlyList<WorldSaveSummary>>? _pending;

    public WorldCatalogScanController(string worldsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);
        _directory = worldsDirectory;
    }

    public static WorldCatalogScanController FromUserDataDirectory() =>
        new(Path.Combine(OS.GetUserDataDir(), "worlds"));

    public bool Begin()
    {
        if (_pending is not null)
            return false;
        _pending = Task.Run(() => WorldSaveCatalog.Scan(_directory));
        return true;
    }

    public bool TryPoll(out IReadOnlyList<WorldSaveSummary> worlds, out string? error)
    {
        worlds = Array.Empty<WorldSaveSummary>();
        error = null;
        if (_pending is not { IsCompleted: true } pending)
            return false;

        _pending = null;
        try
        {
            worlds = pending.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        return true;
    }

    public bool TryOpenFolder(out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(_directory);
            var uri = new Uri(
                Path.GetFullPath(_directory) + Path.DirectorySeparatorChar).AbsoluteUri;
            var result = OS.ShellOpen(uri);
            if (result == Error.Ok)
                return true;
            error = $"Cannot open worlds folder: {result}";
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = exception.Message;
            return false;
        }
    }
}
