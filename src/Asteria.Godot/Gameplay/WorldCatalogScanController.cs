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
    private DimensionRegistry? _dimensions;
    private BlockRegistry? _blocks;
    private FluidRegistry? _fluids;
    private DyeRegistry? _dyes;
    private AttachedLayerRegistry? _layers;

    public void ConfigureValidation(
        DimensionRegistry dimensions, BlockRegistry blocks,
        FluidRegistry fluids, DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        _dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _fluids = fluids ?? throw new ArgumentNullException(nameof(fluids));
        _dyes = dyes ?? throw new ArgumentNullException(nameof(dyes));
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
    }

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
        // Only decoded, fully restorable sessions are marked compatible.
        // Validation is serialized on one worker, never on the Godot frame.
        var dimensions = _dimensions;
        var blocks = _blocks;
        var fluids = _fluids;
        var dyes = _dyes;
        var layers = _layers;
        _pending = Task.Run(() =>
        {
            var entries = WorldSaveCatalog.Scan(_directory);
            if (dimensions is null || blocks is null || fluids is null ||
                dyes is null || layers is null)
                return entries;

            return (IReadOnlyList<WorldSaveSummary>)entries.Select(entry =>
            {
                var folder = Path.Combine(_directory, entry.Id);
                try
                {
                    var (snapshot, _) = SessionSaveStorage.RestoreLatest(
                        folder, dimensions, blocks, fluids, dyes, layers);
                    return snapshot.Name == entry.Id &&
                           WorldCreationSeed.Format(snapshot.Spatial.WorldSeed) == entry.Seed &&
                           snapshot.ActiveSphere?.Value == entry.Sphere
                        ? entry with { Compatible = true }
                        : entry;
                }
                catch (Exception error) when (
                    error is IOException or InvalidDataException or
                    UnauthorizedAccessException or ArgumentException or
                    KeyNotFoundException or InvalidOperationException)
                {
                    return entry;
                }
            }).ToArray();
        });
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
