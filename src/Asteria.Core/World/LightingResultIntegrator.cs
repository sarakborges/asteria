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
    // One bounded lighting-publication window accumulates derived meshlet
    // invalidations. No second light state or geometry owner is introduced.
    private readonly Dictionary<ChunkCoord, ChunkMeshletMask>
        _accumulatedRefreshes = new();

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

    /// <summary>
    /// Apply a bounded slice of one lighting result without immediately
    /// rebuilding its affected meshlets. The caller must flush the accumulated
    /// refreshes after a bounded number of voxels, on completion, or on stale
    /// cancellation (partially published light must never be left unrefreshed).
    /// </summary>
    public void AccumulateRange(
        VoxelWorld lightingSnapshot,
        IReadOnlyList<WorldVoxelCoord> positions,
        int offset,
        int count)
    {
        ArgumentNullException.ThrowIfNull(lightingSnapshot);
        ArgumentNullException.ThrowIfNull(positions);
        if (offset < 0 || count < 0 || offset > positions.Count - count)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count == 0) return;

        _world.CopyLightPositionsFrom(
            lightingSnapshot, positions, offset, count);
        // The copied positions may span multiple chunks, so retain the
        // canonical one-voxel halo invalidation and union by meshlet mask.
        var slice = new WorldVoxelCoord[count];
        for (var index = 0; index < count; index++)
            slice[index] = positions[offset + index];
        foreach (var (coord, mask) in
                 MeshletInvalidation.ForPositions(_world, slice))
        {
            _accumulatedRefreshes[coord] =
                _accumulatedRefreshes.TryGetValue(coord, out var existing)
                    ? existing.Union(mask)
                    : mask;
        }
    }

    /// <summary>
    /// Publish one coalesced invalidation per affected meshlet. Recheck
    /// residency and content when flushing because chunks can retire while
    /// a lighting result is published incrementally.
    /// </summary>
    public LightingIntegrationResult FlushAccumulated()
    {
        var result = EnqueuePresentationRefreshes(_accumulatedRefreshes);
        _accumulatedRefreshes.Clear();
        return result;
    }

    private LightingIntegrationResult EnqueuePresentationRefreshes(
        IReadOnlyList<WorldVoxelCoord> changedPositions) =>
        EnqueuePresentationRefreshes(
            MeshletInvalidation.ForPositions(_world, changedPositions),
            changedPositions.Count);

    private LightingIntegrationResult EnqueuePresentationRefreshes(
        IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> dirty,
        int changedVoxelCount = 0)
    {
        var dirtyChunks = 0;
        var dirtyMeshlets = 0;

        foreach (var (coord, mask) in
                 dirty
                     .OrderBy(entry => entry.Key.Y)
                     .ThenBy(entry => entry.Key.Z)
                     .ThenBy(entry => entry.Key.X))
        {
            // Lighting is presentation-only dirtiness. Keep in-flight
            // structural work valid and enqueue a later vertex refresh.
            if (!_world.TryGetChunk(coord, out var chunk))
                continue;

            if (!chunk.HasTerrainContent && !chunk.HasFluidContent)
                continue;

            if (chunk.HasTerrainContent)
                _worldUpdates.EnqueueMeshlets(coord, mask);

            if (chunk.HasFluidContent)
                _fluidMeshUpdates.EnqueueMeshlets(coord, mask);

            dirtyChunks++;
            dirtyMeshlets += mask.SelectedCount;
        }

        return new LightingIntegrationResult(
            changedVoxelCount, dirtyChunks, dirtyMeshlets);
    }
}
