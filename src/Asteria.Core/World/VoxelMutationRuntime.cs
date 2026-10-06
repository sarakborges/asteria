namespace Asteria.Core.World;

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
        var displacedFluid =
            cell.IsEmpty
                ? FluidCell.Empty
                : _world.GetFluidOrEmpty(position);

        if (!_world.SetCellAt(
                position,
                cell,
                out edit))
        {
            return false;
        }

        EnqueueBlockEdit(position);

        if (!displacedFluid.IsEmpty)
        {
            EnqueueFluidEdit(position);
        }

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
        _fluidContentRevisions.BumpVoxelEdit(
            _world,
            position);
        _fluidMeshUpdates.EnqueueVoxelEdit(
            _world,
            position);
        _worldUpdates.EnqueueLighting(
            position);
        _fluidUpdates.EnqueueTopologyNeighborhood(
            position);
    }
}
