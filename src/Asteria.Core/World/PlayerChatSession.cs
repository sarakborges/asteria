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
    private ulong _nextId;

    public bool IsOpen { get; private set; }
    public IReadOnlyList<PlayerChatLine> History => _history;

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

    public void Append(string message, bool isError = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (_history.Count == HistoryCapacity)
            _history.RemoveAt(0);

        var id = ++_nextId;
        _history.Add(new PlayerChatLine(id, message, isError));
    }

    public void Reset()
    {
        IsOpen = false;
        _history.Clear();
        _nextId = 0;
    }
}

public sealed record PlayerChatLine(ulong Id, string Text, bool IsError);
