using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

/// <summary>
/// Bounded generated-world search performed without blocking Godot. The
/// owning generator identity rejects results after a Sphere transition.
/// </summary>
public sealed class ChatLocateController
{
    private Task<ChatLocateResult?>? _pending;
    private BiomeWorldGenerator? _generator;
    private string _query = "";

    public bool Begin(
        BiomeWorldGenerator generator,
        ChatCommandKind kind,
        string id,
        int worldX,
        int worldZ,
        string? structureId = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_pending is not null || kind is not (
                ChatCommandKind.LocateBiome or ChatCommandKind.LocateStructure))
            return false;

        _generator = generator;
        _query = id;
        const int radius = 512;
        _pending = Task.Run(() =>
        {
            if (kind == ChatCommandKind.LocateBiome)
            {
                var found = generator.FindNearestSurfaceBiome(
                    id, worldX, worldZ, radius);
                return found is null ? null : new ChatLocateResult(
                    id, found.X, generator.SurfaceHeight(found.X, found.Z),
                    found.Z);
            }

            var structure = generator.FindNearestSurfaceStructure(
                id, worldX, worldZ, radius);
            return structure is null ? null : new ChatLocateResult(
                id, structure.Value.AnchorX,
                structure.Value.AnchorY, structure.Value.AnchorZ);
        });
        return true;
    }

    public bool TryPoll(
        BiomeWorldGenerator? active,
        out ChatLocateResult? found,
        out string? error,
        out string searchedId,
        out bool stale)
    {
        found = null;
        error = null;
        stale = false;
        searchedId = "";
        if (_pending is not { IsCompleted: true } pending) return false;
        searchedId = _query;
        stale = !ReferenceEquals(active, _generator);
        _pending = null;
        _generator = null;
        _query = "";
        try
        {
            var result = pending.GetAwaiter().GetResult();
            if (!stale) found = result;
        }
        catch (Exception exception)
        {
            if (!stale) error = exception.Message;
        }
        return true;
    }
}

public sealed record ChatLocateResult(string Id, int X, int Y, int Z);
