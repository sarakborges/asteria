using System.Numerics;

namespace Asteria.Core.World;

/// <summary>Transient presentation-only state. Never mutates chunks or gameplay entities.</summary>
public sealed class AmbientParticleState
{
    internal AmbientParticleState(
        string ruleId, Vector3 position, Vector3 velocity, Vector3 acceleration,
        Vector3 phase, float lifetime, float size, float wander, bool pop)
    {
        RuleId = ruleId;
        Position = position;
        Velocity = velocity;
        Acceleration = acceleration;
        Phase = phase;
        Lifetime = lifetime;
        BaseSize = size;
        WanderStrength = wander;
        PopAtEnd = pop;
    }

    public string RuleId { get; }
    public Vector3 Position { get; internal set; }
    internal Vector3 Velocity { get; set; }
    internal Vector3 Acceleration { get; }
    internal Vector3 Phase { get; }
    internal float Lifetime { get; }
    internal float Age { get; set; }
    internal float BaseSize { get; }
    internal float WanderStrength { get; }
    internal bool PopAtEnd { get; }

    public float Scale
    {
        get
        {
            var progress = Age / Lifetime;
            if (PopAtEnd && progress > 0.72f)
            {
                var pop = Math.Clamp((progress - 0.72f) / 0.28f, 0f, 1f);
                var burst = MathF.Sin(pop * MathF.PI);
                var collapse = pop > 0.78f
                    ? Math.Clamp((1f - pop) / 0.22f, 0f, 1f) : 1f;
                return BaseSize * MathF.Max(0.03f, (1f + burst * 1.7f) * collapse);
            }

            var fade = progress < 0.12f
                ? progress / 0.12f
                : progress > 0.82f
                    ? (1f - progress) / 0.18f
                    : 1f;
            return BaseSize * MathF.Max(0.03f, Math.Clamp(fade, 0f, 1f));
        }
    }
}

public sealed class AmbientParticleRuntime
{
    public const int MaximumActive = 512;
    private const float MaximumDistanceSquared = 64f * 64f;
    private const int AmbientAttempts = 4;
    private const int FluidAttempts = 10;
    private readonly AmbientParticleRegistry _registry;
    private readonly Random _random;
    private readonly float[] _remainders;
    private readonly List<AmbientParticleState> _particles = new(MaximumActive);

    public AmbientParticleRuntime(AmbientParticleRegistry registry, ulong seed)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _random = new Random(unchecked((int)(seed ^ (seed >> 32))));
        _remainders = new float[registry.Definitions.Count];
    }

    public IReadOnlyList<AmbientParticleState> Particles => _particles;

    public void Clear()
    {
        _particles.Clear();
        Array.Clear(_remainders);
    }

    public void Advance(
        float elapsed, Vector3 observer, DimensionId dimensionId,
        DimensionWindDefinition wind, VoxelWorld world,
        BiomeWorldGenerator generator, FluidRegistry fluids)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0f)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        var delta = Math.Min(elapsed, 0.1f);
        // Backwards iteration preserves stable removal and bounds work to the active budget.
        for (var i = _particles.Count - 1; i >= 0; --i)
        {
            var particle = _particles[i];
            particle.Age += delta;
            if (particle.Age >= particle.Lifetime ||
                Vector3.DistanceSquared(particle.Position, observer) > MaximumDistanceSquared)
            {
                _particles.RemoveAt(i);
                continue;
            }

            var age = particle.Age;
            var wander = new Vector3(
                MathF.Sin(particle.Phase.X + age * 1.7f),
                MathF.Sin(particle.Phase.Y + age * 1.3f),
                MathF.Sin(particle.Phase.Z + age * 1.9f)) * particle.WanderStrength;
            particle.Velocity += (particle.Acceleration + wander) * delta;
            particle.Position += particle.Velocity * delta;
            if (!IsEmptyBlock(world, particle.Position))
                _particles.RemoveAt(i);
        }

        if (delta <= 0f || _particles.Count >= MaximumActive ||
            !IsLoaded(world, observer))
            return;

        // Actual 3D biome identity is authoritative: volume biomes do not bleed onto
        // a player on the surface, and underground biomes remain independent.
        var activeBiome = generator.EffectiveBiomeAt(
            Floor(observer.X), Math.Max(0, Floor(observer.Y)), Floor(observer.Z));
        for (var ruleIndex = 0; ruleIndex < _registry.Definitions.Count; ruleIndex++)
        {
            var rule = _registry.Definitions[ruleIndex];
            var active = rule.SourceKind switch
            {
                AmbientParticleSourceKind.Dimension => rule.SourceId == dimensionId.Value,
                AmbientParticleSourceKind.Biome => rule.SourceId == activeBiome,
                AmbientParticleSourceKind.FluidSurface => true,
                _ => false,
            };
            if (!active)
            {
                _remainders[ruleIndex] = 0f;
                continue;
            }

            _remainders[ruleIndex] += rule.SpawnRate * delta;
            var requested = (int)MathF.Floor(_remainders[ruleIndex]);
            _remainders[ruleIndex] -= requested;
            var emitted = Math.Min(requested, MaximumActive - _particles.Count);
            for (var j = 0; j < emitted; j++)
            {
                Vector3? candidate = rule.SourceKind == AmbientParticleSourceKind.FluidSurface
                    ? FindFluidSurface(observer, rule, world, fluids.GetId(rule.SourceId))
                    : FindAmbient(observer, rule, world, generator);

                if (candidate is not { } position)
                    continue;

                var velocity = rule.Velocity + new Vector3(
                    Signed() * rule.VelocityJitter.X,
                    Signed() * rule.VelocityJitter.Y,
                    Signed() * rule.VelocityJitter.Z);
                var windVelocity = wind.Velocity * rule.WindInfluence;
                velocity += new Vector3(windVelocity.X, 0f, windVelocity.Y);

                _particles.Add(new AmbientParticleState(
                    rule.Id, position, velocity, rule.Acceleration,
                    new Vector3(Unit() * MathF.Tau, Unit() * MathF.Tau, Unit() * MathF.Tau),
                    Between(rule.Lifetime), Between(rule.Size),
                    rule.WanderStrength, rule.PopAtEnd));
            }
            if (_particles.Count == MaximumActive)
                break;
        }
    }

    private Vector3? FindAmbient(
        Vector3 observer, AmbientParticleDefinition rule,
        VoxelWorld world, BiomeWorldGenerator generator)
    {
        for (var attempt = 0; attempt < AmbientAttempts; attempt++)
        {
            var position = RandomPosition(observer, rule);
            if (!IsOpen(world, position))
                continue;
            if (rule.SourceKind == AmbientParticleSourceKind.Biome &&
                generator.EffectiveBiomeAt(
                    Floor(position.X), Math.Max(0, Floor(position.Y)),
                    Floor(position.Z)) != rule.SourceId)
                continue;
            return position;
        }
        return null;
    }

    private Vector3? FindFluidSurface(
        Vector3 observer, AmbientParticleDefinition rule,
        VoxelWorld world, FluidRuntimeId fluidId)
    {
        var vertical = (int)MathF.Ceiling(rule.VerticalRange);
        for (var attempt = 0; attempt < FluidAttempts; attempt++)
        {
            var point = RandomPosition(observer, rule);
            var x = Floor(point.X);
            var z = Floor(point.Z);
            var from = Math.Max(0, Floor(observer.Y) - vertical);
            var to = Floor(observer.Y) + vertical;
            for (var y = to; y >= from; y--)
            {
                var cell = new WorldVoxelCoord(x, y, z);
                var above = new WorldVoxelCoord(x, y + 1, z);
                if (!world.IsLoadedAt(cell) || !world.IsLoadedAt(above))
                    continue;
                var fluid = world.GetFluidOrEmpty(cell);
                if (fluid.IsEmpty || fluid.Fluid != fluidId ||
                    !world.GetFluidOrEmpty(above).IsEmpty ||
                    !world.GetCellOrEmpty(above).IsEmpty)
                    continue;

                return new Vector3(x + Unit(), y + MathF.Max(0.01f, fluid.Height - 0.015f), z + Unit());
            }
        }
        return null;
    }

    private Vector3 RandomPosition(Vector3 center, AmbientParticleDefinition rule)
    {
        var angle = Unit() * MathF.Tau;
        var radius = MathF.Sqrt(Unit()) * rule.SpawnRadius;
        return center + new Vector3(
            MathF.Cos(angle) * radius,
            Signed() * rule.VerticalRange,
            MathF.Sin(angle) * radius);
    }

    private static bool IsLoaded(VoxelWorld world, Vector3 position) =>
        position.Y >= 0f &&
        world.IsLoadedAt(new WorldVoxelCoord(
            Floor(position.X), Floor(position.Y), Floor(position.Z)));

    private static bool IsEmptyBlock(VoxelWorld world, Vector3 position) =>
        IsLoaded(world, position) &&
        world.GetCellOrEmpty(new WorldVoxelCoord(
            Floor(position.X), Floor(position.Y), Floor(position.Z))).IsEmpty;

    private static bool IsOpen(VoxelWorld world, Vector3 position) =>
        IsEmptyBlock(world, position) &&
        world.GetFluidOrEmpty(new WorldVoxelCoord(
            Floor(position.X), Floor(position.Y), Floor(position.Z))).IsEmpty;

    private static int Floor(float number) => (int)MathF.Floor(number);
    private float Unit() => (float)_random.NextDouble();
    private float Signed() => Unit() * 2f - 1f;
    private float Between(AmbientParticleRange range) =>
        range.Min + (range.Max - range.Min) * Unit();
}
