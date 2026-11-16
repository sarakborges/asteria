using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

/// <summary>
/// Bounded single-flight deletion. Never accepts paths from WebUI; Main
/// decides when no active/load/catalog session can reference the directory.
/// </summary>
public sealed class WorldDeleteController
{
    private readonly string _worldsDirectory;
    private Task? _pending;

    public WorldDeleteController(string worldsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);
        _worldsDirectory = worldsDirectory;
    }

    public bool IsBusy => _pending is not null;

    public bool Begin(string id)
    {
        if (IsBusy)
            return false;

        // Validate synchronously before enqueueing any filesystem work.
        var validated = new WorldCreationOptions(id, 0);
        if (validated.Name != id)
            throw new ArgumentException("World ID is not canonical.", nameof(id));

        _pending = Task.Run(() => WorldSaveDeletion.Delete(_worldsDirectory, id));
        return true;
    }

    public bool TryPoll(out string? error)
    {
        error = null;
        if (_pending is not { IsCompleted: true } pending)
            return false;

        _pending = null;
        try
        {
            pending.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        return true;
    }
}
