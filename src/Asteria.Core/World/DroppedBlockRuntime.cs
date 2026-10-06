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

public sealed record DroppedBlockRuntimeEntry(
    DroppedBlockState State,
    WorldVoxelCoord? SettledSupport);

public sealed record DroppedBlockRuntimeSnapshot(
    ulong NextId,
    IReadOnlyList<DroppedBlockRuntimeEntry> ActiveBlocks);

public readonly record struct DroppedBlockAdvanceResult(
    int Moved,
    int Settled,
    int Expired);

public sealed class DroppedBlockRuntime
{
    public const float HalfExtent = 0.18f;

    private const double MaximumDeltaSeconds = 0.05;
    private const float MaximumCollisionStep = 0.08f;
    private const float ContactEpsilon = 0.0001f;
    private const int MaximumContactPasses = 4;
    private const float ContactGridCellSize =
        HalfExtent * 2f;

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
    private readonly Dictionary<ContactCell, int>
        _contactHeads = [];
    private readonly ulong[] _contactIds;
    private readonly int[] _contactNext;
    private readonly bool[] _contactMoved;

    private ulong _nextId;

    public DroppedBlockRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        int maximumActive = 2048,
        double lifetimeSeconds = 300.0,
        DroppedBlockRuntimeSnapshot? restore = null)
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
        _contactIds = new ulong[maximumActive];
        _contactNext = new int[maximumActive];
        _contactMoved = new bool[maximumActive];

        if (restore is not null)
        {
            if (restore.ActiveBlocks.Count >
                maximumActive)
            {
                throw new ArgumentException(
                    "Restored dropped-block state exceeds active capacity.",
                    nameof(restore));
            }

            _nextId =
                restore.NextId;

            foreach (var entry in
                     restore.ActiveBlocks
                         .OrderBy(
                             value =>
                                 value.State.Id.Value))
            {
                var state =
                    entry.State;

                if (!_active.TryAdd(
                        state.Id.Value,
                        state))
                {
                    throw new ArgumentException(
                        $"Duplicate restored dropped-block id: {state.Id.Value}",
                        nameof(restore));
                }

                if (entry.SettledSupport is
                    { } support)
                {
                    IndexSettledSupport(
                        state,
                        support);
                }
            }
        }
    }

    public int ActiveCount => _active.Count;

    public IEnumerable<DroppedBlockState> ActiveBlocks =>
        _active.Values;

    public DroppedBlockRuntimeSnapshot CaptureState() =>
        new(
            _nextId,
            _active.Values
                .Select(
                    state =>
                        new DroppedBlockRuntimeEntry(
                            state,
                            _supportById.TryGetValue(
                                state.Id.Value,
                                out var support)
                                ? support
                                : null))
                .ToArray());

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

        var activeCount = 0;

        foreach (var id in _active.Keys)
        {
            _contactIds[activeCount++] = id;
        }

        for (var index = 0;
             index < activeCount;
             index++)
        {
            var id = _contactIds[index];
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

        moved += ResolveContacts();

        return new DroppedBlockAdvanceResult(
            moved,
            settled,
            expired);
    }

    private int ResolveContacts()
    {
        if (_active.Count < 2)
        {
            return 0;
        }

        Array.Clear(_contactMoved);
        var movedCount = 0;

        for (var pass = 0;
             pass < MaximumContactPasses;
             pass++)
        {
            var count = RebuildContactBroadphase();
            var resolvedAny = false;

            for (var firstIndex = 0;
                 firstIndex < count;
                 firstIndex++)
            {
                var firstId =
                    _contactIds[firstIndex];

                if (!_active.TryGetValue(
                        firstId,
                        out var first))
                {
                    continue;
                }

                var firstCell =
                    ContactCell.FromPosition(
                        first.Position);

                for (var y = -1; y <= 1; y++)
                {
                    for (var z = -1; z <= 1; z++)
                    {
                        for (var x = -1; x <= 1; x++)
                        {
                            var cell =
                                new ContactCell(
                                    firstCell.X + x,
                                    firstCell.Y + y,
                                    firstCell.Z + z);

                            if (!_contactHeads.TryGetValue(
                                    cell,
                                    out var secondIndex))
                            {
                                continue;
                            }

                            while (secondIndex >= 0)
                            {
                                if (secondIndex > firstIndex)
                                {
                                    resolvedAny |=
                                        ResolveContactPair(
                                            firstIndex,
                                            secondIndex);
                                }

                                secondIndex =
                                    _contactNext[
                                        secondIndex];
                            }
                        }
                    }
                }
            }

            if (!resolvedAny)
            {
                break;
            }
        }

        for (var index = 0;
             index < _active.Count;
             index++)
        {
            if (!_contactMoved[index])
            {
                continue;
            }

            movedCount++;

            var id = _contactIds[index];

            if (_active.TryGetValue(
                    id,
                    out var state) &&
                state.IsSettled)
            {
                RefreshSettledSupport(
                    id,
                    state);
            }
        }

        return movedCount;
    }

    private int RebuildContactBroadphase()
    {
        _contactHeads.Clear();

        var count = 0;

        foreach (var id in _active.Keys)
        {
            _contactIds[count++] = id;
        }

        for (var index = count - 1;
             index >= 0;
             index--)
        {
            var id = _contactIds[index];
            var state = _active[id];
            var cell =
                ContactCell.FromPosition(
                    state.Position);

            _contactNext[index] =
                _contactHeads.TryGetValue(
                    cell,
                    out var head)
                    ? head
                    : -1;
            _contactHeads[cell] = index;
        }

        return count;
    }

    private bool ResolveContactPair(
        int firstIndex,
        int secondIndex)
    {
        var firstId =
            _contactIds[firstIndex];
        var secondId =
            _contactIds[secondIndex];

        if (!_active.TryGetValue(
                firstId,
                out var first) ||
            !_active.TryGetValue(
                secondId,
                out var second))
        {
            return false;
        }

        var firstBounds =
            BoundsAt(first.Position);
        var secondBounds =
            BoundsAt(second.Position);
        var overlapY =
            MathF.Min(
                firstBounds.Maximum.Y,
                secondBounds.Maximum.Y) -
            MathF.Max(
                firstBounds.Minimum.Y,
                secondBounds.Minimum.Y);

        if (overlapY <= 0f)
        {
            return false;
        }

        var overlapX =
            MathF.Min(
                firstBounds.Maximum.X,
                secondBounds.Maximum.X) -
            MathF.Max(
                firstBounds.Minimum.X,
                secondBounds.Minimum.X);
        var overlapZ =
            MathF.Min(
                firstBounds.Maximum.Z,
                secondBounds.Maximum.Z) -
            MathF.Max(
                firstBounds.Minimum.Z,
                secondBounds.Minimum.Z);

        if (overlapX <= 0f ||
            overlapZ <= 0f)
        {
            return false;
        }

        var axis =
            overlapX <= overlapZ
                ? 0
                : 2;
        var penetration =
            (axis == 0
                ? overlapX
                : overlapZ) +
            ContactEpsilon;
        var firstCenter =
            first.Position[axis];
        var secondCenter =
            second.Position[axis];
        var secondDirection =
            secondCenter > firstCenter
                ? 1f
                : secondCenter < firstCenter
                    ? -1f
                    : 1f;
        var firstTarget =
            penetration * 0.5f;
        var secondTarget =
            penetration - firstTarget;

        var firstPosition =
            first.Position;
        var secondPosition =
            second.Position;
        var firstMoved =
            PushContact(
                ref firstPosition,
                axis,
                -secondDirection *
                firstTarget);
        var secondMoved =
            PushContact(
                ref secondPosition,
                axis,
                secondDirection *
                secondTarget);
        var remaining =
            MathF.Max(
                0f,
                penetration -
                firstMoved -
                secondMoved);

        if (remaining > 0f)
        {
            var extraSecond =
                PushContact(
                    ref secondPosition,
                    axis,
                    secondDirection *
                    remaining);

            if (extraSecond < remaining)
            {
                firstMoved +=
                    PushContact(
                        ref firstPosition,
                        axis,
                        -secondDirection *
                        (remaining -
                         extraSecond));
            }

            secondMoved +=
                extraSecond;
        }

        var firstChanged =
            firstPosition !=
            first.Position;
        var secondChanged =
            secondPosition !=
            second.Position;

        if (!firstChanged &&
            !secondChanged)
        {
            return false;
        }

        if (firstChanged)
        {
            var velocity =
                first.Velocity;
            velocity[axis] = 0f;
            _active[firstId] =
                first with
                {
                    Position = firstPosition,
                    Velocity = velocity,
                };
            _contactMoved[firstIndex] =
                true;
        }

        if (secondChanged)
        {
            var velocity =
                second.Velocity;
            velocity[axis] = 0f;
            _active[secondId] =
                second with
                {
                    Position = secondPosition,
                    Velocity = velocity,
                };
            _contactMoved[secondIndex] =
                true;
        }

        return true;
    }

    private float PushContact(
        ref Vector3 position,
        int axis,
        float distance)
    {
        if (distance == 0f)
        {
            return 0f;
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
        var moved = 0f;

        for (var index = 0;
             index < stepCount;
             index++)
        {
            var next =
                position;
            next[axis] += step;

            if (!VoxelWorldCollision
                    .QueryDetailed(
                        _world,
                        _blocks,
                        BoundsAt(next))
                    .IsClear)
            {
                break;
            }

            position = next;
            moved +=
                MathF.Abs(step);
        }

        return moved;
    }

    private void RefreshSettledSupport(
        ulong id,
        DroppedBlockState state)
    {
        RemoveSupportIndex(id);

        var probe =
            state.Position -
            Vector3.UnitY *
            (MaximumCollisionStep +
             ContactEpsilon);
        var collision =
            VoxelWorldCollision.QueryDetailed(
                _world,
                _blocks,
                BoundsAt(probe));

        if (collision.State ==
                VoxelWorldCollisionState.Blocked &&
            collision.Voxel is
                WorldVoxelCoord support)
        {
            IndexSettledSupport(
                state,
                support);
            return;
        }

        _active[id] =
            state with
            {
                IsSettled = false,
            };
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
                out _))
        {
            return;
        }

        RemoveSupportIndex(id);
    }

    private void RemoveSupportIndex(
        ulong id)
    {
        if (!_supportById.Remove(
                id,
                out var support) ||
            !_settledBySupport.TryGetValue(
                support,
                out var ids))
        {
            return;
        }

        ids.Remove(id);

        if (ids.Count == 0)
        {
            _settledBySupport.Remove(
                support);
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

    private readonly record struct ContactCell(
        int X,
        int Y,
        int Z)
    {
        public static ContactCell FromPosition(
            Vector3 position) =>
            new(
                (int)MathF.Floor(
                    position.X /
                    ContactGridCellSize),
                (int)MathF.Floor(
                    position.Y /
                    ContactGridCellSize),
                (int)MathF.Floor(
                    position.Z /
                    ContactGridCellSize));
    }
}
