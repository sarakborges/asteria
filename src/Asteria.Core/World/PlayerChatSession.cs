namespace Asteria.Core.World;

/// <summary>
/// Authoritative bounded local chat transcript for the active player session.
/// It never owns command execution, input focus or WebUI presentation.
/// </summary>
public sealed class PlayerChatSession
{
    public const int HistoryCapacity = 64;
    public const int MaxInputCharacters = 256;

    private readonly List<PlayerChatLine> _history = new(HistoryCapacity);
    private readonly IReadOnlyList<PlayerChatLine> _readOnlyHistory;

    public PlayerChatSession() =>
        _readOnlyHistory = _history.AsReadOnly();
    private ulong _nextId;

    public bool IsOpen { get; private set; }
    public IReadOnlyList<PlayerChatLine> History => _readOnlyHistory;

    public bool Open()
    {
        if (IsOpen) return false;
        IsOpen = true;
        return true;
    }

    public bool Close()
    {
        if (!IsOpen) return false;
        IsOpen = false;
        return true;
    }

    /// <summary>Closes the UI and accepts a bounded trimmed line for interpretation.</summary>
    public bool TrySubmit(string? input, out string line)
    {
        line = "";
        if (!IsOpen) return false;
        IsOpen = false;
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaxInputCharacters)
            return false;
        line = input.Trim();
        return line.Length > 0;
    }

    public void Append(
        string message,
        bool isError = false,
        string? localizationKey = null,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        AddLine(message, isError, localizationKey, parameters, null);
    }

    /// <summary>A validated locate result creates a link bound to its
    /// transcript identity. The WebUI may submit only that identity, never
    /// arbitrary coordinates as a privileged action.</summary>
    public void AppendLocated(
        string localizationKey,
        IReadOnlyDictionary<string, string> parameters,
        PlayerChatWarpTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localizationKey);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Y < 0 || string.IsNullOrWhiteSpace(target.Dimension))
            throw new ArgumentException("Invalid chat warp destination.", nameof(target));
        AddLine(localizationKey, false, localizationKey, parameters, target);
    }

    public bool TryGetWarpTarget(ulong messageId, out PlayerChatWarpTarget? target)
    {
        target = _history.FirstOrDefault(line => line.Id == messageId)?.WarpTarget;
        return target is not null;
    }

    private void AddLine(
        string message,
        bool isError,
        string? localizationKey,
        IReadOnlyDictionary<string, string>? parameters,
        PlayerChatWarpTarget? warpTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (_history.Count == HistoryCapacity)
            _history.RemoveAt(0);

        var id = ++_nextId;
        _history.Add(new PlayerChatLine(id, message, isError,
            localizationKey, parameters, warpTarget));
    }

    public void Reset()
    {
        IsOpen = false;
        _history.Clear();
        _nextId = 0;
    }
}

public sealed record PlayerChatLine(
    ulong Id, string Text, bool IsError,
    string? LocalizationKey = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    PlayerChatWarpTarget? WarpTarget = null);

public sealed record PlayerChatWarpTarget(int X, int Y, int Z, string Dimension);
