namespace Asteria.Core.World;

/// <summary>
/// Authoritative per-player mode and creative flight state, shared when
/// entering another Sphere. Presentation and input never mirror this state.
/// </summary>
public sealed class PlayerSessionState
{
    public const ulong DoubleTapWindowTicks = 12;

    private ulong? _nextJumpDeadline;

    public PlayerSessionState(PlayerGameMode initialMode = PlayerGameMode.Survival)
    {
        Validate(initialMode);
        GameMode = initialMode;
        IsFlying = initialMode.IsSpectator();
    }

    public PlayerGameMode GameMode { get; private set; }

    public bool IsFlying { get; private set; }

    public bool CanInteract => !GameMode.IsSpectator();

    public HeldBlockPlacement HeldBlock { get; } = new();

    public bool SetGameMode(PlayerGameMode mode)
    {
        Validate(mode);
        if (GameMode == mode) return false;
        GameMode = mode;
        IsFlying = mode.IsSpectator();
        CancelDoubleTap();
        return true;
    }

    public bool JumpPressed(ulong currentTick)
    {
        if (GameMode != PlayerGameMode.Creative)
        {
            return false;
        }

        if (_nextJumpDeadline is { } deadline &&
            currentTick <= deadline)
        {
            _nextJumpDeadline = null;
            IsFlying = !IsFlying;
            return true;
        }

        _nextJumpDeadline = ulong.MaxValue - currentTick <
            DoubleTapWindowTicks
                ? ulong.MaxValue
                : currentTick + DoubleTapWindowTicks;
        return false;
    }

    public bool Land()
    {
        if (GameMode != PlayerGameMode.Creative || !IsFlying)
        {
            return false;
        }

        IsFlying = false;
        CancelDoubleTap();
        return true;
    }

    public void CancelDoubleTap() => _nextJumpDeadline = null;

    private static void Validate(PlayerGameMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
