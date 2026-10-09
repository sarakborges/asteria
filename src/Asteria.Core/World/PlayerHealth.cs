namespace Asteria.Core.World;

public enum PlayerDamageResult : byte
{
    Ignored,
    Hurt,
    Killed,
}

/// <summary>
/// Single authoritative player-life owner shared between Spheres.
/// No renderer, browser view or physics adapter may mutate Current directly.
/// </summary>
public sealed class PlayerHealth
{
    public const float DefaultMaximum = 20f;
    public const float SafeFallDistance = 3f;

    public PlayerHealth(float maximum = DefaultMaximum)
    {
        if (!float.IsFinite(maximum) || maximum <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        Maximum = maximum;
        Current = maximum;
    }

    public float Maximum { get; }
    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;
    public ulong Revision { get; private set; }

    public PlayerDamageResult Damage(float amount, PlayerGameMode mode)
    {
        if (!float.IsFinite(amount) || amount < 0f)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (mode != PlayerGameMode.Survival || IsDead || amount == 0f)
            return PlayerDamageResult.Ignored;

        Current = MathF.Max(0f, Current - amount);
        Revision++;
        return IsDead ? PlayerDamageResult.Killed : PlayerDamageResult.Hurt;
    }

    /// <summary>
    /// Fall damage uses actual descended distance, not framerate, velocity
    /// snapshots or cosmetic fall animations. One heart is two hit points.
    /// </summary>
    public PlayerDamageResult Land(float distance, PlayerGameMode mode)
    {
        if (!float.IsFinite(distance) || distance < 0f)
            throw new ArgumentOutOfRangeException(nameof(distance));
        var amount = MathF.Floor(MathF.Max(0f, distance - SafeFallDistance));
        return Damage(amount, mode);
    }

    public bool Heal(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0f)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (IsDead || amount == 0f || Current == Maximum) return false;
        Current = MathF.Min(Maximum, Current + amount);
        Revision++;
        return true;
    }

    public bool Respawn()
    {
        if (!IsDead) return false;
        Current = Maximum;
        Revision++;
        return true;
    }

    public void Restore(float current)
    {
        if (!float.IsFinite(current) || current < 0f || current > Maximum)
            throw new InvalidDataException("Invalid saved player health.");
        Current = current;
        Revision++;
    }
}
