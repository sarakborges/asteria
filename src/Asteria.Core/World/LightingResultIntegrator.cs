namespace Asteria.Core.World;

public readonly record struct LightingIntegrationResult(
    int ChangedVoxelCount,
    int DirtyChunkCount,
    int DirtyMeshletCount);

public sealed class LightingResultIntegrator
{
    private readonly VoxelWorld _world;
    private readonly WorldUpdateQueue _worldUpdates;
    private readonly FluidMeshUpdateQueue _fluidMeshUpdates;
    private readonly MeshletContentRevisions _terrainContentRevisions;
    private readonly MeshletContentRevisions _fluidContentRevisions;

    public LightingResultIntegrator(
        VoxelWorld world,
        WorldUpdateQueue worldUpdates,
        FluidMeshUpdateQueue fluidMeshUpdates,
        MeshletContentRevisions terrainContentRevisions,
        MeshletContentRevisions fluidContentRevisions)
    {
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _worldUpdates =
            worldUpdates ??
            throw new ArgumentNullException(nameof(worldUpdates));
        _fluidMeshUpdates =
            fluidMeshUpdates ??
            throw new ArgumentNullException(nameof(fluidMeshUpdates));
        _terrainContentRevisions =
            terrainContentRevisions ??
            throw new ArgumentNullException(
                nameof(terrainContentRevisions));
        _fluidContentRevisions =
            fluidContentRevisions ??
            throw new ArgumentNullException(
                nameof(fluidContentRevisions));
    }

    public LightingIntegrationResult Apply(
        VoxelWorld lightingSnapshot,
        IReadOnlyList<WorldVoxelCoord> changedPositions)
    {
        ArgumentNullException.ThrowIfNull(lightingSnapshot);
        ArgumentNullException.ThrowIfNull(changedPositions);

        if (changedPositions.Count == 0)
        {
            return default;
        }

        _world.CopyLightFrom(
            lightingSnapshot);

        var dirty =
            MeshletInvalidation.ForPositions(
                _world,
                changedPositions);
        var dirtyMeshlets = 0;

        foreach (var (coord, mask) in
                 dirty
                     .OrderBy(entry => entry.Key.Y)
                     .ThenBy(entry => entry.Key.Z)
                     .ThenBy(entry => entry.Key.X))
        {
            _terrainContentRevisions.Bump(
                coord,
                mask);
            _fluidContentRevisions.Bump(
                coord,
                mask);
            _worldUpdates.EnqueueMeshlets(
                coord,
                mask);
            _fluidMeshUpdates.EnqueueMeshlets(
                coord,
                mask);

            dirtyMeshlets +=
                mask.Indices().Count();
        }

        return new LightingIntegrationResult(
            changedPositions.Count,
            dirty.Count,
            dirtyMeshlets);
    }
}
