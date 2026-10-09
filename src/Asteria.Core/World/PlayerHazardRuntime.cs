namespace Asteria.Core.World;

/// <summary>
/// Time-based hazards consume existing native collision/contact samples.
/// Health remains the sole HP owner; this runtime only schedules exposure
/// and contact pulses. No biome IDs, physics or rendering dependencies.
/// </summary>
public sealed class PlayerHazardRuntime
{
    public const float BreathCapacitySeconds = 15f;
    public const float CreatureContactIntervalSeconds = 0.8f;
    private float _breathSeconds = BreathCapacitySeconds;
    private float _drownPulseSeconds;
    private float _fluidPulseSeconds;
    private float _creatureCooldownSeconds;

    public float BreathSeconds => _breathSeconds;
    public bool BreathDepleting { get; private set; }

    public void AdvanceTime(float elapsed)
    {
        ValidateSeconds(elapsed);
        _creatureCooldownSeconds = MathF.Max(0f, _creatureCooldownSeconds - elapsed);
    }

    public PlayerDamageResult TouchCreature(
        float damage, float protection, PlayerHealth health, PlayerGameMode mode)
    {
        ArgumentNullException.ThrowIfNull(health);
        if (!float.IsFinite(damage) || damage < 0f ||
            !float.IsFinite(protection) || protection < 0f || protection > 1f)
            throw new ArgumentOutOfRangeException(nameof(damage));

        if (mode != PlayerGameMode.Survival || health.IsDead ||
            damage == 0f || _creatureCooldownSeconds > 0f)
            return PlayerDamageResult.Ignored;

        // The cooldown belongs to the real contact event, even if protection
        // absorbs it completely, avoiding unbounded same-frame retriggers.
        _creatureCooldownSeconds = CreatureContactIntervalSeconds;
        return health.Damage(damage * (1f - protection), mode);
    }

    public PlayerDamageResult AdvanceFluid(
        float elapsed, bool eyeSubmerged, bool bodyImmersed,
        bool depletesBreath, float contactDamagePerSecond,
        PlayerHealth health, PlayerGameMode mode)
    {
        ArgumentNullException.ThrowIfNull(health);
        ValidateSeconds(elapsed);
        if (!float.IsFinite(contactDamagePerSecond) ||
            contactDamagePerSecond < 0f || contactDamagePerSecond > 100f)
            throw new ArgumentOutOfRangeException(nameof(contactDamagePerSecond));

        if (mode != PlayerGameMode.Survival || health.IsDead)
        {
            ResetFluid();
            return PlayerDamageResult.Ignored;
        }

        BreathDepleting = eyeSubmerged && depletesBreath;
        var oldBreath = _breathSeconds;
        var drowningElapsed = 0f;
        if (eyeSubmerged && depletesBreath)
        {
            _breathSeconds = MathF.Max(0f, _breathSeconds - elapsed);
            drowningElapsed = MathF.Max(0f, elapsed - oldBreath);
        }
        else
        {
            _breathSeconds = BreathCapacitySeconds;
            _drownPulseSeconds = 0f;
        }

        PlayerDamageResult result = PlayerDamageResult.Ignored;
        _drownPulseSeconds += drowningElapsed;
        while (_drownPulseSeconds >= 1f && !health.IsDead)
        {
            _drownPulseSeconds -= 1f;
            result = Combine(result, health.Damage(2f, mode));
        }

        if (bodyImmersed && contactDamagePerSecond > 0f)
        {
            _fluidPulseSeconds += elapsed;
            while (_fluidPulseSeconds >= 1f && !health.IsDead)
            {
                _fluidPulseSeconds -= 1f;
                result = Combine(result, health.Damage(contactDamagePerSecond, mode));
            }
        }
        else
        {
            _fluidPulseSeconds = 0f;
        }

        return result;
    }

    public void Reset()
    {
        ResetFluid();
        _creatureCooldownSeconds = 0f;
    }

    private void ResetFluid()
    {
        _breathSeconds = BreathCapacitySeconds;
        BreathDepleting = false;
        _drownPulseSeconds = 0f;
        _fluidPulseSeconds = 0f;
    }

    private static PlayerDamageResult Combine(PlayerDamageResult first, PlayerDamageResult second) =>
        first == PlayerDamageResult.Killed || second == PlayerDamageResult.Killed
            ? PlayerDamageResult.Killed
            : first == PlayerDamageResult.Hurt || second == PlayerDamageResult.Hurt
                ? PlayerDamageResult.Hurt : PlayerDamageResult.Ignored;

    private static void ValidateSeconds(float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0f || elapsed > 1f)
            throw new ArgumentOutOfRangeException(nameof(elapsed),
                "Exposure samples must be bounded to a single physical frame.");
    }
}
