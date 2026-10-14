using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

public readonly record struct CreatureInstanceId(ulong Value);

public readonly record struct CreatureInstanceState(
    CreatureInstanceId Id,
    string DefinitionId,
    Vector3 Position,
    float Health,
    double AgeSeconds,
    CreatureHopMotion Motion = default)
{
    public uint AttacksReceived { get; init; }
    public uint AttacksDealt { get; init; }
    public float AttackSecondsRemaining { get; init; }

    /// <summary>Next allowed ranged shot in this creature's logical age.</summary>
    public double NextRangedAttackAtSeconds { get; init; }

    public CreatureMetaTags MetaTags { get; init; }
    public bool NoAi => MetaTags.NoAi;
    public bool Persistent => MetaTags.Persistent;

    /// <summary>Combat feedback is owned by the creature lifecycle, not Godot.</summary>
    public float HurtSecondsRemaining { get; init; }

    public float DeathSecondsRemaining { get; init; }

    public bool IsDying => Health == 0f;
}

public readonly record struct CreatureAttackResult(
    CreatureInstanceId Id,
    string DefinitionId,
    Vector3 Position,
    float Health,
    float MaximumHealth,
    bool Killed,
    IReadOnlyList<CreatureLootEntry> Loot);

/// <summary>One landing-impact hit with the attacker position at impact.
 /// Consumers must validate the damage through the authoritative player
 /// hazards before applying any physical reaction.</summary>
public readonly record struct CreatureMeleeHit(
    CreatureInstanceId Id, float Damage, Vector3 AttackerPosition);

public sealed record CreatureRuntimeSnapshot(
    ulong NextId,
    IReadOnlyList<CreatureInstanceState> Creatures);

/// <summary>
/// Authoritative, dimension-isolated population. Natural spawning is deliberately
/// separate: MineClone's rebuild branch does not yet implement it either.
/// </summary>
public sealed class CreatureRuntime
{
    public const int MaximumActive = 128;
    public const float DespawnRadius = 128f;
    public const double DespawnGraceSeconds = 5.0;
    public const float DeathAnimationSeconds = 0.75f;
    public const float HurtAnimationSeconds = 0.4f;
    public const float AttackAnimationSeconds = 0.34f;
    // The hop solver moves in bounded 0.08-block collision steps. Limit
    // stacked knockback to a safe speed without losing MineClone's additive
    // consecutive-hit semantics.
    public const float MaximumKnockbackSpeed =
        AttackDefinition.MaximumKnockbackStrength * 24f;
    // Reach beyond the solid body; physical colliders should not need
    // to overlap for a deliberate melee strike to connect.
    private const float MeleeReach = 0.3f;
    private const float SeparationEpsilon = 0.001f;

    private readonly PackContentRegistry<CreatureDefinition> _definitions;
    private readonly SortedDictionary<ulong, CreatureInstanceState> _active = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly List<CreatureMeleeHit> _meleeHits = [];
    private ulong _nextId;

    public CreatureRuntime(
        PackContentRegistry<CreatureDefinition> definitions,
        CreatureRuntimeSnapshot? restore = null)
    {
        _definitions = definitions ??
            throw new ArgumentNullException(nameof(definitions));

        if (restore is null) return;

        if (restore.Creatures.Count > MaximumActive)
        {
            throw new ArgumentException("Creature snapshot exceeds population capacity.", nameof(restore));
        }

        foreach (var creature in restore.Creatures.OrderBy(entry => entry.Id.Value))
        {
            if (creature.Id.Value == 0 || creature.Id.Value > restore.NextId ||
                !IsFinite(creature.Position) || creature.Position.Y < 0 ||
                !double.IsFinite(creature.AgeSeconds) || creature.AgeSeconds < 0 ||
                !Enum.IsDefined(creature.Motion.Phase) ||
                !float.IsFinite(creature.Motion.SecondsRemaining) ||
                !float.IsFinite(creature.Motion.VerticalSpeed) ||
                !float.IsFinite(creature.Motion.Direction.X) ||
                !float.IsFinite(creature.Motion.Direction.Y) ||
                !float.IsFinite(creature.Motion.FacingRadians) ||
                !float.IsFinite(creature.Motion.KnockbackVelocity.X) ||
                !float.IsFinite(creature.Motion.KnockbackVelocity.Y) ||
                creature.Motion.KnockbackVelocity.LengthSquared() >
                    MaximumKnockbackSpeed * MaximumKnockbackSpeed + 0.01f ||
                !float.IsFinite(creature.Motion.KnockbackSeconds) ||
                creature.Motion.KnockbackSeconds < 0f ||
                !double.IsFinite(creature.NextRangedAttackAtSeconds) ||
                creature.NextRangedAttackAtSeconds < 0d ||
                !float.IsFinite(creature.HurtSecondsRemaining) ||
                creature.HurtSecondsRemaining < 0f ||
                !float.IsFinite(creature.DeathSecondsRemaining) ||
                creature.DeathSecondsRemaining < 0f ||
                !creature.MetaTags.IsValid ||
                (creature.Health == 0f && creature.DeathSecondsRemaining <= 0f) ||
                (creature.Health > 0f && creature.DeathSecondsRemaining != 0f))
            {
                throw new ArgumentException("Creature snapshot contains an invalid state.", nameof(restore));
            }
            var definition = _definitions.Get(creature.DefinitionId);
            if (!float.IsFinite(creature.Health) ||
                creature.Health < 0f || creature.Health > definition.Health ||
                !CanAdd(definition) || !_active.TryAdd(creature.Id.Value, creature))
            {
                throw new ArgumentException("Creature snapshot exceeds population limits or contains duplicate IDs.", nameof(restore));
            }
            _counts[creature.DefinitionId] = CountFor(creature.DefinitionId) + 1;
        }

        _nextId = restore.NextId;
    }

    public int Count => _active.Count;

    public IEnumerable<CreatureInstanceState> ActiveCreatures => _active.Values;

    public int CountType(string definitionId) => CountFor(definitionId);

    public CreatureRuntimeSnapshot CaptureState() =>
        new(_nextId, _active.Values.ToArray());

    public bool TrySpawn(string definitionId, Vector3 feet, out CreatureInstanceState creature, bool noAi = false)
    {
        var tags = default(CreatureMetaTags);
        if (noAi)
            _ = tags.TryChange(CreatureMetaTagAction.Add,
                CreatureMetaTags.NoAiTag, null, out tags, out _);
        return TrySpawnWithTags(definitionId, feet, tags, out creature);
    }

    public bool TrySpawnWithTags(
        string definitionId, Vector3 feet, CreatureMetaTags tags,
        out CreatureInstanceState creature)
    {
        if (!IsFinite(feet) || feet.Y < 0f || !tags.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(feet));
        }

        if (!_definitions.TryGet(definitionId, out var definition) ||
            definition is null || !CanAdd(definition))
        {
            creature = default;
            return false;
        }

        var id = new CreatureInstanceId(checked(_nextId + 1));
        creature = new CreatureInstanceState(
            id, definition.Id, feet, definition.Health, 0.0,
            CreatureHopMotion.Initial)
        {
            MetaTags = tags,
            NextRangedAttackAtSeconds = definition.RangedAttack?.CooldownSeconds ?? 0d,
        };
        _active.Add(id.Value, creature);
        _counts[definition.Id] = CountFor(definition.Id) + 1;
        _nextId = id.Value;
        return true;
    }

    public CreatureTargetHit? FindTarget(
        VoxelWorld world,
        BlockRegistry blocks,
        Vector3 origin,
        Vector3 direction,
        float maximumDistance) =>
        CreatureTargetQuery.Find(
            _active.Values,
            _definitions,
            world,
            blocks,
            origin,
            direction,
            maximumDistance);

    /// <summary>
    /// Consume actual melee impacts produced by a slime's landing attack.
    /// Merely touching a creature never generates damage.
    /// </summary>
    public IReadOnlyList<CreatureMeleeHit> DrainMeleeHits()
    {
        var hits = _meleeHits.ToArray();
        _meleeHits.Clear();
        return hits;
    }

    private static bool MeleeStrikeHitsPlayer(
        CreatureInstanceState creature,
        CreatureDefinition definition,
        WorldAabb playerBounds,
        VoxelWorld world,
        BlockRegistry blocks)
    {
        var body = CreatureHopSolver.Bounds(definition.Collider, creature.Position);
        var reach = new Vector3(MeleeReach, 0.1f, MeleeReach);
        if (!new WorldAabb(body.Minimum - reach,
                body.Maximum + reach).Intersects(playerBounds))
            return false;

        // A thin solid wall between the player and slime must block the hit.
        var origin = (body.Minimum + body.Maximum) * 0.5f;
        var target = (playerBounds.Minimum + playerBounds.Maximum) * 0.5f;
        var delta = target - origin;
        var distance = delta.Length();
        return distance <= 0.0001f ||
            VoxelWorldRaycaster.Raycast(world, blocks, origin,
                delta / distance, distance) is null;
    }

    /// <summary>
    /// Attempts authored ranged attacks for living, aggressive creatures.
    /// Cooldowns follow the creature's logical age and survive snapshots;
    /// successful projectile launches alone commit the attack animation.
    /// </summary>
    public int FireRangedAttacks(
        ProjectileRuntime projectiles,
        VoxelWorld world,
        BlockRegistry blocks,
        WorldAabb playerBounds,
        bool targetPlayer)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!targetPlayer || _active.Count == 0)
            return 0;

        var playerCenter = (playerBounds.Minimum + playerBounds.Maximum) * 0.5f;
        var fired = 0;
        foreach (var id in _active.Keys.ToArray())
        {
            var creature = _active[id];
            if (creature.IsDying || creature.NoAi ||
                creature.AgeSeconds < creature.NextRangedAttackAtSeconds)
                continue;

            var definition = _definitions.Get(creature.DefinitionId);
            var attack = definition.RangedAttack;
            if (attack is null || definition.AggroRange <= attack.MinimumDistance)
                continue;

            var difference = new Vector2(
                playerCenter.X - creature.Position.X,
                playerCenter.Z - creature.Position.Z);
            var horizontalDistanceSquared = difference.LengthSquared();
            if (horizontalDistanceSquared <= attack.MinimumDistance * attack.MinimumDistance ||
                horizontalDistanceSquared > definition.AggroRange * definition.AggroRange)
                continue;

            var center = creature.Position + new Vector3(
                definition.Collider.CenterOffset.X,
                definition.Collider.CenterOffset.Y,
                definition.Collider.CenterOffset.Z);
            if (MathF.Abs(playerCenter.Y - center.Y) > 3f)
                continue;

            var toPlayer = playerCenter - center;
            if (toPlayer.LengthSquared() <= float.Epsilon)
                continue;
            var direction = Vector3.Normalize(toPlayer);
            // Launch outside the slime's physical hitbox (including large variants).
            var size = definition.Collider.Size;
            var shell = MathF.Max(size.X, MathF.Max(size.Y, size.Z)) * 0.5f;
            var origin = center + direction * (shell + attack.Radius + 0.05f);
            var radius = new Vector3(attack.Radius);
            if (origin.Y < attack.Radius ||
                !VoxelWorldCollision.IsClear(world, blocks,
                    new WorldAabb(origin - radius, origin + radius)))
                continue;

            var flight = playerCenter - origin;
            var distance = flight.Length();
            if (distance <= 0f ||
                VoxelWorldRaycaster.Raycast(world, blocks,
                    origin, direction, distance) is not null)
                continue;

            var launch = new ProjectileLaunch(
                attack.Projectile, origin, direction * attack.Speed,
                attack.Radius, attack.Damage, attack.GravityScale,
                attack.LifetimeSeconds, ProjectileOwner.Creature, creature.Id);
            if (!projectiles.TryLaunch(launch, out _))
                continue;

            _active[id] = creature with
            {
                NextRangedAttackAtSeconds = creature.AgeSeconds + attack.CooldownSeconds,
                AttacksDealt = checked(creature.AttacksDealt + 1),
                AttackSecondsRemaining = AttackAnimationSeconds,
            };
            fired++;
        }

        return fired;
    }

    /// <summary>
    /// Authoritative attack application. Every accepted hit increments an
    /// instance-local serial to make probabilistic effects deterministic
    /// even across dimension retirement and restoration.
    /// </summary>
    public bool TryAttack(
        CreatureInstanceId id,
        AttackDefinition attack,
        Vector3 attacker,
        out CreatureAttackResult result)
    {
        ArgumentNullException.ThrowIfNull(attack);
        if (!IsFinite(attacker) ||
            !float.IsFinite(attack.Damage) || attack.Damage < 0f ||
            attack.Effects is null ||
            attack.Effects.Any(effect => effect is null ||
                effect.Effect != "knockback" ||
                !float.IsFinite(effect.Chance) ||
                effect.Chance < 0f || effect.Chance > 1f ||
                !float.IsFinite(effect.Strength) ||
                effect.Strength < 0f ||
                effect.Strength > AttackDefinition.MaximumKnockbackStrength))
        {
            throw new ArgumentOutOfRangeException(nameof(attack));
        }

        if (!_active.TryGetValue(id.Value, out var current) ||
            current.IsDying || current.NoAi)
        {
            result = default;
            return false;
        }

        var definition = _definitions.Get(current.DefinitionId);
        var health = MathF.Max(0f, current.Health - attack.Damage);
        var killed = health == 0f;
        result = new CreatureAttackResult(
            id, current.DefinitionId, current.Position,
            health, definition.Health, killed,
            killed
                ? CreatureLootTable.Roll(definition.LootTable, id.Value)
                : Array.Empty<CreatureLootEntry>());

        if (killed)
        {
            SetDying(current);
            return true;
        }

        var motion = current.Motion;
        var knockback = new Vector2(
            current.Position.X - attacker.X,
            current.Position.Z - attacker.Z);
        var serial = checked(current.AttacksReceived + 1);

        for (var index = 0; index < attack.Effects.Count; index++)
        {
            var effect = attack.Effects[index];
            if (effect.Effect != "knockback" ||
                effect.Strength == 0f ||
                knockback.LengthSquared() <= float.Epsilon ||
                !EffectApplies(id.Value, serial, (uint)index, effect.Chance))
            {
                continue;
            }

            // MineClone adds successive knockback effects. Preserve both
            // independent strikes and multiple authored effects in one hit.
            // Capping the resulting speed bounds collision-step work in the
            // engine-agnostic hop solver.
            var impulse = Vector2.Normalize(knockback) *
                (effect.Strength * 24f);
            var velocity = motion.KnockbackVelocity + impulse;
            if (velocity.LengthSquared() >
                MaximumKnockbackSpeed * MaximumKnockbackSpeed)
                velocity = Vector2.Normalize(velocity) * MaximumKnockbackSpeed;
            motion = motion with
            {
                KnockbackVelocity = velocity,
                KnockbackSeconds = velocity.LengthSquared() > 0f ? 0.28f : 0f,
            };
        }

        _active[id.Value] = current with
        {
            Health = health,
            Motion = motion,
            AttacksReceived = serial,
            HurtSecondsRemaining = HurtAnimationSeconds,
        };
        return true;
    }

    /// <summary>Kill a targeted living creature through the same death
    /// transition and loot-table semantics as lethal combat.</summary>
    public bool TryKill(
        CreatureInstanceId id,
        out CreatureAttackResult result)
    {
        if (!_active.TryGetValue(id.Value, out var current) || current.IsDying)
        {
            result = default;
            return false;
        }

        var definition = _definitions.Get(current.DefinitionId);
        result = new CreatureAttackResult(
            id, current.DefinitionId, current.Position,
            0f, definition.Health, true,
            CreatureLootTable.Roll(definition.LootTable, id.Value));
        SetDying(current);
        return true;
    }

    private void SetDying(CreatureInstanceState current)
    {
        _active[current.Id.Value] = current with
        {
            Health = 0f,
            DeathSecondsRemaining = DeathAnimationSeconds,
            HurtSecondsRemaining = 0f,
            AttackSecondsRemaining = 0f,
            AttacksReceived = checked(current.AttacksReceived + 1),
            Motion = current.Motion with
            {
                VerticalSpeed = 0f,
                Direction = Vector2.Zero,
                KnockbackVelocity = Vector2.Zero,
                KnockbackSeconds = 0f,
            },
        };
    }

    /// <summary>Only the active Sphere's creature owner mutates NO_AI.</summary>
    public bool TrySetNoAi(CreatureInstanceId id, bool enabled)
    {
        if (!_active.TryGetValue(id.Value, out var creature) || creature.IsDying)
            return false;
        if (creature.NoAi == enabled) return true;
        var action = enabled ? CreatureMetaTagAction.Add :
            CreatureMetaTagAction.Remove;
        if (!creature.MetaTags.TryChange(action, CreatureMetaTags.NoAiTag,
                null, out var tags, out _))
            return false;
        _active[id.Value] = creature with
        {
            MetaTags = tags,
            AttackSecondsRemaining = enabled ? 0f : creature.AttackSecondsRemaining,
            Motion = enabled ? CreatureHopMotion.Initial : creature.Motion,
        };
        return true;
    }

    public bool TryChangeMetaTag(
        CreatureInstanceId id,
        CreatureMetaTagAction action, string tag, string? value,
        out CreatureMetaTagError error)
    {
        error = CreatureMetaTagError.NotSet;
        if (!_active.TryGetValue(id.Value, out var creature) ||
            creature.IsDying)
            return false;
        if (!creature.MetaTags.TryChange(
                action, tag, value, out var updated, out error))
            return false;

        _active[id.Value] = creature with
        {
            MetaTags = updated,
            AttackSecondsRemaining = updated.NoAi ? 0f : creature.AttackSecondsRemaining,
            Motion = updated.NoAi && !creature.NoAi
                ? CreatureHopMotion.Initial : creature.Motion,
        };
        return true;
    }

    private static bool EffectApplies(
        ulong id,
        uint hit,
        uint effectIndex,
        float chance)
    {
        if (chance >= 1f) return true;
        if (chance <= 0f) return false;

        var value = (uint)(id ^ (id >> 32)) ^
            (hit * 0x9e3779b9u) ^ (effectIndex * 0x85ebca6bu);
        value ^= value >> 16;
        value *= 0x7feb352du;
        value ^= value >> 15;
        value *= 0x846ca68bu;
        value ^= value >> 16;
        return value / (double)uint.MaxValue < chance;
    }

    /// <summary>Only known active creatures can receive damage; death retires the entity.</summary>
    public bool TryDamage(CreatureInstanceId id, float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (!_active.TryGetValue(id.Value, out var creature) ||
            creature.IsDying || creature.NoAi) return false;

        var health = Math.Max(0f, creature.Health - amount);
        _active[id.Value] = creature with
        {
            Health = health,
            HurtSecondsRemaining = health == 0f
                ? 0f : HurtAnimationSeconds,
            DeathSecondsRemaining = health == 0f
                ? DeathAnimationSeconds : 0f,
            AttackSecondsRemaining = health == 0f
                ? 0f : creature.AttackSecondsRemaining,
            AttacksReceived = checked(creature.AttacksReceived + 1),
        };

        return true;
    }

    /// <summary>
    /// Advances grace timers and removes distant entities. Stable ID ordering
    /// prevents collection order from affecting which entities survive.
    /// </summary>
    public int Advance(double deltaSeconds, Vector3 playerPosition)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0 ||
            !IsFinite(playerPosition))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        var removed = 0;
        foreach (var id in _active.Keys.ToArray())
        {
            var creature = _active[id];
            var age = creature.AgeSeconds + deltaSeconds;
            var hurt = MathF.Max(0f,
                creature.HurtSecondsRemaining - (float)deltaSeconds);
            var attack = MathF.Max(0f,
                creature.AttackSecondsRemaining - (float)deltaSeconds);
            var death = MathF.Max(0f,
                creature.DeathSecondsRemaining - (float)deltaSeconds);

            if (creature.IsDying)
            {
                if (death == 0f)
                {
                    Remove(creature.Id);
                    removed++;
                }
                else
                {
                    _active[id] = creature with
                    {
                        AgeSeconds = age,
                        DeathSecondsRemaining = death,
                    };
                }
                continue;
            }

            if (!creature.Persistent && age >= DespawnGraceSeconds &&
                Vector3.DistanceSquared(creature.Position, playerPosition) >
                    DespawnRadius * DespawnRadius)
            {
                Remove(creature.Id);
                removed++;
            }
            else
            {
                _active[id] = creature with
                {
                    AgeSeconds = age,
                    HurtSecondsRemaining = hurt,
                    AttackSecondsRemaining = attack,
                };
            }
        }

        return removed;
    }

    /// <summary>
    /// Moves creatures only through resident voxel collision, preserving
    /// their hop phase and heading in the same Sphere-owned snapshot.
    /// Returns true only when presentation must change.
    /// </summary>
    public bool AdvanceWorld(
        double deltaSeconds,
        Vector3 observerPosition,
        VoxelWorld world,
        BlockRegistry blocks,
        float gravityStrength,
        bool targetPlayer = true,
        WorldAabb? playerBounds = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0 ||
            !float.IsFinite(gravityStrength) || gravityStrength < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        // Events are frame-local. Never replay old hits or accumulate a queue
        // when no player is present.
        _meleeHits.Clear();
        var hadEndingReactions = _active.Values.Any(
            creature => !creature.IsDying &&
                creature.HurtSecondsRemaining > 0f &&
                creature.HurtSecondsRemaining <= deltaSeconds ||
                !creature.IsDying &&
                creature.AttackSecondsRemaining > 0f &&
                creature.AttackSecondsRemaining <= deltaSeconds);
        var changed = Advance(deltaSeconds, observerPosition) > 0 ||
            hadEndingReactions;
        changed |= ResolveOverlaps(world, blocks);
        foreach (var id in _active.Keys.ToArray())
        {
            var creature = _active[id];
            if (creature.IsDying || creature.NoAi) continue;
            var definition = _definitions.Get(creature.DefinitionId);
            var next = CreatureHopSolver.Step(
                creature, definition, world, blocks,
                gravityStrength, (float)Math.Min(deltaSeconds, 0.05),
                targetPlayer && definition.ContactDamage > 0f
                    ? observerPosition : null,
                candidate => OverlapsCreature(id, candidate));

            if (next.Position != creature.Position ||
                next.Motion.Phase != creature.Motion.Phase ||
                next.Motion.FacingRadians != creature.Motion.FacingRadians ||
                (creature.HurtSecondsRemaining > 0f &&
                 next.HurtSecondsRemaining == 0f))
            {
                changed = true;
            }

            // A melee hit is emitted only at the landing impact of a hop,
            // never from touching or remaining adjacent to the slime.
            if (targetPlayer &&
                playerBounds is { } bounds &&
                creature.Motion.Phase == CreatureHopPhase.Airborne &&
                next.Motion.Phase == CreatureHopPhase.Land &&
                definition.ContactDamage > 0f &&
                MeleeStrikeHitsPlayer(next, definition, bounds, world, blocks))
            {
                _meleeHits.Add(new CreatureMeleeHit(
                    next.Id, definition.ContactDamage, next.Position));
                next = next with
                {
                    AttacksDealt = checked(next.AttacksDealt + 1),
                    AttackSecondsRemaining = AttackAnimationSeconds,
                };
                changed = true;
            }

            _active[id] = next;
        }

        changed |= ResolveOverlaps(world, blocks);
        return changed;
    }

    private WorldAabb CreatureBounds(CreatureInstanceState creature) =>
        CreatureHopSolver.Bounds(
            _definitions.Get(creature.DefinitionId).Collider, creature.Position);

    /// <summary>Solid creature bodies are separate from combat target boxes.</summary>
    private bool OverlapsCreature(ulong id, WorldAabb bounds, ulong except = 0)
    {
        foreach (var creature in _active.Values)
        {
            if (creature.IsDying || creature.Id.Value == id ||
                creature.Id.Value == except)
                continue;
            if (CreatureBounds(creature).Intersects(bounds)) return true;
        }
        return false;
    }

    /// <summary>
    /// Separate overlapping summons/restores deterministically. All attempted
    /// displacement is swept against loaded terrain; no-AI creatures stay put.
    /// </summary>
    private bool ResolveOverlaps(VoxelWorld world, BlockRegistry blocks)
    {
        if (_active.Count < 2)
            return false;

        var changed = false;
        var ids = _active.Keys.ToArray();
        // Build each collider once instead of repeatedly resolving its
        // definition for every O(n²) pair. Update the cache immediately after
        // separation so subsequent pairs still see authoritative positions.
        var bounds = new WorldAabb[ids.Length];
        for (var i = 0; i < ids.Length; i++)
            bounds[i] = CreatureBounds(_active[ids[i]]);

        for (var pass = 0; pass < 4; pass++)
        {
            var movedThisPass = false;
            for (var i = 0; i < ids.Length; i++)
            for (var j = i + 1; j < ids.Length; j++)
            {
                var first = _active[ids[i]];
                var second = _active[ids[j]];
                if (first.IsDying || second.IsDying ||
                    (first.NoAi && second.NoAi))
                    continue;
                if (!bounds[i].Intersects(bounds[j]))
                    continue;

                if (!second.NoAi && TrySeparate(second, first, world, blocks,
                        out var newSecond))
                {
                    _active[second.Id.Value] = newSecond;
                    bounds[j] = CreatureBounds(newSecond);
                    changed = movedThisPass = true;
                }
                else if (!first.NoAi && TrySeparate(first, second, world, blocks,
                             out var newFirst))
                {
                    _active[first.Id.Value] = newFirst;
                    bounds[i] = CreatureBounds(newFirst);
                    changed = movedThisPass = true;
                }
            }
            if (!movedThisPass) break;
        }
        return changed;
    }

    private bool TrySeparate(CreatureInstanceState moving,
        CreatureInstanceState obstacle, VoxelWorld world, BlockRegistry blocks,
        out CreatureInstanceState result)
    {
        var own = CreatureBounds(moving);
        var other = CreatureBounds(obstacle);
        var xOverlap = MathF.Min(own.Maximum.X, other.Maximum.X) -
            MathF.Max(own.Minimum.X, other.Minimum.X);
        var zOverlap = MathF.Min(own.Maximum.Z, other.Maximum.Z) -
            MathF.Max(own.Minimum.Z, other.Minimum.Z);
        var xFirst = xOverlap <= zOverlap;
        var collider = _definitions.Get(moving.DefinitionId).Collider;

        for (var pass = 0; pass < 2; pass++)
        {
            var axis = (pass == 0) == xFirst ? 0 : 2;
            var overlap = axis == 0 ? xOverlap : zOverlap;
            var direction = moving.Position[axis] >= obstacle.Position[axis]
                ? 1f : -1f;
            var total = overlap + SeparationEpsilon;
            var steps = Math.Max(1, (int)MathF.Ceiling(total / 0.08f));
            var position = moving.Position;
            var isClear = true;
            for (var i = 0; i < steps; i++)
            {
                position[axis] += direction * total / steps;
                var bounds = CreatureHopSolver.Bounds(collider, position);
                if (!VoxelWorldCollision.IsClear(world, blocks, bounds) ||
                    OverlapsCreature(moving.Id.Value, bounds, obstacle.Id.Value))
                {
                    isClear = false;
                    break;
                }
            }
            if (!isClear ||
                CreatureHopSolver.Bounds(collider, position).Intersects(other))
                continue;
            result = moving with { Position = position };
            return true;
        }

        result = default;
        return false;
    }

    private bool CanAdd(CreatureDefinition definition) =>
        _active.Count < MaximumActive &&
        CountFor(definition.Id) < definition.MaxPerType;

    private int CountFor(string id) =>
        _counts.TryGetValue(id, out var value) ? value : 0;

    private void Remove(CreatureInstanceId id)
    {
        var creature = _active[id.Value];
        _active.Remove(id.Value);
        var remaining = CountFor(creature.DefinitionId) - 1;
        if (remaining == 0) _counts.Remove(creature.DefinitionId);
        else _counts[creature.DefinitionId] = remaining;
    }

    private static bool IsFinite(Vector3 vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) &&
        float.IsFinite(vector.Z);
}
