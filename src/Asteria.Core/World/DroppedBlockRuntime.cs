using System.Numerics;

namespace Asteria.Core.World;

public readonly record struct DroppedBlockId(
    ulong Value);

public readonly record struct DroppedBlockState(
    DroppedBlockId Id,
    BlockStateSnapshot Block,
    Vector3 Position,
    Vector3 Velocity,
    double AgeSeconds,
    bool IsSettled);

public readonly record struct DroppedBlockAdvanceResult(
    int Moved,
    int Settled,
    int Expired);

public sealed class DroppedBlockRuntime
{
    public const float HalfExtent = 0.18f;

    private const double MaximumDeltaSeconds = 0.05;
    private const float MaximumCollisionStep = 0.08f;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly int _maximumActive;
    private readonly double _lifetimeSeconds;
    private readonly SortedDictionary<ulong, DroppedBlockState>
        _active = [];
    private readonly Dictionary<WorldVoxelCoord, HashSet<ulong>>
        _settledBySupport = [];
    private readonly Dictionary<ulong, WorldVoxelCoord>
        _supportById = [];

    private ulong _nextId;

    public DroppedBlockRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        int maximumActive = 2048,
        double lifetimeSeconds = 300.0)
    {
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _blocks =
            blocks ??
            throw new ArgumentNullException(nameof(blocks));

        if (maximumActive <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumActive));
        }

        if (!double.IsFinite(lifetimeSeconds) ||
            lifetimeSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetimeSeconds));
        }

        _maximumActive = maximumActive;
        _lifetimeSeconds = lifetimeSeconds;
    }

    public int ActiveCount => _active.Count;

    public IEnumerable<DroppedBlockState> ActiveBlocks =>
        _active.Values;

    public DroppedBlockId Spawn(
        BlockStateSnapshot block,
        Vector3 position,
        Vector3 velocity = default)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (!IsFinite(position) ||
            !IsFinite(velocity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "Dropped block position and velocity must be finite.");
        }

        if (_active.Count >= _maximumActive)
        {
            Remove(_active.First().Key);
        }

        var id =
            new DroppedBlockId(
                checked(++_nextId));

        _active.Add(
            id.Value,
            new DroppedBlockState(
                id,
                block,
                position,
                velocity,
                AgeSeconds: 0.0,
                IsSettled: false));

        return id;
    }

    public void NotifyVoxelEdit(
        WorldVoxelCoord position)
    {
        if (!_settledBySupport.Remove(
                position,
                out var ids))
        {
            return;
        }

        foreach (var id in ids)
        {
            _supportById.Remove(id);

            if (_active.TryGetValue(
                    id,
                    out var state))
            {
                _active[id] =
                    state with
                    {
                        IsSettled = false,
                    };
            }
        }
    }

    public DroppedBlockAdvanceResult Advance(
        double deltaSeconds,
        double gravityStrength)
    {
        if (!double.IsFinite(deltaSeconds) ||
            deltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds));
        }

        if (!double.IsFinite(gravityStrength) ||
            gravityStrength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gravityStrength));
        }

        if (_active.Count == 0 ||
            deltaSeconds == 0)
        {
            return default;
        }

        var delta =
            Math.Min(
                deltaSeconds,
                MaximumDeltaSeconds);
        var moved = 0;
        var settled = 0;
        var expired = 0;

        foreach (var id in
                 _active.Keys.ToArray())
        {
            var state = _active[id];
            var age =
                state.AgeSeconds +
                deltaSeconds;

            if (age >= _lifetimeSeconds)
            {
                Remove(id);
                expired++;
                continue;
            }

            if (state.IsSettled ||
                gravityStrength == 0)
            {
                _active[id] =
                    state with
                    {
                        AgeSeconds = age,
                    };
                continue;
            }

            var voxel =
                FloorToVoxel(state.Position);

            if (!_world.IsLoadedAt(voxel))
            {
                _active[id] =
                    state with
                    {
                        AgeSeconds = age,
                    };
                continue;
            }

            var position = state.Position;
            var velocity = state.Velocity;
            velocity.Y -=
                (float)(gravityStrength * delta);

            var changed = false;

            changed |=
                MoveAxis(
                    ref position,
                    ref velocity,
                    axis: 0,
                    (float)delta,
                    out _);
            changed |=
                MoveAxis(
                    ref position,
                    ref velocity,
                    axis: 2,
                    (float)delta,
                    out _);
            changed |=
                MoveAxis(
                    ref position,
                    ref velocity,
                    axis: 1,
                    (float)delta,
                    out var downwardSupport);

            var isSettled =
                downwardSupport is not null &&
                velocity.Y == 0f;

            var next =
                state with
                {
                    Position = position,
                    Velocity = velocity,
                    AgeSeconds = age,
                    IsSettled = isSettled,
                };

            _active[id] = next;

            if (changed)
            {
                moved++;
            }

            if (isSettled)
            {
                IndexSettledSupport(
                    next,
                    downwardSupport!.Value);
                settled++;
            }
        }

        return new DroppedBlockAdvanceResult(
            moved,
            settled,
            expired);
    }

    public static WorldAabb BoundsAt(
        Vector3 center) =>
        new(
            center -
            new Vector3(
                HalfExtent,
                HalfExtent,
                HalfExtent),
            center +
            new Vector3(
                HalfExtent,
                HalfExtent,
                HalfExtent));

    private bool MoveAxis(
        ref Vector3 position,
        ref Vector3 velocity,
        int axis,
        float deltaSeconds,
        out WorldVoxelCoord? downwardSupport)
    {
        downwardSupport = null;

        var distance =
            velocity[axis] *
            deltaSeconds;

        if (distance == 0f)
        {
            return false;
        }

        var stepCount =
            Math.Max(
                1,
                (int)MathF.Ceiling(
                    MathF.Abs(distance) /
                    MaximumCollisionStep));
        var step =
            distance /
            stepCount;
        var changed = false;

        for (var index = 0;
             index < stepCount;
             index++)
        {
            var next = position;
            next[axis] += step;

            var collision =
                VoxelWorldCollision.QueryDetailed(
                    _world,
                    _blocks,
                    BoundsAt(next));

            if (!collision.IsClear)
            {
                velocity[axis] = 0f;

                if (collision.State ==
                        VoxelWorldCollisionState.Blocked &&
                    axis == 1 &&
                    step < 0f)
                {
                    downwardSupport =
                        collision.Voxel;
                }

                break;
            }

            position = next;
            changed = true;
        }

        return changed;
    }

    private void IndexSettledSupport(
        DroppedBlockState state,
        WorldVoxelCoord support)
    {
        _supportById[state.Id.Value] =
            support;

        if (!_settledBySupport.TryGetValue(
                support,
                out var ids))
        {
            ids = [];
            _settledBySupport.Add(
                support,
                ids);
        }

        ids.Add(state.Id.Value);
    }

    private void Remove(ulong id)
    {
        if (!_active.Remove(
                id,
                out var state) ||
            !state.IsSettled ||
            !_supportById.Remove(
                id,
                out var support))
        {
            return;
        }

        if (!_settledBySupport.TryGetValue(
                support,
                out var ids))
        {
            return;
        }

        ids.Remove(id);

        if (ids.Count == 0)
        {
            _settledBySupport.Remove(support);
        }
    }

    private static WorldVoxelCoord FloorToVoxel(
        Vector3 position) =>
        new(
            (int)MathF.Floor(position.X),
            (int)MathF.Floor(position.Y),
            (int)MathF.Floor(position.Z));

    private static bool IsFinite(
        Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
