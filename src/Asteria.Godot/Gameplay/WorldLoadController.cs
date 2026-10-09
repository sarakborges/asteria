using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Gameplay;

/// <summary>
/// Prepares and validates a restored session entirely off the Godot thread.
/// Only Main publishes the prepared Core state and creates presentation.
/// </summary>
public sealed class WorldLoadController
{
    private readonly string _root;
    private readonly DimensionRegistry _dimensions;
    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly DyeRegistry _dyes;
    private readonly AttachedLayerRegistry _layers;
    private Task<(GameplaySessionSnapshot Snapshot,
        DimensionSessionStateStore States)>? _pending;

    public WorldLoadController(
        string worldsDirectory, DimensionRegistry dimensions,
        BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);
        _root = worldsDirectory;
        _dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _fluids = fluids ?? throw new ArgumentNullException(nameof(fluids));
        _dyes = dyes ?? throw new ArgumentNullException(nameof(dyes));
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
    }

    public bool Begin(string worldName)
    {
        if (_pending is not null)
            return false;

        // Validation rules are the same as new-world creation. A browser
        // message can never supply a path or cause directory traversal.
        var validated = new WorldCreationOptions(worldName, 0);
        if (validated.Name != worldName)
            throw new ArgumentException("Saved world ID is not canonical.", nameof(worldName));

        var directory = Path.Combine(_root, validated.Name);
        _pending = Task.Run(() =>
        {
            var restored = SessionSaveStorage.RestoreLatest(
                directory, _dimensions, _blocks, _fluids, _dyes, _layers);
            if (restored.Snapshot.Name != validated.Name)
                throw new InvalidDataException("Saved world does not match the selected folder.");
            return restored;
        });
        return true;
    }

    public bool TryPoll(
        out (GameplaySessionSnapshot Snapshot,
            DimensionSessionStateStore States)? result,
        out string? error)
    {
        result = null;
        error = null;
        if (_pending is not { IsCompleted: true } pending)
            return false;

        _pending = null;
        try
        {
            result = pending.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        return true;
    }
}
