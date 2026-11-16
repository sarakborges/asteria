using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

/// <summary>
/// Generated-world locate queries run off the Godot thread. Obsolete searches
/// are cancelled when their Sphere's generator is no longer active.
/// </summary>
public sealed class ChatLocateController
{
    // Locate is a generated-world query, independent of chunk residency and
    // render distance. Keep its search limit aligned with MineClone.
    public const int MaxSearchRadius = 32_768;

    private Task<ChatLocateResult?>? _pending;
    private CancellationTokenSource? _cancellation;
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

        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        _cancellation = cancellation;
        _generator = generator;
        _query = id;
        _pending = Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (kind == ChatCommandKind.LocateBiome)
            {
                var found = generator.FindNearestSurfaceBiome(
                    id, worldX, worldZ, MaxSearchRadius, token);
                token.ThrowIfCancellationRequested();
                return found is null ? null : new ChatLocateResult(
                    id, found.X, generator.SurfaceHeight(found.X, found.Z),
                    found.Z);
            }

            var structure = generator.FindNearestSurfaceStructure(
                id, worldX, worldZ, MaxSearchRadius, structureId, token);
            return structure is null ? null : new ChatLocateResult(
                id, structure.Value.AnchorX,
                structure.Value.AnchorY, structure.Value.AnchorZ);
        }, token);
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
        if (_pending is not { } pending) return false;

        stale = !ReferenceEquals(active, _generator);
        if (!stale && !pending.IsCompleted) return false;

        searchedId = _query;
        if (stale) _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _pending = null;
        _generator = null;
        _query = "";

        if (stale) return true;
        try
        {
            found = pending.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        return true;
    }
}

public sealed record ChatLocateResult(string Id, int X, int Y, int Z);
