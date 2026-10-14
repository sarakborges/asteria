using System.Text.Json;

namespace Asteria.ModAPI;

/// <summary>A stable, engine-independent mod entry point.</summary>
public interface IAsteriaMod
{
    void Initialize(IModContext context);
}

public interface IModContext
{
    string ModId { get; }
    JsonElement Settings { get; }
    IGameplayHooks Hooks { get; }
}

/// <summary>
/// An immutable observation at the beginning of a native block break.
/// Returning false from a handler cancels that operation before mutation.
/// </summary>
public readonly record struct BeforeBlockBreak(
    string BlockId, int X, int Y, int Z);

public interface IGameplayHooks
{
    void RegisterBeforeBlockBreak(Func<BeforeBlockBreak, bool> callback);
}
