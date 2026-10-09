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

    private readonly PackContentRegistry<CreatureDefinition> _definitions;
    private readonly SortedDictionary<ulong, CreatureInstanceState> _active = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
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
                !float.IsFinite(creature.Motion.KnockbackSeconds) ||
                creature.Motion.KnockbackSeconds < 0f ||
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
            CreatureHopMotion.Initial) { MetaTags = tags };
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
    /// Picks one live hostile creature overlapping the player's physical
    /// capsule AABB. Stable creature-id order is the deterministic tie break.
    /// This query never mutates combat state or sends damage by itself.
    /// </summary>
    public float ContactDamageAt(WorldAabb playerBounds)
    {
        foreach (var creature in _active.Values)
        {
            if (creature.IsDying || creature.NoAi)
                continue;
            var definition = _definitions.Get(creature.DefinitionId);
            if (definition.ContactDamage <= 0f)
                continue;

            var center = creature.Position + new Vector3(
                definition.Collider.CenterOffset.X,
                definition.Collider.CenterOffset.Y,
                definition.Collider.CenterOffset.Z);
            var half = new Vector3(
                definition.Collider.Size.X,
                definition.Collider.Size.Y,
                definition.Collider.Size.Z) * 0.5f;
            var min = center - half;
            var max = center + half;

            if (min.X < playerBounds.Maximum.X &&
                max.X > playerBounds.Minimum.X &&
                min.Y < playerBounds.Maximum.Y &&
                max.Y > playerBounds.Minimum.Y &&
                min.Z < playerBounds.Maximum.Z &&
                max.Z > playerBounds.Minimum.Z)
                return definition.ContactDamage;
        }

        return 0f;
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
            !float.IsFinite(attack.Damage) || attack.Damage < 0f)
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
                knockback.LengthSquared() <= float.Epsilon ||
                !EffectApplies(id.Value, serial, (uint)index, effect.Chance))
            {
                continue;
            }

            motion = motion with
            {
                KnockbackVelocity = Vector2.Normalize(knockback) *
                    (effect.Strength * 24f),
                KnockbackSeconds = effect.Strength == 0f ? 0f : 0.28f,
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
        float gravityStrength)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0 ||
            !float.IsFinite(gravityStrength) || gravityStrength < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        var hadEndingReactions = _active.Values.Any(
            creature => !creature.IsDying &&
                creature.HurtSecondsRemaining > 0f &&
                creature.HurtSecondsRemaining <= deltaSeconds);
        var changed = Advance(deltaSeconds, observerPosition) > 0 ||
            hadEndingReactions;
        foreach (var id in _active.Keys.ToArray())
        {
            var creature = _active[id];
            if (creature.IsDying || creature.NoAi) continue;
            var definition = _definitions.Get(creature.DefinitionId);
            var next = CreatureHopSolver.Step(
                creature, definition, world, blocks,
                gravityStrength, (float)Math.Min(deltaSeconds, 0.05));

            if (next.Position != creature.Position ||
                next.Motion.Phase != creature.Motion.Phase ||
                next.Motion.FacingRadians != creature.Motion.FacingRadians ||
                (creature.HurtSecondsRemaining > 0f &&
                 next.HurtSecondsRemaining == 0f))
            {
                changed = true;
            }

            _active[id] = next;
        }

        return changed;
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
