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
    CreatureHopMotion Motion = default);

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
                !float.IsFinite(creature.Motion.FacingRadians))
            {
                throw new ArgumentException("Creature snapshot contains an invalid state.", nameof(restore));
            }
            var definition = _definitions.Get(creature.DefinitionId);
            if (!float.IsFinite(creature.Health) ||
                creature.Health <= 0f || creature.Health > definition.Health ||
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

    public bool TrySpawn(string definitionId, Vector3 feet, out CreatureInstanceState creature)
    {
        if (!IsFinite(feet) || feet.Y < 0f)
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
            CreatureHopMotion.Initial);
        _active.Add(id.Value, creature);
        _counts[definition.Id] = CountFor(definition.Id) + 1;
        _nextId = id.Value;
        return true;
    }

    /// <summary>Only known active creatures can receive damage; death retires the entity.</summary>
    public bool TryDamage(CreatureInstanceId id, float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (!_active.TryGetValue(id.Value, out var creature)) return false;

        var health = Math.Max(0f, creature.Health - amount);
        if (health == 0f)
        {
            Remove(id);
        }
        else
        {
            _active[id.Value] = creature with { Health = health };
        }

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
            if (age >= DespawnGraceSeconds &&
                Vector3.DistanceSquared(creature.Position, playerPosition) >
                    DespawnRadius * DespawnRadius)
            {
                Remove(creature.Id);
                removed++;
            }
            else
            {
                _active[id] = creature with { AgeSeconds = age };
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

        var changed = Advance(deltaSeconds, observerPosition) > 0;
        foreach (var id in _active.Keys.ToArray())
        {
            var creature = _active[id];
            var definition = _definitions.Get(creature.DefinitionId);
            var next = CreatureHopSolver.Step(
                creature, definition, world, blocks,
                gravityStrength, (float)Math.Min(deltaSeconds, 0.05));

            if (next.Position != creature.Position ||
                next.Motion.Phase != creature.Motion.Phase ||
                next.Motion.FacingRadians != creature.Motion.FacingRadians)
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
