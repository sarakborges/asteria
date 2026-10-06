namespace Asteria.Core.World;

public readonly record struct FallingBlockId(
    ulong Value);

public readonly record struct FallingBlockState(
    FallingBlockId Id,
    VoxelCell Cell,
    int ColumnX,
    int ColumnZ,
    double CenterY,
    double VelocityY);

public sealed class BlockGravityRuntime
{
    public const string GravityTag = "gravity";

    private const double MaximumDeltaSeconds = 0.05;
    private const double SupportEpsilon = 0.0001;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly BlockGravityUpdateQueue _updates;
    private readonly Dictionary<FallingBlockId, FallingBlockState>
        _active = [];

    private ulong _nextId;

    public BlockGravityRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelMutationRuntime mutations,
        BlockGravityUpdateQueue updates)
    {
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _blocks =
            blocks ??
            throw new ArgumentNullException(nameof(blocks));
        _mutations =
            mutations ??
            throw new ArgumentNullException(nameof(mutations));
        _updates =
            updates ??
            throw new ArgumentNullException(nameof(updates));
    }

    public IReadOnlyCollection<FallingBlockState>
        ActiveBlocks =>
        _active.Values;

    public int ActiveCount => _active.Count;

    public int EnqueueResidentChunk(
        ChunkCoord coord)
    {
        if (!_world.TryGetChunk(
                coord,
                out var chunk))
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
                    var cell =
                        chunk.GetCell(x, y, z);

                    if (cell.IsEmpty ||
                        !_blocks
                            .GetDefinition(cell.Block)
                            .HasTag(GravityTag))
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

    public int ProcessWakeups()
    {
        var started = 0;

        foreach (var position in
                 _updates.DrainBatch())
        {
            if (!_world.TryGetCell(
                    position,
                    out var cell) ||
                cell.IsEmpty)
            {
                continue;
            }

            var definition =
                _blocks.GetDefinition(cell.Block);

            if (!definition.HasTag(GravityTag))
            {
                continue;
            }

            var below =
                position + (0, -1, 0);

            if (below.Y < 0)
            {
                continue;
            }

            if (!_world.IsLoadedAt(below))
            {
                _updates.Enqueue(position);
                continue;
            }

            if (!_world
                    .GetCellOrEmpty(below)
                    .IsEmpty)
            {
                continue;
            }

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
                    cell,
                    position.X,
                    position.Z,
                    position.Y + 0.5,
                    0.0));
            started++;
        }

        return started;
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
            Math.Min(
                deltaSeconds,
                MaximumDeltaSeconds);
        var landed = 0;

        foreach (var id in
                 _active.Keys.ToArray())
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

                if (!_world
                        .GetCellOrEmpty(support)
                        .IsEmpty)
                {
                    landingY = supportY + 1;
                    break;
                }
            }

            if (blockedByUnloaded)
            {
                _active[id] =
                    state with
                    {
                        VelocityY = 0.0,
                    };
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
                    state with
                    {
                        VelocityY = 0.0,
                    };
                continue;
            }

            if (!_world
                    .GetCellOrEmpty(landing)
                    .IsEmpty)
            {
                _active[id] =
                    state with
                    {
                        CenterY = voxelY + 0.5,
                        VelocityY = 0.0,
                    };
                continue;
            }

            if (_mutations.SetCellAt(
                    landing,
                    state.Cell,
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
