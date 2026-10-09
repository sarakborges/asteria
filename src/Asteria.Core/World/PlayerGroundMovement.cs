namespace Asteria.Core.World;

/// <summary>
/// Pure transient input/posture state for native player movement. Running
/// uses the same 12-world-tick double-forward window as MineClone. It owns
/// neither collision geometry nor player inventory/session persistence.
/// </summary>
public sealed class PlayerGroundMovement
{
    public const ulong RunDoubleTapWindowTicks = 12;
    public const float RunSpeedMultiplier = 1.5f;
    public const float CrouchSpeedMultiplier = 0.3f;

    private bool _forwardHeld;
    private ulong? _runDeadlineTick;

    public bool IsRunning { get; private set; }
    public bool IsCrouching { get; private set; }

    public void ForwardChanged(bool pressed, ulong currentWorldTick)
    {
        // A release stops an active run without clearing an unfinished
        // double-tap window (a tap necessarily contains a release).
        if (!pressed)
        {
            _forwardHeld = false;
            IsRunning = false;
            return;
        }

        if (_forwardHeld) return;
        _forwardHeld = true;

        if (IsCrouching)
        {
            CancelRunning();
            return;
        }

        if (_runDeadlineTick is { } deadline &&
            currentWorldTick <= deadline)
        {
            IsRunning = true;
            _runDeadlineTick = null;
            return;
        }

        IsRunning = false;
        _runDeadlineTick =
            ulong.MaxValue - currentWorldTick < RunDoubleTapWindowTicks
                ? ulong.MaxValue
                : currentWorldTick + RunDoubleTapWindowTicks;
    }

    /// <summary>
    /// Physical clearance comes from Godot's actual collision shapes.
    /// Crouching wins over running; standing is forbidden under a low roof.
    /// </summary>
    public bool UpdateCrouch(bool requested, bool hasStandingClearance)
    {
        var before = IsCrouching;
        if (requested)
        {
            IsCrouching = true;
            CancelRunning();
        }
        else if (hasStandingClearance)
        {
            IsCrouching = false;
        }

        return before != IsCrouching;
    }

    public float SpeedMultiplier(bool flying, bool immersed)
    {
        // Swim speed is definition-owned and creative/spectator flight
        // retains its own movement response.
        if (flying || immersed) return 1f;
        return IsCrouching ? CrouchSpeedMultiplier :
            IsRunning ? RunSpeedMultiplier : 1f;
    }

    public void CancelRunning()
    {
        IsRunning = false;
        _runDeadlineTick = null;
    }

    public void Reset()
    {
        _forwardHeld = false;
        IsCrouching = false;
        CancelRunning();
    }
}
