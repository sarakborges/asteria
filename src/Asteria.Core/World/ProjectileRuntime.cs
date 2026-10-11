using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

public readonly record struct ProjectileId(ulong Value);

public enum ProjectileOwner : byte
{
    World,
    Player,
    Creature,
}

public enum ProjectileHitTarget : byte
{
    Block,
    Unloaded,
    Creature,
    Player,
}

/// <summary>
/// Immutable launch parameters. A projectile's position, velocity and lifetime
/// are authoritative Core state; the client never performs hit detection.
/// </summary>
public readonly record struct ProjectileLaunch(
    string Kind,
    Vector3 Position,
    Vector3 Velocity,
    float Radius,
    float Damage,
    float GravityScale,
    float LifetimeSeconds,
    ProjectileOwner Owner,
    CreatureInstanceId SourceCreature = default);

public readonly record struct ProjectileState(
    ProjectileId Id,
    ProjectileLaunch Launch,
    Vector3 Position,
    Vector3 Velocity,
    float AgeSeconds);

public readonly record struct ProjectileImpact(
    ProjectileId Id,
    ProjectileHitTarget Target,
    Vector3 Position,
    float Damage,
    WorldVoxelCoord? Voxel = null,
    CreatureInstanceId? Creature = null);

/// <summary>
/// Dimension-local, bounded projectile simulation. Uses swept collision against
/// the shared voxel geometry and authored creature colliders; an impact removes
/// the projectile exactly once. A missing chunk is a terminating collision.
/// </summary>
public sealed class ProjectileRuntime
{
    public const int MaximumActive = 256;
    public const float MaximumSpeed = 256f;
    public const float MaximumRadius = 0.75f;
    public const float MaximumLifetimeSeconds = 30f;
    private const float MaximumFrameSeconds = 0.1f;
    private const float TieEpsilon = 0.000001f;
    private const float FineSweepStep = 1f / (BlockGeometry.Resolution * 4f);

    private readonly PackContentRegistry<CreatureDefinition> _definitions;
    private readonly SortedDictionary<ulong, ProjectileState> _active = [];
    private ulong _nextId;

    public ProjectileRuntime(PackContentRegistry<CreatureDefinition> definitions)
    {
        _definitions = definitions ??
            throw new ArgumentNullException(nameof(definitions));
    }

    public int Count => _active.Count;
    public IEnumerable<ProjectileState> ActiveProjectiles => _active.Values;

    public bool TryLaunch(ProjectileLaunch launch, out ProjectileState state)
    {
        if (string.IsNullOrWhiteSpace(launch.Kind) || launch.Kind.Length > 128 ||
            !Finite(launch.Position) || !Finite(launch.Velocity) ||
            launch.Position.Y < launch.Radius ||
            !float.IsFinite(launch.Radius) || launch.Radius < 1f / 64f ||
            launch.Radius > MaximumRadius ||
            !float.IsFinite(launch.Damage) || launch.Damage < 0f ||
            launch.Damage > 1000f ||
            !float.IsFinite(launch.GravityScale) ||
            launch.GravityScale < 0f || launch.GravityScale > 4f ||
            !float.IsFinite(launch.LifetimeSeconds) ||
            launch.LifetimeSeconds <= 0f ||
            launch.LifetimeSeconds > MaximumLifetimeSeconds ||
            launch.Velocity.LengthSquared() > MaximumSpeed * MaximumSpeed ||
            !Enum.IsDefined(launch.Owner) ||
            (launch.Owner == ProjectileOwner.Creature) !=
                (launch.SourceCreature.Value != 0) ||
            _active.Count >= MaximumActive || _nextId == ulong.MaxValue)
        {
            state = default;
            return false;
        }

        state = new ProjectileState(
            new ProjectileId(++_nextId), launch,
            launch.Position, launch.Velocity, 0f);
        _active.Add(state.Id.Value, state);
        return true;
    }

    public IReadOnlyList<ProjectileImpact> Advance(
        double elapsedSeconds,
        VoxelWorld world,
        BlockRegistry blocks,
        IEnumerable<CreatureInstanceState> creatures,
        WorldAabb? playerBounds,
        bool playerTargetable,
        float gravityStrength)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(creatures);
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0 ||
            !float.IsFinite(gravityStrength) || gravityStrength < 0f)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

        if (elapsedSeconds == 0 || _active.Count == 0)
            return Array.Empty<ProjectileImpact>();

        // Frame stalls never turn a single simulation step into an unbounded
        // world query. Every live projectile still receives a swept test.
        var elapsed = (float)Math.Min(elapsedSeconds, MaximumFrameSeconds);
        var impacts = new List<ProjectileImpact>();
        var expired = new List<ulong>();
        // Snapshot only stable IDs so replacing state does not invalidate the
        // sorted population enumerator while resolving this frame.
        foreach (var id in _active.Keys.ToArray())
        {
            var state = _active[id];
            var remaining = state.Launch.LifetimeSeconds - state.AgeSeconds;
            if (remaining <= 0f)
            {
                expired.Add(id);
                continue;
            }

            var dt = MathF.Min(elapsed, remaining);
            var velocity = state.Velocity -
                Vector3.UnitY * (gravityStrength * state.Launch.GravityScale * dt);
            var end = state.Position + velocity * dt;
            var hit = Sweep(
                state, end, world, blocks, creatures,
                playerTargetable ? playerBounds : null);
            if (hit is { } contact)
            {
                impacts.Add(new ProjectileImpact(
                    state.Id, contact.Target,
                    Vector3.Lerp(state.Position, end, contact.Time),
                    state.Launch.Damage, contact.Voxel, contact.Creature));
                expired.Add(id);
                continue;
            }

            if (dt >= remaining)
            {
                expired.Add(id);
            }
            else
            {
                _active[id] = state with
                {
                    Position = end,
                    Velocity = velocity,
                    AgeSeconds = state.AgeSeconds + dt,
                };
            }
        }

        foreach (var id in expired) _active.Remove(id);
        return impacts;
    }

    private readonly record struct Contact(
        float Time, ProjectileHitTarget Target,
        WorldVoxelCoord? Voxel = null, CreatureInstanceId? Creature = null);

    private Contact? Sweep(
        ProjectileState projectile,
        Vector3 end,
        VoxelWorld world,
        BlockRegistry blocks,
        IEnumerable<CreatureInstanceState> creatures,
        WorldAabb? playerBounds)
    {
        var start = projectile.Position;
        var radius = projectile.Launch.Radius;
        var expansion = new Vector3(radius);
        Contact? nearest = null;

        // Narrow phase only inspects voxels in the swept AABB, not the entire
        // chunk. The slab test rejects candidates outside the actual path.
        var low = Vector3.Min(start, end) - expansion;
        var high = Vector3.Max(start, end) + expansion;
        for (var y = (int)MathF.Floor(low.Y); y <= (int)MathF.Floor(high.Y); y++)
        for (var z = (int)MathF.Floor(low.Z); z <= (int)MathF.Floor(high.Z); z++)
        for (var x = (int)MathF.Floor(low.X); x <= (int)MathF.Floor(high.X); x++)
        {
            var voxel = new WorldVoxelCoord(x, y, z);
            var min = new Vector3(x, y, z) - expansion;
            var max = new Vector3(x + 1f, y + 1f, z + 1f) + expansion;
            if (!SegmentBox(start, end, min, max, out var enter, out var exit) ||
                nearest is { } earlier && enter > earlier.Time)
                continue;

            ProjectileHitTarget kind;
            if (y < 0)
                kind = ProjectileHitTarget.Block;
            else if (!world.IsLoadedAt(voxel))
                kind = ProjectileHitTarget.Unloaded;
            else
            {
                var cell = world.GetCellOrEmpty(voxel);
                if (cell.IsEmpty) continue;
                var definition = blocks.GetDefinition(cell.Block);
                if (!definition.IsCollidable) continue;
                if (BlockGeometry.RequiresFineMeshing(definition, cell))
                {
                    if (!SweepPartialGeometry(
                        world, voxel, definition, cell,
                        start, end, radius, enter, exit, out enter))
                        continue;
                }
                kind = ProjectileHitTarget.Block;
            }

            var contact = new Contact(enter, kind, voxel);
            if (IsEarlier(contact, nearest)) nearest = contact;
        }

        foreach (var creature in creatures)
        {
            if (creature.IsDying ||
                projectile.Launch.Owner == ProjectileOwner.Creature &&
                creature.Id == projectile.Launch.SourceCreature)
                continue;
            var collider = _definitions.Get(creature.DefinitionId).Collider;
            var center = creature.Position + new Vector3(
                collider.CenterOffset.X,
                collider.CenterOffset.Y,
                collider.CenterOffset.Z);
            var half = new Vector3(
                collider.Size.X, collider.Size.Y, collider.Size.Z) * 0.5f +
                expansion;
            if (!SegmentBox(start, end, center - half, center + half,
                    out var time, out _))
                continue;
            var contact = new Contact(
                time, ProjectileHitTarget.Creature, null, creature.Id);
            if (IsEarlier(contact, nearest)) nearest = contact;
        }

        if (playerBounds is { } bounds &&
            projectile.Launch.Owner != ProjectileOwner.Player &&
            SegmentBox(start, end, bounds.Minimum - expansion,
                bounds.Maximum + expansion, out var playerTime, out _))
        {
            var contact = new Contact(playerTime, ProjectileHitTarget.Player);
            if (IsEarlier(contact, nearest)) nearest = contact;
        }

        return nearest;
    }

    private static bool SweepPartialGeometry(
        VoxelWorld world,
        WorldVoxelCoord voxel,
        BlockDefinition definition,
        VoxelCell cell,
        Vector3 start,
        Vector3 end,
        float radius,
        float enter,
        float exit,
        out float impact)
    {
        var movement = end - start;
        var count = Math.Max(1, (int)MathF.Ceiling(
            movement.Length() * (exit - enter) / FineSweepStep));
        var mask = world.GetMicroblockMaskOrEmpty(voxel);
        var extent = new Vector3(radius);
        var previous = enter;
        for (var step = 0; step <= count; step++)
        {
            var at = enter + (exit - enter) * step / count;
            var position = Vector3.Lerp(start, end, at);
            var bounds = new WorldAabb(position - extent, position + extent);
            if (!BlockGeometry.Intersects(definition, cell, mask, voxel, bounds))
            {
                previous = at;
                continue;
            }

            // Refine the first occupied point to avoid visible overshoot.
            var before = previous;
            var after = at;
            for (var i = 0; i < 8 && before < after; i++)
            {
                var middle = (before + after) * 0.5f;
                var probe = Vector3.Lerp(start, end, middle);
                if (BlockGeometry.Intersects(definition, cell, mask, voxel,
                        new WorldAabb(probe - extent, probe + extent)))
                    after = middle;
                else
                    before = middle;
            }

            impact = after;
            return true;
        }

        impact = 0f;
        return false;
    }

    private static bool IsEarlier(Contact candidate, Contact? best)
    {
        if (best is not { } current) return true;
        if (candidate.Time < current.Time - TieEpsilon) return true;
        if (MathF.Abs(candidate.Time - current.Time) > TieEpsilon) return false;
        if (candidate.Target != current.Target)
            return candidate.Target < current.Target;
        if (candidate.Target == ProjectileHitTarget.Creature)
            return candidate.Creature!.Value.Value < current.Creature!.Value.Value;
        // Voxels are visited in Y, Z, X order; equal-time candidates keep
        // the first stable voxel.
        return false;
    }

    private static bool SegmentBox(
        Vector3 start, Vector3 end,
        Vector3 min, Vector3 max,
        out float entry, out float exit)
    {
        entry = 0f;
        exit = 1f;
        var delta = end - start;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = axis == 0 ? start.X : axis == 1 ? start.Y : start.Z;
            var speed = axis == 0 ? delta.X : axis == 1 ? delta.Y : delta.Z;
            var a = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            var b = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(speed) < 0.0000001f)
            {
                if (origin < a || origin > b) return false;
                continue;
            }

            var near = (a - origin) / speed;
            var far = (b - origin) / speed;
            if (near > far) (near, far) = (far, near);
            entry = MathF.Max(entry, near);
            exit = MathF.Min(exit, far);
            if (entry > exit) return false;
        }

        return true;
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
