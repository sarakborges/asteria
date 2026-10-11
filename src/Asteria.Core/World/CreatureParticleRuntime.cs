using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// One presentation phase and event revision, derived from authoritative
/// creature state. The same choice drives model clips and authored particles.
/// </summary>
public readonly record struct CreatureVisualState(string Phase, uint Revision)
{
    public static CreatureVisualState From(CreatureInstanceState creature) =>
        creature.IsDying ? new("death", creature.AttacksReceived) :
        creature.HurtSecondsRemaining > 0f ? new("hurt", creature.AttacksReceived) :
        creature.AttackSecondsRemaining > 0f ? new("attack", creature.AttacksDealt) :
        new(creature.Motion.Phase switch
        {
            CreatureHopPhase.Idle => "idle",
            CreatureHopPhase.Anticipate => "anticipate",
            CreatureHopPhase.Airborne => "airborne",
            CreatureHopPhase.Land => "land",
            _ => "idle",
        }, 0);
}

public sealed class CreatureParticleState
{
    internal CreatureParticleState(Vector3 position, Vector3 velocity,
        Vector4 color, float gravity, float size, float lifetime, float endScale)
    {
        Position = position;
        Velocity = velocity;
        Color = color;
        Gravity = gravity;
        BaseSize = size;
        Lifetime = lifetime;
        EndScale = endScale;
    }

    public Vector3 Position { get; internal set; }
    internal Vector3 Velocity { get; set; }
    internal float Gravity { get; }
    internal float Age { get; set; }
    internal float Lifetime { get; }
    internal float BaseSize { get; }
    internal float EndScale { get; }
    public Vector4 Color { get; }
    public float Scale => MathF.Max(0.001f,
        BaseSize * (1f + (EndScale - 1f) * Age / Lifetime));
}

/// <summary>
/// Bounded visual-only particle simulation. Authored creature animation phases
/// control emission; no gameplay state, collision, or save data is modified.
/// </summary>
public sealed class CreatureParticleRuntime
{
    public const int MaximumActive = 512;
    private const float MaximumDelta = 0.05f;
    private const uint InitialRandomState = 0xA5A51F3Du;

    private sealed class Emitter
    {
        public CreatureVisualState State;
        public float Timer;
    }

    private readonly PackContentRegistry<CreatureDefinition> _definitions;
    private readonly Dictionary<ulong, Emitter> _emitters = [];
    private readonly List<CreatureParticleState> _particles = new(MaximumActive);
    private uint _random = InitialRandomState;

    public CreatureParticleRuntime(PackContentRegistry<CreatureDefinition> definitions) =>
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    public IReadOnlyList<CreatureParticleState> Particles => _particles;

    public void Advance(float elapsed, IEnumerable<CreatureInstanceState> creatures)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0f)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        ArgumentNullException.ThrowIfNull(creatures);
        var dt = MathF.Min(MaximumDelta, elapsed);
        for (var i = _particles.Count - 1; i >= 0; --i)
        {
            var particle = _particles[i];
            particle.Age += dt;
            if (particle.Age >= particle.Lifetime)
            {
                _particles.RemoveAt(i);
                continue;
            }

            var velocity = particle.Velocity;
            velocity.Y += particle.Gravity * dt;
            particle.Position += velocity * dt;
            particle.Velocity = velocity;
        }

        var seen = new HashSet<ulong>();
        // The authoritative creature runtime enumerates stable IDs. Sort here
        // as well so a different enumerable cannot affect emission order.
        foreach (var creature in creatures.OrderBy(value => value.Id.Value))
        {
            if (!seen.Add(creature.Id.Value))
                throw new ArgumentException("Duplicate creature identity.", nameof(creatures));

            var state = CreatureVisualState.From(creature);
            if (!_emitters.TryGetValue(creature.Id.Value, out var emitter))
            {
                emitter = new Emitter();
                _emitters.Add(creature.Id.Value, emitter);
            }

            var changed = emitter.State != state;
            if (changed)
            {
                emitter.State = state;
                emitter.Timer = 0f;
            }

            var definition = _definitions.Get(creature.DefinitionId);
            if (!definition.ParticleEffects.TryGetValue(state.Phase, out var effect))
                continue;

            if (changed)
            {
                Emit(creature.Position, effect);
                emitter.Timer = Value(effect, "interval");
                continue;
            }

            var interval = Value(effect, "interval");
            if (dt <= 0f || interval <= 0f)
                continue;
            emitter.Timer -= dt;
            if (emitter.Timer <= 0f)
            {
                Emit(creature.Position, effect);
                emitter.Timer += interval;
                if (emitter.Timer <= 0f)
                    emitter.Timer = interval;
            }
        }

        foreach (var id in _emitters.Keys.Where(id => !seen.Contains(id)).ToArray())
            _emitters.Remove(id);
    }

    public void Clear()
    {
        _emitters.Clear();
        _particles.Clear();
        _random = InitialRandomState;
    }

    private void Emit(Vector3 origin, CreatureParticleEffect effect)
    {
        var count = Math.Min(effect.Count, MaximumActive - _particles.Count);
        var props = effect.Properties;
        var radius = Value(effect, "spawnRadius");
        var yOffset = Value(effect, "yOffset");
        var horizontalSpeed = Value(effect, "horizontalSpeed");
        var verticalSpeed = Value(effect, "verticalSpeed");
        var verticalJitter = Value(effect, "verticalJitter");
        var gravity = Value(effect, "gravity");
        var size = Value(effect, "size");
        var lifetime = Value(effect, "lifetime");
        var endScale = props.TryGetValue("endScale", out var scale) ? scale : 0.15f;
        var color = new Vector4(
            effect.Color[0], effect.Color[1], effect.Color[2], effect.Color[3]);

        for (var i = 0; i < count; i++)
        {
            var angle = Unit() * MathF.Tau;
            var distance = radius * MathF.Sqrt(Unit());
            var hx = MathF.Cos(angle);
            var hz = MathF.Sin(angle);
            var particleSize = size * (0.78f + Unit() * 0.44f);
            var particleLifetime = lifetime * (0.88f + Unit() * 0.24f);
            var position = origin + new Vector3(
                hx * distance,
                yOffset + Signed() * radius * 0.15f,
                hz * distance);
            var velocity = new Vector3(
                hx * horizontalSpeed,
                verticalSpeed + Signed() * verticalJitter,
                hz * horizontalSpeed);
            _particles.Add(new CreatureParticleState(
                position, velocity, color, gravity, particleSize,
                particleLifetime, endScale));
        }
    }

    private static float Value(CreatureParticleEffect effect, string key) =>
        effect.Properties.TryGetValue(key, out var value) ? value : 0f;

    private float Unit()
    {
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return (_random >> 8) * (1f / 16777216f);
    }

    private float Signed() => Unit() * 2f - 1f;
}
