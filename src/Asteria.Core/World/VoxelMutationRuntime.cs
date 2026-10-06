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

public sealed class VoxelMutationRuntime
{
    private readonly VoxelWorld _world;
    private readonly WorldUpdateQueue _worldUpdates;
    private readonly FluidUpdateQueue _fluidUpdates;
    private readonly FluidMeshUpdateQueue _fluidMeshUpdates;
    private readonly BlockPhysicsUpdateQueue _blockPhysicsUpdates;
    private readonly MeshletContentRevisions _terrainContentRevisions;
    private readonly MeshletContentRevisions _fluidContentRevisions;

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
