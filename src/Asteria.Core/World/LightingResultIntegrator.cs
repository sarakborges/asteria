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

    public LightingResultIntegrator(
        VoxelWorld world,
        WorldUpdateQueue worldUpdates,
        FluidMeshUpdateQueue fluidMeshUpdates)
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

        _world.CopyChangedLightFrom(
            lightingSnapshot,
            changedPositions);
        return EnqueuePresentationRefreshes(changedPositions);
    }

    /// <summary>
    /// Publishes one bounded part of a completed lighting update. Calls can
    /// be spread across frames without copying entire chunk light arrays.
    /// </summary>
    public LightingIntegrationResult ApplyRange(
        VoxelWorld lightingSnapshot,
        IReadOnlyList<WorldVoxelCoord> positions,
        int offset,
        int count)
    {
        ArgumentNullException.ThrowIfNull(lightingSnapshot);
        ArgumentNullException.ThrowIfNull(positions);
        if (offset < 0 || count < 0 || offset > positions.Count - count)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count == 0)
            return default;

        _world.CopyLightPositionsFrom(
            lightingSnapshot, positions, offset, count);
        var slice = new WorldVoxelCoord[count];
        for (var i = 0; i < count; i++)
            slice[i] = positions[offset + i];
        return EnqueuePresentationRefreshes(slice);
    }

    private LightingIntegrationResult EnqueuePresentationRefreshes(
        IReadOnlyList<WorldVoxelCoord> changedPositions)
    {
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
            // Lighting changes are presentation refreshes, not structural
            // content mutations. Keep an in-flight structural mesh valid so
            // block edits can publish immediately; the queued refresh below
            // rebuilds current vertex lighting afterward.
            if (!_world.TryGetChunk(
                    coord,
                    out var chunk))
            {
                continue;
            }

            if (chunk.HasTerrainContent)
            {
                _worldUpdates.EnqueueMeshlets(
                    coord,
                    mask);
            }

            if (chunk.HasFluidContent)
            {
                _fluidMeshUpdates.EnqueueMeshlets(
                    coord,
                    mask);
            }

            dirtyMeshlets +=
                mask.SelectedCount;
        }

        return new LightingIntegrationResult(
            changedPositions.Count,
            dirty.Count,
            dirtyMeshlets);
    }
}
