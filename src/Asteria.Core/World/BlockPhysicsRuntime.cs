using System.Numerics;

namespace Asteria.Core.World;

public readonly record struct FallingBlockId(ulong Value);

public readonly record struct FallingBlockState(
    FallingBlockId Id,
    BlockStateSnapshot Block,
    int ColumnX,
    int ColumnZ,
    double CenterY,
    double VelocityY)
{
    public VoxelCell Cell => Block.Cell;
}

public sealed record BlockPhysicsRuntimeSnapshot(
    ulong NextId,
    IReadOnlyList<FallingBlockState> ActiveBlocks);

public readonly record struct BlockPhysicsWakeResult(
    int FallingStarted,
    int UnsupportedRemoved)
{
    public static BlockPhysicsWakeResult Empty => default;

    public bool HasChanges =>
        FallingStarted > 0 ||
        UnsupportedRemoved > 0;
}

public sealed class BlockPhysicsRuntime : IDisposable
{
    private static readonly IComparer<FallingBlockId>
        FallingBlockIdComparer =
            Comparer<FallingBlockId>.Create(
                static (left, right) =>
                    left.Value.CompareTo(
                        right.Value));

    private sealed class FallingBlockAdvanceComparer : IComparer<FallingBlockId>
    {
        private readonly IReadOnlyDictionary<FallingBlockId, FallingBlockState> _active;

        public FallingBlockAdvanceComparer(
            IReadOnlyDictionary<FallingBlockId, FallingBlockState> active) =>
            _active = active;

        public int Compare(FallingBlockId left, FallingBlockId right)
        {
            var leftState = _active[left];
            var rightState = _active[right];
            var result = leftState.ColumnX.CompareTo(rightState.ColumnX);
            if (result != 0)
                return result;

            result = leftState.ColumnZ.CompareTo(rightState.ColumnZ);
            if (result != 0)
                return result;

            result = leftState.CenterY.CompareTo(rightState.CenterY);
            return result != 0
                ? result
                : left.Value.CompareTo(right.Value);
        }
    }

    private const double MaximumDeltaSeconds = 0.05;
    private const double SupportEpsilon = 0.0001;
    private const double FallingBlockHeight = 1.0;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly BlockPhysicsUpdateQueue _updates;
    private readonly DroppedBlockRuntime _droppedBlocks;
    private readonly int _maximumActive;
    private readonly SortedDictionary<FallingBlockId, FallingBlockState>
        _active =
            new(FallingBlockIdComparer);
    private readonly FallingBlockId[] _advanceIds;
    private readonly IComparer<FallingBlockId> _advanceComparer;
    private readonly Func<WorldVoxelCoord, FallingBlockId?, bool>
        _fallingBlockOccupancyQuery;
    // A falling block whose next support lies in an unloaded chunk is
    // dormant until that chunk becomes resident. It remains authoritative
    // in _active and is included in snapshots.
    private readonly Dictionary<ChunkCoord, HashSet<FallingBlockId>>
        _waitingFallingByChunk = [];
    private readonly Dictionary<FallingBlockId, ChunkCoord>
        _fallingWaitingForChunk = [];

    private ulong _nextId;

    public BlockPhysicsRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelMutationRuntime mutations,
        BlockPhysicsUpdateQueue updates,
        DroppedBlockRuntime droppedBlocks,
        int maximumActive = 2048,
        BlockPhysicsRuntimeSnapshot? restore = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _droppedBlocks =
            droppedBlocks ??
            throw new ArgumentNullException(nameof(droppedBlocks));

        if (maximumActive <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumActive));
        }

        _maximumActive = maximumActive;
        _advanceIds =
            new FallingBlockId[maximumActive];
        _advanceComparer =
            new FallingBlockAdvanceComparer(_active);
        _fallingBlockOccupancyQuery =
            IsOccupiedByOtherFallingBlock;

        if (restore is not null)
        {
            if (restore.ActiveBlocks.Count >
                maximumActive)
            {
                throw new ArgumentException(
                    "Restored falling-block state exceeds active capacity.",
                    nameof(restore));
            }

            _nextId =
                restore.NextId;

            foreach (var state in
                     restore.ActiveBlocks
                         .OrderBy(
                             value =>
                                 value.Id.Value))
            {
                if (!_active.TryAdd(
                        state.Id,
                        state))
                {
                    throw new ArgumentException(
                        $"Duplicate restored falling-block id: {state.Id.Value}",
                        nameof(restore));
                }
            }
        }

        _mutations.ConfigureFallingBlockOccupancy(
            _fallingBlockOccupancyQuery);
        _world.ChunkBecameResident += OnChunkBecameResident;
    }

    public void Dispose()
    {
        _world.ChunkBecameResident -= OnChunkBecameResident;
        _mutations.ClearFallingBlockOccupancy(
            _fallingBlockOccupancyQuery);
    }

    private void OnChunkBecameResident(ChunkCoord coord)
    {
        _updates.WakeResidentChunk(coord);
        if (!_waitingFallingByChunk.Remove(coord, out var waiting))
            return;

        foreach (var id in waiting)
            _fallingWaitingForChunk.Remove(id);
    }

    private void WaitForFallingChunk(FallingBlockId id, WorldVoxelCoord support)
    {
        var coord = VoxelCoordinates.FromWorld(
            support.X, support.Y, support.Z).Chunk;
        if (_fallingWaitingForChunk.TryGetValue(id, out var previous))
        {
            if (previous == coord)
                return;

            var previousWaiters = _waitingFallingByChunk[previous];
            previousWaiters.Remove(id);
            if (previousWaiters.Count == 0)
                _waitingFallingByChunk.Remove(previous);
        }

        if (!_waitingFallingByChunk.TryGetValue(coord, out var waiters))
        {
            waiters = [];
            _waitingFallingByChunk.Add(coord, waiters);
        }

        waiters.Add(id);
        _fallingWaitingForChunk[id] = coord;
    }

    public IReadOnlyCollection<FallingBlockState> ActiveBlocks =>
        _active.Values;

    public int ActiveCount => _active.Count;

    public int WaitingFallingCount => _fallingWaitingForChunk.Count;

    private bool IsOccupiedByOtherFallingBlock(
        WorldVoxelCoord position,
        FallingBlockId? ignoredFallingBlockId)
    {
        if (position.Y < 0)
            return false;

        foreach (var state in _active.Values)
        {
            if (ignoredFallingBlockId == state.Id ||
                state.ColumnX != position.X ||
                state.ColumnZ != position.Z)
                continue;

            var bottom = state.CenterY - FallingBlockHeight / 2.0;
            var top = state.CenterY + FallingBlockHeight / 2.0;
            if (top > position.Y + SupportEpsilon &&
                bottom < position.Y + 1.0 - SupportEpsilon)
                return true;
        }

        return false;
    }

    public BlockPhysicsRuntimeSnapshot CaptureState() =>
        new(
            _nextId,
            _active.Values.ToArray());

    public int EnqueueResidentChunk(ChunkCoord coord)
    {
        if (!_world.TryGetChunk(coord, out var chunk))
        {
            return 0;
        }

        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);
        var queued = 0;

        chunk.VisitBlockCells(
            (x, y, z, cell) =>
            {
                var definition =
                    _blocks.GetDefinition(cell.Block);
                var position = new WorldVoxelCoord(
                    originX + x, originY + y, originZ + z);
                var hasGravity = definition.HasTag(
                    BlockPhysicsCapabilities.Gravity);
                var hasSupportDependency =
                    definition.HasTag(BlockPhysicsCapabilities.SupportBelow) ||
                    definition.HasTag(BlockPhysicsCapabilities.SupportAbove) ||
                    definition.HasTag(BlockPhysicsCapabilities.SupportWall);

                if (!hasGravity && !hasSupportDependency)
                    return;

                // Chunk arrival is not itself a reason to schedule a support
                // check. Supported attached blocks are stable until a voxel
                // edit changes their dependency. Keep unloaded and unsupported
                // dependencies queued so they can be resolved after arrival.
                if (hasSupportDependency &&
                    BlockSupportRules.Evaluate(
                        _world, _blocks, definition, cell,
                        _world.GetMicroblockMaskOrEmpty(position),
                        position) != BlockSupportState.Supported)
                {
                    _updates.Enqueue(position);
                    queued++;
                    return;
                }

                if (!hasGravity)
                    return;

                // Gravity blocks with known solid support are already stable.
                // Queue only empty or unloaded lower cells; y=0 has no lower
                // world voxel and can never start falling.
                var belowState = BlockMaterializationRules.Evaluate(
                    _world, position + (0, -1, 0));
                if (belowState is not (
                        BlockMaterializationState.Available or
                        BlockMaterializationState.Unloaded))
                    return;

                _updates.Enqueue(position);
                queued++;
            });

        return queued;
    }

    public BlockPhysicsWakeResult ProcessWakeups()
    {
        var fallingStarted = 0;
        var unsupportedRemoved = 0;

        foreach (var position in _updates.DrainBatch())
        {
            _droppedBlocks.NotifyVoxelEdit(position);
            if (!_world.TryGetCell(position, out var cell) ||
                cell.IsEmpty)
            {
                continue;
            }

            var definition =
                _blocks.GetDefinition(cell.Block);
            var support =
                BlockSupportRules.Evaluate(
                    _world,
                    _blocks,
                    definition,
                    cell,
                    _world.GetMicroblockMaskOrEmpty(
                        position),
                    position,
                    out var missingSupport);

            if (support == BlockSupportState.Unloaded)
            {
                _updates.WaitForChunk(
                    position,
                    VoxelCoordinates.FromWorld(
                        missingSupport.X, missingSupport.Y, missingSupport.Z).Chunk);
                continue;
            }

            if (support == BlockSupportState.Unsupported)
            {
                var detachedBlock =
                    BlockStateSnapshot.Capture(
                        _world,
                        position,
                        cell);

                if (_mutations.SetCellAt(
                        position,
                        VoxelCell.Empty,
                        out _))
                {
                    unsupportedRemoved++;

                    if (definition.DropsSelf)
                    {
                        _droppedBlocks.Spawn(
                            detachedBlock,
                            new Vector3(
                                position.X + 0.5f,
                                position.Y + 0.5f,
                                position.Z + 0.5f));
                    }
                }
                else
                {
                    _updates.Enqueue(position);
                }

                continue;
            }

            if (!definition.HasTag(
                    BlockPhysicsCapabilities.Gravity))
            {
                continue;
            }

            var below =
                position + (0, -1, 0);
            var belowState =
                BlockMaterializationRules.Evaluate(
                    _world,
                    below);

            if (belowState ==
                BlockMaterializationState.BelowWorld)
            {
                continue;
            }

            if (belowState ==
                BlockMaterializationState.Unloaded)
            {
                _updates.WaitForChunk(position,
                    VoxelCoordinates.FromWorld(below.X, below.Y, below.Z).Chunk);
                continue;
            }

            if (belowState ==
                BlockMaterializationState.Occupied)
            {
                continue;
            }

            if (_active.Count >=
                _maximumActive)
            {
                _updates.Enqueue(position);
                continue;
            }

            var fallingBlock =
                BlockStateSnapshot.Capture(
                    _world,
                    position,
                    cell);

            if (!_mutations.SetCellAt(
                    position,
                    VoxelCell.Empty,
                    out _))
            {
                _updates.Enqueue(position);
                continue;
            }

            var id =
                new FallingBlockId(
                    checked(++_nextId));

            _active.Add(
                id,
                new FallingBlockState(
                    id,
                    fallingBlock,
                    position.X,
                    position.Z,
                    position.Y + 0.5,
                    0.0));
            fallingStarted++;
        }

        return new BlockPhysicsWakeResult(
            fallingStarted,
            unsupportedRemoved);
    }

    public int Advance(
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
            deltaSeconds == 0 ||
            gravityStrength == 0)
        {
            return 0;
        }

        var delta =
            Math.Min(deltaSeconds, MaximumDeltaSeconds);
        var activeCount = 0;

        // Resolve lower blocks first within each column. Falling bodies are
        // separate from voxel storage, so the voxel sweep alone cannot see
        // another body and would otherwise allow faster blocks to pass through
        // slower blocks in the same column.
        foreach (var id in _active.Keys)
            _advanceIds[activeCount++] = id;
        Array.Sort(
            _advanceIds,
            0,
            activeCount,
            _advanceComparer);

        var landed = 0;
        var hasColumn = false;
        var columnX = 0;
        var columnZ = 0;
        FallingBlockState? lowerBlock = null;

        for (var index = 0; index < activeCount; index++)
        {
            var id = _advanceIds[index];
            if (!_active.TryGetValue(id, out var state))
                continue;

            if (!hasColumn ||
                state.ColumnX != columnX ||
                state.ColumnZ != columnZ)
            {
                hasColumn = true;
                columnX = state.ColumnX;
                columnZ = state.ColumnZ;
                lowerBlock = null;
            }

            if (_fallingWaitingForChunk.ContainsKey(id))
            {
                state = ConstrainAboveFallingBlock(state, lowerBlock);
                _active[id] = state;
                lowerBlock = state;
                continue;
            }

            var remainsActive = AdvanceFallingBlock(
                state,
                lowerBlock,
                delta,
                gravityStrength,
                out var blockLanded);

            if (blockLanded)
                landed++;

            if (remainsActive && _active.TryGetValue(id, out var updated))
                lowerBlock = updated;
            else
                // A landed block is now an ordinary occupied voxel; the
                // voxel sweep will use it as support for blocks above.
                lowerBlock = null;
        }

        return landed;
    }

    private bool AdvanceFallingBlock(
        FallingBlockState state,
        FallingBlockState? lowerBlock,
        double delta,
        double gravityStrength,
        out bool landed)
    {
        landed = false;
        var id = state.Id;

        // Do not simulate gravity inside a resident chunk until the chunk
        // directly below it is resident. Otherwise falling blocks can advance
        // toward an unloaded vertical gap before the sweep reaches that edge.
        var currentVoxelY = (int)Math.Floor(state.CenterY);
        var currentChunk = VoxelCoordinates.FromWorld(
            state.ColumnX, currentVoxelY, state.ColumnZ).Chunk;
        if (currentChunk.Y > 0)
        {
            var lowerChunk = new ChunkCoord(
                currentChunk.X, currentChunk.Y - 1, currentChunk.Z);
            if (!_world.ContainsChunk(lowerChunk))
            {
                state = ConstrainAboveFallingBlock(
                    state with { VelocityY = 0.0 },
                    lowerBlock);
                _active[id] = state;
                var missingChunkVoxel = new WorldVoxelCoord(
                    state.ColumnX,
                    checked(currentChunk.Y * Chunk.Size - 1),
                    state.ColumnZ);
                WaitForFallingChunk(id, missingChunkVoxel);
                return true;
            }
        }

        var velocity =
            state.VelocityY -
            gravityStrength * delta;
        var targetCenterY =
            state.CenterY +
            velocity * delta;
        var currentBottom =
            state.CenterY - FallingBlockHeight / 2.0;
        var targetBottom =
            targetCenterY - FallingBlockHeight / 2.0;
        var highestSupport =
            (int)Math.Floor(
                currentBottom -
                SupportEpsilon);
        var lowestSupport =
            (int)Math.Floor(
                targetBottom -
                SupportEpsilon);
        int? landingY = null;
        var blockedByUnloaded = false;
        var missingSupport = default(WorldVoxelCoord);

        for (var supportY = highestSupport;
             supportY >= lowestSupport;
             supportY--)
        {
            if (supportY < 0)
            {
                landingY = 0;
                break;
            }

            var support =
                new WorldVoxelCoord(
                    state.ColumnX,
                    supportY,
                    state.ColumnZ);
            var supportState =
                BlockMaterializationRules.Evaluate(
                    _world,
                    support);

            if (supportState ==
                BlockMaterializationState.Unloaded)
            {
                blockedByUnloaded = true;
                missingSupport = support;
                break;
            }

            if (supportState ==
                BlockMaterializationState.Occupied)
            {
                landingY =
                    supportY + 1;
                break;
            }
        }

        if (blockedByUnloaded)
        {
            state = ConstrainAboveFallingBlock(
                state with { VelocityY = 0.0 },
                lowerBlock);
            _active[id] = state;
            WaitForFallingChunk(id, missingSupport);
            return true;
        }

        if (landingY is not { } voxelY)
        {
            state = ConstrainAboveFallingBlock(
                state with
                {
                    CenterY = targetCenterY,
                    VelocityY = velocity,
                },
                lowerBlock);
            _active[id] = state;
            return true;
        }

        // If the voxel sweep only sees the floor below another falling body,
        // that body takes precedence. Clamp to its top and inherit its vertical
        // velocity, so stacked bodies continue moving together without overlap.
        var landingCenterY =
            voxelY + FallingBlockHeight / 2.0;
        if (lowerBlock is { } lower &&
            landingCenterY < lower.CenterY + FallingBlockHeight)
        {
            _active[id] = ConstrainAboveFallingBlock(
                state with
                {
                    CenterY = landingCenterY,
                    VelocityY = velocity,
                },
                lowerBlock);
            return true;
        }

        var landing =
            new WorldVoxelCoord(
                state.ColumnX,
                voxelY,
                state.ColumnZ);

        var landingState =
            BlockMaterializationRules.Evaluate(
                _world,
                landing);

        if (landingState ==
            BlockMaterializationState.Unloaded)
        {
            state = ConstrainAboveFallingBlock(
                state with { VelocityY = 0.0 },
                lowerBlock);
            _active[id] = state;
            WaitForFallingChunk(id, landing);
            return true;
        }

        if (landingState !=
            BlockMaterializationState.Available)
        {
            state = ConstrainAboveFallingBlock(
                state with
                {
                    CenterY = landingCenterY,
                    VelocityY = 0.0,
                },
                lowerBlock);
            _active[id] = state;
            return true;
        }

        if (_mutations.SetFallingBlockStateAt(
                landing,
                state.Block,
                id,
                out _))
        {
            _active.Remove(id);
            landed = true;
            return false;
        }

        state = ConstrainAboveFallingBlock(
            state with
            {
                CenterY = landingCenterY,
                VelocityY = 0.0,
            },
            lowerBlock);
        _active[id] = state;
        return true;
    }

    private static FallingBlockState ConstrainAboveFallingBlock(
        FallingBlockState state,
        FallingBlockState? lowerBlock)
    {
        if (lowerBlock is not { } lower ||
            state.CenterY >= lower.CenterY + FallingBlockHeight)
            return state;

        return state with
        {
            CenterY = lower.CenterY + FallingBlockHeight,
            VelocityY = lower.VelocityY,
        };
    }
}
