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

public sealed record VoxelStructureObjectChange(
    WorldVoxelCoord Position,
    WorldVoxelCoord Support,
    ObjectCell Object);

public sealed class VoxelMutationRuntime
{
    private readonly VoxelWorld _world;
    private readonly WorldUpdateQueue _worldUpdates;
    private readonly FluidUpdateQueue _fluidUpdates;
    private readonly FluidMeshUpdateQueue _fluidMeshUpdates;
    private readonly BlockPhysicsUpdateQueue _blockPhysicsUpdates;
    private readonly MeshletContentRevisions _terrainContentRevisions;
    private readonly MeshletContentRevisions _fluidContentRevisions;
    private ObjectDetachmentRuntime? _objectDetachment;

    /// <summary>Installs detached-Object drop consequences once per active
    /// Sphere, before any player or simulated voxel mutation occurs.</summary>
    public void ConfigureObjectDetachment(ObjectDetachmentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (_objectDetachment is not null)
            throw new InvalidOperationException("Object detachment already configured.");
        _objectDetachment = runtime;
    }

    /// <summary>Counts support drops so compound block/spike removals can
    /// preflight the same bounded physical drop pool as the Object owner.</summary>
    public int ObjectSupportDropCount(WorldVoxelCoord support,
        BlockBreakLootPolicy policy = BlockBreakLootPolicy.DropSelf) =>
        _objectDetachment?.DropCount(support, policy) ?? 0;

    /// <summary>Committed voxel changes; never raised for a rejected edit.
    /// Lifecycle consumers must not bypass this canonical mutation boundary.</summary>
    public event Action<VoxelWorldEdit>? BlockCellChanged;

    /// <summary>Object-only mutations do not enqueue block or fluid remeshing.
    /// Chunk ObjectRevision is the separate materialization invalidation key.</summary>
    public event Action<ObjectWorldEdit>? ObjectCellChanged;

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
        out VoxelWorldEdit edit,
        BlockBreakLootPolicy objectLootPolicy = BlockBreakLootPolicy.DropSelf)
    {
        var detach = ObjectDetachmentPlan.Empty;
        if (_objectDetachment is not null)
        {
            if (!_world.TryGetCell(position, out var current) ||
                !_objectDetachment.TryPrepare(
                    position, current, cell, objectLootPolicy, out detach))
            {
                edit = default;
                return false;
            }
        }
        if (!_world.SetCellAt(position, cell, out edit))
            return false;

        // Commit object drops before notifying dependent spike/physics systems.
        // A changed supporting block never silently discards attached Objects.
        _objectDetachment?.Commit(detach);
        EnqueueBlockEdit(position);
        BlockCellChanged?.Invoke(edit);
        return true;
    }

    public bool SetBlockStateAt(
        WorldVoxelCoord position,
        BlockStateSnapshot state,
        out VoxelWorldEdit edit,
        BlockBreakLootPolicy objectLootPolicy = BlockBreakLootPolicy.DropSelf)
    {
        ArgumentNullException.ThrowIfNull(state);
        var detach = ObjectDetachmentPlan.Empty;
        if (_objectDetachment is not null)
        {
            if (!_world.TryGetCell(position, out var current) ||
                !_objectDetachment.TryPrepare(position, current, state.Cell,
                    objectLootPolicy, out detach))
            {
                edit = default;
                return false;
            }
        }
        if (!_world.SetBlockStateAt(position, state, out edit))
            return false;

        _objectDetachment?.Commit(detach);
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

    public bool AddObjectAt(WorldVoxelCoord support, ObjectCell obj)
    {
        if (!_world.AddObjectAt(support, obj)) return false;
        ObjectCellChanged?.Invoke(new ObjectWorldEdit(
            support, obj, Placed: true, _world.Revision));
        return true;
    }

    public bool RemoveObjectAt(WorldVoxelCoord support, ObjectCell obj)
    {
        if (!_world.RemoveObjectAt(support, obj)) return false;
        ObjectCellChanged?.Invoke(new ObjectWorldEdit(
            support, obj, Placed: false, _world.Revision));
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
    public bool ApplyStructureChanges(
        IReadOnlyList<VoxelStructureChange> changes,
        IReadOnlyList<VoxelStructureObjectChange>? objects = null)
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

        // All Objects must be insertable after staged voxel changes. Capacity,
        // duplicate identities, support and free target are verified before
        // either voxels or Objects can mutate the world.
        if (objects is { Count: > 0 })
        {
            var finalCells = changes.ToDictionary(
                change => change.Position, change => change.Block?.Cell ?? VoxelCell.Empty);
            var counts = new Dictionary<WorldVoxelCoord, int>();
            var planned = new HashSet<(WorldVoxelCoord Support, ObjectCell Object)>();
            foreach (var obj in objects)
            {
                if (obj.Position.Y < 0 || obj.Support.Y < 0 ||
                    !_world.IsLoadedAt(obj.Position) ||
                    !_world.IsLoadedAt(obj.Support) ||
                    !planned.Add((obj.Support, obj.Object)))
                    return false;

                var target = finalCells.TryGetValue(obj.Position, out var stagedTarget)
                    ? stagedTarget : _world.GetCellOrEmpty(obj.Position);
                var support = finalCells.TryGetValue(obj.Support, out var stagedSupport)
                    ? stagedSupport : _world.GetCellOrEmpty(obj.Support);
                if (!target.IsEmpty || support.IsEmpty)
                    return false;

                if (!counts.TryGetValue(obj.Support, out var count))
                {
                    if (!_world.TryGetObjects(obj.Support, out var existing))
                        return false;
                    var supportChanged = finalCells.TryGetValue(obj.Support, out var next) &&
                        next.Block != _world.GetCellOrEmpty(obj.Support).Block;
                    if (!supportChanged && existing.Contains(obj.Object))
                        return false;
                    count = supportChanged ? 0 : existing.Count;
                }
                count++;
                if (count > Chunk.MaximumObjectsPerSupport)
                    return false;
                counts[obj.Support] = count;
            }
        }

        // Structures are committed as a batch: preflight combined Object
        // drops, not each voxel independently, or a late full drop pool would
        // leave the preceding structure edits partially applied.
        if (_objectDetachment is not null)
        {
            var detachedDropCount = 0;
            foreach (var change in changes)
            {
                var previous = _world.GetCellOrEmpty(change.Position);
                var next = change.Block?.Cell ?? VoxelCell.Empty;
                if (!_objectDetachment.TryPrepare(change.Position, previous, next,
                        BlockBreakLootPolicy.DropSelf, out var plan))
                    return false;
                detachedDropCount += plan.Rewards.Count;
            }
            if (detachedDropCount > _objectDetachment.AvailableDropCapacity)
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

        if (objects is not null)
        {
            foreach (var obj in objects)
                if (!AddObjectAt(obj.Support, obj.Object))
                    throw new InvalidOperationException(
                        $"Validated Structure Object could not attach at {obj.Support}.");
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
                !_world.CanContainFluidAt(change.Position))
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
