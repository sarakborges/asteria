namespace Asteria.Core.World;

public readonly record struct FluidMutationBatchResult(
    bool Accepted,
    int AppliedChangeCount,
    int UniquePositionCount)
{
    public static FluidMutationBatchResult Empty =>
        new(true, 0, 0);

    public static FluidMutationBatchResult Rejected =>
        new(false, 0, 0);
}

/// <summary>Final block and fluid state of one voxel in a structure batch.
/// A null block means air; fluid may be present only in air.</summary>
public sealed record VoxelStructureChange(
    WorldVoxelCoord Position,
    BlockStateSnapshot? Block,
    FluidCell Fluid);

public sealed class VoxelMutationRuntime
{
    private readonly VoxelWorld _world;
    private readonly WorldUpdateQueue _worldUpdates;
    private readonly FluidUpdateQueue _fluidUpdates;
    private readonly FluidMeshUpdateQueue _fluidMeshUpdates;
    private readonly BlockPhysicsUpdateQueue _blockPhysicsUpdates;
    private readonly MeshletContentRevisions _terrainContentRevisions;
    private readonly MeshletContentRevisions _fluidContentRevisions;

    /// <summary>Committed voxel changes; never raised for a rejected edit.
    /// Lifecycle consumers must not bypass this canonical mutation boundary.</summary>
    public event Action<VoxelWorldEdit>? BlockCellChanged;

    public VoxelMutationRuntime(
        VoxelWorld world,
        WorldUpdateQueue worldUpdates,
        FluidUpdateQueue fluidUpdates,
        FluidMeshUpdateQueue fluidMeshUpdates,
        BlockPhysicsUpdateQueue blockPhysicsUpdates,
        MeshletContentRevisions terrainContentRevisions,
        MeshletContentRevisions fluidContentRevisions)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _worldUpdates = worldUpdates ?? throw new ArgumentNullException(nameof(worldUpdates));
        _fluidUpdates = fluidUpdates ?? throw new ArgumentNullException(nameof(fluidUpdates));
        _fluidMeshUpdates = fluidMeshUpdates ?? throw new ArgumentNullException(nameof(fluidMeshUpdates));
        _blockPhysicsUpdates = blockPhysicsUpdates ?? throw new ArgumentNullException(nameof(blockPhysicsUpdates));
        _terrainContentRevisions = terrainContentRevisions ?? throw new ArgumentNullException(nameof(terrainContentRevisions));
        _fluidContentRevisions = fluidContentRevisions ?? throw new ArgumentNullException(nameof(fluidContentRevisions));
    }

    public bool SetBlockAt(
        WorldVoxelCoord position,
        BlockRuntimeId block,
        out VoxelWorldEdit edit) =>
        SetCellAt(
            position,
            new VoxelCell(block),
            out edit);

    public bool SetCellAt(
        WorldVoxelCoord position,
        VoxelCell cell,
        out VoxelWorldEdit edit)
    {
        if (!_world.SetCellAt(
                position,
                cell,
                out edit))
        {
            return false;
        }

        EnqueueBlockEdit(position);
        BlockCellChanged?.Invoke(edit);
        return true;
    }

    public bool SetBlockStateAt(
        WorldVoxelCoord position,
        BlockStateSnapshot state,
        out VoxelWorldEdit edit)
    {
        if (!_world.SetBlockStateAt(
                position,
                state,
                out edit))
        {
            return false;
        }

        EnqueueBlockEdit(position);
        BlockCellChanged?.Invoke(edit);
        return true;
    }

    public bool SetBlockSurfaceStateAt(
        WorldVoxelCoord position,
        BlockSurfaceState state,
        out VoxelWorldEdit edit)
    {
        if (!_world.SetBlockSurfaceStateAt(position, state, out edit))
            return false;

        _terrainContentRevisions.BumpVoxelEdit(_world, position);
        _worldUpdates.EnqueueSurfaceEdit(_world, position);
        return true;
    }

    public bool SetFluidAt(
        WorldVoxelCoord position,
        FluidCell fluid,
        out FluidWorldEdit edit)
    {
        if (!_world.SetFluidAt(
                position,
                fluid,
                out edit))
        {
            return false;
        }

        EnqueueFluidEdit(position);
        return true;
    }

    /// <summary>
    /// Applies a fully planned structure as one noninterleavable main-thread
    /// mutation operation. All input/residency preconditions are checked
    /// before a single cell or dependent queue is changed.
    /// </summary>
    public bool ApplyStructureChanges(IReadOnlyList<VoxelStructureChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var positions = new HashSet<WorldVoxelCoord>();
        foreach (var change in changes)
        {
            if (!positions.Add(change.Position) ||
                change.Position.Y < 0 ||
                !_world.IsLoadedAt(change.Position) ||
                (change.Block is not null && !change.Fluid.IsEmpty))
                return false;
        }

        foreach (var change in changes)
        {
            var currentCell = _world.GetCellOrEmpty(change.Position);
            var currentFluid = _world.GetFluidOrEmpty(change.Position);
            if (change.Block is { } block)
            {
                var currentBlock = currentCell.IsEmpty ? null :
                    BlockStateSnapshot.Capture(_world, change.Position, currentCell);
                if (currentBlock != block)
                {
                    if (!SetBlockStateAt(change.Position, block, out _))
                        throw new InvalidOperationException(
                            $"Validated structure mutation failed at {change.Position}.");
                }
                else if (!currentFluid.IsEmpty &&
                         !SetFluidAt(change.Position, FluidCell.Empty, out _))
                    throw new InvalidOperationException(
                        $"Validated structure fluid clear failed at {change.Position}.");
            }
            else
            {
                if (!currentCell.IsEmpty &&
                    !SetCellAt(change.Position, VoxelCell.Empty, out _))
                    throw new InvalidOperationException(
                        $"Validated structure clearing failed at {change.Position}.");
                if (_world.GetFluidOrEmpty(change.Position) != change.Fluid &&
                    !SetFluidAt(change.Position, change.Fluid, out _))
                    throw new InvalidOperationException(
                        $"Validated structure fluid mutation failed at {change.Position}.");
            }
        }

        return true;
    }

    public FluidMutationBatchResult ApplyFluidChanges(
        IReadOnlyList<FluidCellChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return FluidMutationBatchResult.Empty;
        }

        var virtualState =
            new Dictionary<WorldVoxelCoord, FluidCell>();

        foreach (var change in changes)
        {
            if (!_world.IsLoadedAt(
                    change.Position))
            {
                return FluidMutationBatchResult.Rejected;
            }

            var current =
                virtualState.TryGetValue(
                    change.Position,
                    out var pending)
                    ? pending
                    : _world.GetFluidOrEmpty(
                        change.Position);

            if (current != change.Previous)
            {
                return FluidMutationBatchResult.Rejected;
            }

            if (!change.Current.IsEmpty &&
                !_world.GetCellOrEmpty(
                        change.Position)
                    .IsEmpty)
            {
                return FluidMutationBatchResult.Rejected;
            }

            virtualState[change.Position] =
                change.Current;
        }

        var changedPositions =
            new HashSet<WorldVoxelCoord>();
        var applied = 0;

        foreach (var change in changes)
        {
            if (!_world.SetFluidAt(
                    change.Position,
                    change.Current,
                    out _))
            {
                throw new InvalidOperationException(
                    $"Validated fluid mutation unexpectedly failed at {change.Position}.");
            }

            changedPositions.Add(
                change.Position);
            applied++;
        }

        PublishFluidBatchConsequences(
            changedPositions);

        return new FluidMutationBatchResult(
            true,
            applied,
            changedPositions.Count);
    }

    private void EnqueueBlockEdit(
        WorldVoxelCoord position)
    {
        _terrainContentRevisions.BumpVoxelEdit(
            _world,
            position);
        _fluidContentRevisions.BumpVoxelEdit(
            _world,
            position);

        _worldUpdates.EnqueueVoxelEdit(
            _world,
            position);
        _fluidUpdates.EnqueueTopologyNeighborhood(
            position);
        _fluidMeshUpdates.EnqueueVoxelEdit(
            _world,
            position);
        _blockPhysicsUpdates.EnqueueVoxelEdit(
            position);
    }

    private void EnqueueFluidEdit(
        WorldVoxelCoord position)
    {
        PublishFluidBatchConsequences(
            [position]);
    }

    private void PublishFluidBatchConsequences(
        IEnumerable<WorldVoxelCoord> positions)
    {
        var bufferedPositions =
            positions.ToArray();
        var dirty =
            MeshletInvalidation.ForPositions(
                _world,
                bufferedPositions);

        foreach (var position in bufferedPositions)
        {
            _worldUpdates.EnqueueLighting(
                position);
            _fluidUpdates.EnqueueTopologyNeighborhood(
                position);
        }

        foreach (var (coord, mask) in dirty)
        {
            _fluidContentRevisions.Bump(
                coord,
                mask);
            _fluidMeshUpdates.EnqueueMeshlets(
                coord,
                mask);
        }
    }
}
