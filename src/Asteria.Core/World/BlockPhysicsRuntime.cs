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

public readonly record struct BlockPhysicsWakeResult(
    int FallingStarted,
    int UnsupportedRemoved)
{
    public static BlockPhysicsWakeResult Empty => default;

    public bool HasChanges =>
        FallingStarted > 0 ||
        UnsupportedRemoved > 0;
}

public sealed class BlockPhysicsRuntime
{
    private const double MaximumDeltaSeconds = 0.05;
    private const double SupportEpsilon = 0.0001;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly BlockPhysicsUpdateQueue _updates;
    private readonly DroppedBlockRuntime _droppedBlocks;
    private readonly Dictionary<FallingBlockId, FallingBlockState>
        _active = [];

    private ulong _nextId;

    public BlockPhysicsRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelMutationRuntime mutations,
        BlockPhysicsUpdateQueue updates,
        DroppedBlockRuntime droppedBlocks)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _droppedBlocks =
            droppedBlocks ??
            throw new ArgumentNullException(nameof(droppedBlocks));
    }

    public IReadOnlyCollection<FallingBlockState> ActiveBlocks =>
        _active.Values;

    public int ActiveCount => _active.Count;

    public int EnqueueResidentChunk(ChunkCoord coord)
    {
        if (!_world.TryGetChunk(coord, out var chunk))
        {
            return 0;
        }

        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);
        var queued = 0;

        for (var y = 0; y < Chunk.Size; y++)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var cell = chunk.GetCell(x, y, z);
                    if (cell.IsEmpty)
                    {
                        continue;
                    }

                    var definition =
                        _blocks.GetDefinition(cell.Block);

                    if (!definition.HasTag(BlockPhysicsCapabilities.Gravity) &&
                        !definition.HasTag(BlockPhysicsCapabilities.SupportBelow))
                    {
                        continue;
                    }

                    _updates.Enqueue(
                        new WorldVoxelCoord(
                            originX + x,
                            originY + y,
                            originZ + z));
                    queued++;
                }
            }
        }

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
                    position);

            if (support == BlockSupportState.Unloaded)
            {
                _updates.Enqueue(position);
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

            var below = position + (0, -1, 0);

            if (below.Y < 0)
            {
                continue;
            }

            if (!_world.IsLoadedAt(below))
            {
                _updates.Enqueue(position);
                continue;
            }

            if (!_world.GetCellOrEmpty(below).IsEmpty)
            {
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
        var landed = 0;

        foreach (var id in _active.Keys.ToArray())
        {
            var state = _active[id];
            var velocity =
                state.VelocityY -
                gravityStrength * delta;
            var targetCenterY =
                state.CenterY +
                velocity * delta;
            var currentBottom =
                state.CenterY - 0.5;
            var targetBottom =
                targetCenterY - 0.5;
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

                if (!_world.IsLoadedAt(support))
                {
                    blockedByUnloaded = true;
                    break;
                }

                if (!_world.GetCellOrEmpty(support).IsEmpty)
                {
                    landingY = supportY + 1;
                    break;
                }
            }

            if (blockedByUnloaded)
            {
                _active[id] =
                    state with { VelocityY = 0.0 };
                continue;
            }

            if (landingY is not { } voxelY)
            {
                _active[id] =
                    state with
                    {
                        CenterY = targetCenterY,
                        VelocityY = velocity,
                    };
                continue;
            }

            var landing =
                new WorldVoxelCoord(
                    state.ColumnX,
                    voxelY,
                    state.ColumnZ);

            if (!_world.IsLoadedAt(landing))
            {
                _active[id] =
                    state with { VelocityY = 0.0 };
                continue;
            }

            if (!_world.GetCellOrEmpty(landing).IsEmpty)
            {
                _active[id] =
                    state with
                    {
                        CenterY = voxelY + 0.5,
                        VelocityY = 0.0,
                    };
                continue;
            }

            if (_mutations.SetBlockStateAt(
                    landing,
                    state.Block,
                    out _))
            {
                _active.Remove(id);
                landed++;
            }
            else
            {
                _active[id] =
                    state with
                    {
                        CenterY = voxelY + 0.5,
                        VelocityY = 0.0,
                    };
            }
        }

        return landed;
    }
}
