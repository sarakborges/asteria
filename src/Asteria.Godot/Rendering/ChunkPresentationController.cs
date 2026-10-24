using System.Diagnostics;
using Asteria.Core.World;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Rendering;

public readonly record struct ChunkPresentationIntegrationStats(
    int Published,
    int Stale,
    int Remaining,
    double ElapsedMilliseconds,
    double RenderBuildMilliseconds = 0d,
    double CollisionBuildMilliseconds = 0d,
    long RenderVertices = 0,
    long CollisionTriangles = 0,
    int GpuAttributePatches = 0)
{
    public int Handled => Published + Stale;
}

public sealed class ChunkPresentationController
{
    private readonly Node3D _parent;
    private readonly VoxelWorld _world;
    private readonly WorldUpdateQueue _worldUpdates;
    private readonly FluidMeshUpdateQueue _fluidMeshUpdates;
    private readonly MeshletContentRevisions _terrainRevisions;
    private readonly MeshletContentRevisions _fluidRevisions;
    private readonly VoxelTerrainMaterialSet _terrainMaterials;
    private readonly FluidMaterialCatalog _fluidMaterials;
    private readonly Dictionary<ChunkCoord, ChunkPresentation>
        _presentations = [];
    // Only a bounded set of recently retired Godot meshes remains warm.
    // VoxelWorld keeps ALL materialized content, independently of this cache.
    // A complete four-chunk render sphere often contains ~300 chunks. The
    // previous capacity of 96 lost most meshes before a return trip.
    // Keep this finite; the archive remains authoritative beyond the cache.
    private const int MaximumWarmPresentations = 384;
    private readonly BoundedReuseCache<ChunkCoord, ChunkPresentation>
        _warmPresentations = new(MaximumWarmPresentations);
    private int _warmReuses;
    private int _warmEvictions;
    private int _warmTerrainMeshletsReused;
    private int _warmFluidMeshletsReused;
    private readonly DeduplicatedQueue<ChunkMeshletKey>
        _priorityTerrainPublicationOrder = new();
    private readonly DeduplicatedQueue<ChunkMeshletKey>
        _backgroundTerrainPublicationOrder = new();
    private readonly Dictionary<ChunkMeshletKey, TerrainPublication>
        _terrainPublications = [];
    private readonly DeduplicatedQueue<ChunkCoord> _pendingTerrainMeshFlush = new();
    private readonly DeduplicatedQueue<ChunkCoord> _pendingFluidMeshFlush = new();
    private readonly DeduplicatedQueue<ChunkMeshletKey>
        _fluidPublicationOrder = new();
    private readonly Dictionary<ChunkMeshletKey, FluidPublication>
        _fluidPublications = [];

    private ulong _appliedSelectionRevision;
    private readonly PackContentRegistry<ObjectDefinition> _objects;
    private readonly BiomeTintField _objectTints;
    private readonly WorldObjectVisualCatalog _objectVisuals;
    private readonly DeduplicatedQueue<ChunkCoord> _pendingObjects = new();
    private readonly HashSet<ChunkCoord> _deferredCollisionChunks = [];
    private double _collisionActivationMilliseconds;
    private int _collisionEmergencyShapes;

    public int DeferredCollisionChunkCount => _deferredCollisionChunks.Count;

    public int TakeCollisionEmergencyShapes()
    {
        var count = _collisionEmergencyShapes;
        _collisionEmergencyShapes = 0;
        return count;
    }

    private void TrackCollisionActivation(ChunkPresentation presentation, bool enabled)
    {
        var stats = presentation.SetPhysicsEnabled(enabled);
        _collisionActivationMilliseconds += stats.Milliseconds;
        _collisionEmergencyShapes += stats.EmergencyShapes;
    }

    public double TakeCollisionActivationMilliseconds()
    {
        var elapsed = _collisionActivationMilliseconds;
        _collisionActivationMilliseconds = 0d;
        return elapsed;
    }

    public int PrewarmCollisionShapes(
        ChunkResidencyRuntime residency,
        int maximumShapes,
        WorldFrameWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(residency);
        if (maximumShapes <= 0 || _deferredCollisionChunks.Count == 0)
            return 0;

        // The global deadline may already be spent by generation/residency.
        // Guarantee one nearby shape progresses each frame; otherwise the
        // first collision-radius activation must build all remaining shapes
        // synchronously. More work respects both the local 1ms budget and
        // the shared frame deadline.
        var now = Stopwatch.GetTimestamp();
        var work = FrameWorkBudget.Begin(
            TimeSpan.FromMilliseconds(1),
            minimumItems: 1,
            nowTimestamp: now,
            timestampFrequency: Stopwatch.Frequency,
            maximumItems: maximumShapes,
            globalDeadlineTimestamp: budget.DeadlineTimestamp);

        // Only player-near resident presentations participate. This ordering
        // is independent of the HashSet's iteration order.
        var nearby = _deferredCollisionChunks
            .Where(coord => residency.ShouldPrewarmPresentationCollision(coord))
            .OrderBy(coord => ChunkStreamingState.Priority(
                coord, residency.Center, residency.MovementDirection))
            .ToArray();

        foreach (var coord in nearby)
        {
            if (work.Exhausted(Stopwatch.GetTimestamp()))
                break;

            if (!_presentations.TryGetValue(coord, out var presentation))
            {
                _deferredCollisionChunks.Remove(coord);
                continue;
            }

            // Exhaust the nearest chunk's deferred shapes first, up to the
            // strict per-frame shape cap. Previously each candidate received
            // only one shape, even when no other nearby work existed.
            while (!work.Exhausted(Stopwatch.GetTimestamp()) &&
                   presentation.PrewarmOneCollision())
            {
                work.Record();
            }

            if (!presentation.HasDeferredCollisions)
                _deferredCollisionChunks.Remove(coord);
        }

        return work.ProcessedItems;
    }

    public ChunkPresentationController(
        Node3D parent,
        VoxelWorld world,
        WorldUpdateQueue worldUpdates,
        FluidMeshUpdateQueue fluidMeshUpdates,
        MeshletContentRevisions terrainRevisions,
        MeshletContentRevisions fluidRevisions,
        VoxelTerrainMaterialSet terrainMaterials,
        FluidMaterialCatalog fluidMaterials,
        PackContentRegistry<ObjectDefinition> objects,
        PackSelection packSelection,
        BiomeTintField objectTints)
    {
        _parent =
            parent ??
            throw new ArgumentNullException(nameof(parent));
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _worldUpdates =
            worldUpdates ??
            throw new ArgumentNullException(nameof(worldUpdates));
        _fluidMeshUpdates =
            fluidMeshUpdates ??
            throw new ArgumentNullException(nameof(fluidMeshUpdates));
        _terrainRevisions =
            terrainRevisions ??
            throw new ArgumentNullException(nameof(terrainRevisions));
        _fluidRevisions =
            fluidRevisions ??
            throw new ArgumentNullException(nameof(fluidRevisions));
        _terrainMaterials =
            terrainMaterials ??
            throw new ArgumentNullException(nameof(terrainMaterials));
        _fluidMaterials =
            fluidMaterials ??
            throw new ArgumentNullException(nameof(fluidMaterials));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _objectTints = objectTints ??
            throw new ArgumentNullException(nameof(objectTints));
        _objectVisuals = new WorldObjectVisualCatalog(packSelection);
        _world.ObjectChunkChanged += OnObjectChunkChanged;
    }

    private void OnObjectChunkChanged(ChunkCoord coord)
    {
        if (_presentations.ContainsKey(coord))
            _pendingObjects.Enqueue(coord);
    }

    private const int MaximumObjectLightingUpdatesPerSlice = 128;

    public int IntegrateObjectChanges(int maximumPerFrame,
        WorldFrameWorkBudget budget)
    {
        if (maximumPerFrame <= 0 || _pendingObjects.Count == 0)
            return 0;

        var work = FrameWorkBudget.Begin(
            TimeSpan.FromMilliseconds(1),
            minimumItems: 1,
            nowTimestamp: Stopwatch.GetTimestamp(),
            timestampFrequency: Stopwatch.Frequency,
            maximumItems: maximumPerFrame,
            globalDeadlineTimestamp: budget.DeadlineTimestamp);
        var processed = 0;
        while (processed < maximumPerFrame &&
               _pendingObjects.TryDequeue(out var coord))
        {
            if (processed > 0 && work.Exhausted(Stopwatch.GetTimestamp()))
            {
                _pendingObjects.EnqueueFront(coord);
                break;
            }

            var complete = true;
            if (_presentations.TryGetValue(coord, out var presentation) &&
                _world.TryGetChunk(coord, out var chunk))
            {
                complete = presentation.ApplyObjects(chunk, _world,
                    _objects, _objectVisuals, _objectTints,
                    MaximumObjectLightingUpdatesPerSlice);
            }

            processed++;
            work.Record();
            if (!complete)
            {
                // Continue the same chunk on a later frame. Do not immediately
                // dequeue it again and accidentally consume the entire budget
                // rebuilding one large instance-light buffer.
                _pendingObjects.Enqueue(coord);
                break;
            }
        }
        return processed;
    }

    public void Dispose()
    {
        _world.ObjectChunkChanged -= OnObjectChunkChanged;
        _pendingObjects.Clear();
        _deferredCollisionChunks.Clear();
        _pendingTerrainMeshFlush.Clear();
        _pendingFluidMeshFlush.Clear();
        _warmPresentations.Clear(static presentation =>
            presentation.DisposeSuspended());
    }

    public int WarmPresentationCount => _warmPresentations.Count;

    public (int Reused, int Evicted, int TerrainMeshletsReused,
        int FluidMeshletsReused) TakeWarmPresentationStats()
    {
        var stats = (_warmReuses, _warmEvictions,
            _warmTerrainMeshletsReused, _warmFluidMeshletsReused);
        _warmReuses = 0;
        _warmEvictions = 0;
        _warmTerrainMeshletsReused = 0;
        _warmFluidMeshletsReused = 0;
        return stats;
    }

    public int Count => _presentations.Count;

    public IEnumerable<ChunkCoord> Coordinates =>
        _presentations.Keys;

    public int PendingTerrainPublications =>
        _terrainPublications.Count;

    public int PendingFluidPublications =>
        _fluidPublications.Count;

    public bool Contains(ChunkCoord coord) =>
        _presentations.ContainsKey(coord);

    public bool IsFullyPublished(ChunkCoord coord) =>
        _presentations.TryGetValue(
            coord,
            out var presentation) &&
        presentation.IsFullyPublished;

    /// <summary>
    /// Count only the chunks in the current render selection, excluding
    /// retained presentations outside the visible area. The residency
    /// runtime remains the sole owner of the desired selection.
    /// </summary>
    public int CountFullyPublishedDesired(ChunkResidencyRuntime residency)
    {
        ArgumentNullException.ThrowIfNull(residency);
        var count = 0;
        foreach (var (coord, presentation) in _presentations)
        {
            if (residency.IsDesired(coord) && presentation.IsFullyPublished)
                count++;
        }
        return count;
    }

    public int PendingObjectChanges => _pendingObjects.Count;

    public bool HasTerrainPublicationsFor(Func<ChunkCoord, bool> includesChunk)
    {
        ArgumentNullException.ThrowIfNull(includesChunk);
        return _terrainPublications.Keys.Any(key => includesChunk(key.Chunk));
    }

    public bool HasFluidPublicationsFor(Func<ChunkCoord, bool> includesChunk)
    {
        ArgumentNullException.ThrowIfNull(includesChunk);
        return _fluidPublications.Keys.Any(key => includesChunk(key.Chunk));
    }

    public bool HasPendingFluidMeshFlushFor(Func<ChunkCoord, bool> includesChunk)
    {
        ArgumentNullException.ThrowIfNull(includesChunk);
        return _pendingFluidMeshFlush.Any(includesChunk);
    }

    public bool HasObjectPresentationWorkFor(Func<ChunkCoord, bool> includesChunk)
    {
        ArgumentNullException.ThrowIfNull(includesChunk);
        return _pendingObjects.Any(includesChunk);
    }

    public IReadOnlyDictionary<ChunkMeshletKey, ChunkFluidMeshData>
        CaptureRelightableFluid(WorldMeshBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var captured = new Dictionary<ChunkMeshletKey, ChunkFluidMeshData>();
        foreach (var (coord, mask) in batch.DirtyMeshlets)
        {
            if (!_presentations.TryGetValue(coord, out var presentation))
                continue;
            foreach (var index in mask.Indices())
            {
                if (presentation.TryGetRelightableFluid(
                        index, _world, out var data) && data is not null)
                    captured.Add(new ChunkMeshletKey(coord, index), data);
            }
        }
        return captured;
    }

    public IReadOnlyDictionary<ChunkMeshletKey, ChunkMeshData>
        CaptureRelightableTerrain(WorldMeshBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var captured = new Dictionary<ChunkMeshletKey, ChunkMeshData>();
        foreach (var (coord, mask) in batch.DirtyMeshlets)
        {
            if (!_presentations.TryGetValue(coord, out var presentation))
                continue;
            foreach (var index in mask.Indices())
            {
                if (presentation.TryGetRelightableGeometry(
                        index, _world, out var data) && data is not null)
                    captured.Add(new ChunkMeshletKey(coord, index), data);
            }
        }
        return captured;
    }

    public bool NeedsPriorityInitialTerrainPublication(
        ChunkCoord coord,
        int meshletIndex) =>
        _presentations.TryGetValue(coord, out var presentation) &&
        presentation.IsVisible &&
        !presentation.IsTerrainMeshletPublished(meshletIndex);

    public int ReservePending(
        ChunkResidencyRuntime residency,
        int maximumPerFrame,
        WorldFrameWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(residency);

        if (maximumPerFrame <= 0)
        {
            return 0;
        }

        var reserved = 0;
        var processed = 0;

        while (processed < maximumPerFrame)
        {
            if (processed > 0 &&
                budget.Exhausted(
                    Stopwatch.GetTimestamp()))
            {
                break;
            }

            var coord =
                residency.PopPresentationByPriority();

            if (coord is null)
            {
                break;
            }

            processed++;

            if (!_world.TryGetChunk(
                    coord.Value,
                    out var chunk) ||
                _presentations.ContainsKey(
                    coord.Value))
            {
                continue;
            }

            var warmed = _warmPresentations.TryTake(
                coord.Value, out var presentation);
            presentation ??= new ChunkPresentation(coord.Value);
            if (warmed)
                _warmReuses++;

            // Reactivate the same GPU resources immediately. The current
            // terrain/fluid work below is still queued because residency,
            // nearby contents and lighting may have changed in the meantime.
            _parent.AddChild(presentation.Root);
            presentation.SetVisible(
                residency.ShouldPresentationBeVisible(
                    coord.Value,
                    currentlyVisible: false));
            _presentations.Add(
                coord.Value,
                presentation);
            if (warmed || chunk.ObjectCount != 0)
                _pendingObjects.Enqueue(coord.Value);

            var terrainMeshlets = chunk.OccupiedTerrainMeshlets();
            var fluidMeshlets = chunk.OccupiedFluidMeshlets();
            var reusableTerrain = warmed
                ? presentation.ReusableTerrainMeshlets(_world, terrainMeshlets)
                : ChunkMeshletMask.None;
            var reusableFluid = warmed
                ? presentation.ReusableFluidMeshlets(_world, fluidMeshlets)
                : ChunkMeshletMask.None;

            if (warmed)
            {
                // Remove geometry that no longer matches its actual captured
                // 3x3x3 dependency footprint. Unchanged meshlets retain their
                // existing Godot resources, physics shapes and publication.
                presentation.DiscardInvalidCachedMeshlets(
                    ChunkMeshletMask.All.Except(reusableTerrain),
                    ChunkMeshletMask.All.Except(reusableFluid));
                if (presentation.HasPendingTerrainMeshFlush)
                    _pendingTerrainMeshFlush.Enqueue(coord.Value);
                if (presentation.HasPendingFluidMeshFlush)
                    _pendingFluidMeshFlush.Enqueue(coord.Value);
                _warmTerrainMeshletsReused += reusableTerrain.Indices().Count();
                _warmFluidMeshletsReused += reusableFluid.Indices().Count();
                if (presentation.HasDeferredCollisions)
                    _deferredCollisionChunks.Add(coord.Value);
            }

            if (presentation.HasPendingTerrainMeshFlush)
                _pendingTerrainMeshFlush.Enqueue(coord.Value);
            if (presentation.HasPendingFluidMeshFlush)
                _pendingFluidMeshFlush.Enqueue(coord.Value);

            presentation.MarkTerrainPublished(
                ChunkMeshletMask.All.Except(terrainMeshlets));
            var terrainToBuild = terrainMeshlets.Except(reusableTerrain);
            if (!terrainToBuild.IsEmpty)
            {
                if (presentation.IsVisible)
                    _worldUpdates.EnqueuePriorityMeshlets(
                        coord.Value, terrainToBuild);
                else
                    _worldUpdates.EnqueueMeshlets(
                        coord.Value, terrainToBuild);
            }

            var fluidToBuild = fluidMeshlets.Except(reusableFluid);
            if (!fluidToBuild.IsEmpty)
                _fluidMeshUpdates.EnqueueMeshlets(coord.Value, fluidToBuild);

            TrackCollisionActivation(presentation,
                presentation.IsVisible &&
                residency.ShouldPresentationHaveCollision(coord.Value));
            InvalidatePresentedNeighborsForAddition(
                coord.Value);
            reserved++;
        }

        return reserved;
    }

    public void SyncVisibility(
        ChunkResidencyRuntime residency)
    {
        ArgumentNullException.ThrowIfNull(residency);

        if (_appliedSelectionRevision ==
            residency.PresentationSelectionRevision)
        {
            return;
        }

        List<(ChunkCoord Coord, ChunkMeshletMask Missing)>? promoted = null;
        foreach (var (coord, presentation) in
                 _presentations)
        {
            var wasVisible = presentation.IsVisible;
            presentation.SetVisible(
                residency.ShouldPresentationBeVisible(
                    coord,
                    wasVisible));
            if (!wasVisible && presentation.IsVisible)
            {
                promoted ??= [];
                promoted.Add((coord, presentation.UnpublishedTerrainMeshlets));
            }
            TrackCollisionActivation(presentation, 
                presentation.IsVisible &&
                residency.ShouldPresentationHaveCollision(coord));
            if (!presentation.HasDeferredCollisions)
                _deferredCollisionChunks.Remove(coord);
        }

        if (promoted is not null)
        {
            // Queue order must not depend on Dictionary iteration order.
            foreach (var (coord, missing) in promoted.OrderBy(
                         entry => entry.Coord, ChunkCoordOrdering.YThenZThenX))
            {
                _worldUpdates.PromoteBackgroundMeshlets(coord, missing);
                PromoteQueuedInitialTerrainPublications(coord, missing);
            }
        }

        _appliedSelectionRevision =
            residency.PresentationSelectionRevision;
    }

    public bool Retire(ChunkCoord coord, bool archived)
    {
        RemovePendingPublications(coord);
        _pendingObjects.Remove(coord);
        _deferredCollisionChunks.Remove(coord);
        _pendingTerrainMeshFlush.Remove(coord);
        _pendingFluidMeshFlush.Remove(coord);

        if (!_presentations.Remove(coord, out var presentation))
            return false;

        if (archived && presentation.HasReusableVisualResources)
        {
            presentation.Suspend();
            _parent.RemoveChild(presentation.Root);
            _warmPresentations.Store(coord, presentation, evicted =>
            {
                _warmEvictions++;
                evicted.DisposeSuspended();
            });
        }
        else
        {
            // No authoritative archived chunk can back this presentation.
            presentation.Retire();
        }

        InvalidatePresentedNeighbors(coord);
        return true;
    }

    public void EnqueueTerrainPublication(
        TerrainMeshletBuild meshlet,
        ulong contentRevision,
        MeshDependencyStamp dependencies) =>
        EnqueueTerrainPublication(
            meshlet,
            contentRevision,
            dependencies,
            priority: false);

    public void EnqueuePriorityTerrainPublication(
        TerrainMeshletBuild meshlet,
        ulong contentRevision,
        MeshDependencyStamp dependencies) =>
        EnqueueTerrainPublication(
            meshlet,
            contentRevision,
            dependencies,
            priority: true);

    public void EnqueueFluidPublication(
        FluidMeshletBuild meshlet,
        ulong contentRevision,
        MeshDependencyStamp dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var key =
            new ChunkMeshletKey(
                meshlet.Coord,
                meshlet.MeshletIndex);

        _fluidPublications[key] =
            new FluidPublication(
                meshlet,
                contentRevision,
                dependencies);
        _fluidPublicationOrder.Enqueue(key);
    }

    public ChunkPresentationIntegrationStats
        IntegrateTerrainPublications(
            int maximumPerFrame,
            WorldFrameWorkBudget budget)
    {
        var stopwatch = Stopwatch.StartNew();
        var published = 0;
        var stale = 0;
        var processed = 0;
        var renderBuildMilliseconds = 0d;
        var collisionBuildMilliseconds = 0d;
        long renderVertices = 0;
        long collisionTriangles = 0;
        var gpuAttributePatches = 0;

        while (processed < maximumPerFrame &&
               TryDequeueTerrainPublication(
                   out var key,
                   out var priority))
        {
            if (processed > 0 &&
                budget.Exhausted(
                    Stopwatch.GetTimestamp()))
            {
                RequeueTerrainPublicationFront(
                    key,
                    priority);
                break;
            }

            processed++;

            if (!_terrainPublications.Remove(
                    key,
                    out var pending))
            {
                continue;
            }

            if (!_world.ContainsChunk(
                    pending.Meshlet.Coord) ||
                !_presentations.TryGetValue(
                    pending.Meshlet.Coord,
                    out var presentation))
            {
                continue;
            }

            if (!_terrainRevisions.IsCurrent(
                    key,
                    pending.ContentRevision) ||
                !pending.Dependencies.IsCurrent(
                    _world))
            {
                var mask =
                    ChunkMeshletMask.Single(
                        pending.Meshlet.MeshletIndex);

                if (priority)
                {
                    _worldUpdates.EnqueuePriorityMeshlets(
                        pending.Meshlet.Coord,
                        mask);
                }
                else
                {
                    _worldUpdates.EnqueueMeshlets(
                        pending.Meshlet.Coord,
                        mask);
                }

                stale++;
                continue;
            }

            var apply = presentation.Apply(
                pending.Meshlet.MeshletIndex,
                pending.Meshlet.Data,
                _terrainMaterials,
                pending.Dependencies);
            if (presentation.HasPendingTerrainMeshFlush)
                _pendingTerrainMeshFlush.Enqueue(pending.Meshlet.Coord);
            renderBuildMilliseconds += apply.RenderBuildMilliseconds;
            collisionBuildMilliseconds += apply.CollisionBuildMilliseconds;
            renderVertices += apply.RenderVertices;
            collisionTriangles += apply.CollisionTriangles;
            if (apply.PatchedGpuAttributes)
                gpuAttributePatches++;
            if (presentation.HasDeferredCollisions)
                _deferredCollisionChunks.Add(pending.Meshlet.Coord);
            else
                _deferredCollisionChunks.Remove(pending.Meshlet.Coord);
            // Object mesh resources stay stable; refresh only per-instance
            // voxel-light channels after a changed terrain-light publication.
            if (_world.TryGetChunk(pending.Meshlet.Coord, out var updated) &&
                updated.ObjectCount > 0)
            {
                _pendingObjects.Enqueue(pending.Meshlet.Coord);
            }
            published++;
        }

        FlushPendingTerrainMeshes(budget, ref renderBuildMilliseconds);
        stopwatch.Stop();

        return new ChunkPresentationIntegrationStats(
            published, stale, _terrainPublications.Count,
            stopwatch.Elapsed.TotalMilliseconds,
            renderBuildMilliseconds, collisionBuildMilliseconds,
            renderVertices, collisionTriangles,
            gpuAttributePatches);
    }

    private void FlushPendingTerrainMeshes(
        WorldFrameWorkBudget budget,
        ref double renderBuildMilliseconds)
    {
        // Process at most the queue's initial population. Re-enqueued chunks
        // return on the next frame, so one complex chunk cannot monopolize
        // this frame just because it is the only item currently queued.
        var candidates = _pendingTerrainMeshFlush.Count;
        var processed = 0;
        while (processed < candidates &&
               _pendingTerrainMeshFlush.TryDequeue(out var coord))
        {
            if (processed > 0 &&
                budget.Exhausted(Stopwatch.GetTimestamp()))
            {
                _pendingTerrainMeshFlush.EnqueueFront(coord);
                break;
            }

            processed++;
            if (!_presentations.TryGetValue(coord, out var presentation))
                continue;

            renderBuildMilliseconds += presentation.FlushTerrainMesh(
                _terrainMaterials, maximumSurfaceUpdates: 1);

            if (presentation.HasPendingTerrainMeshFlush)
                _pendingTerrainMeshFlush.Enqueue(coord);
        }
    }

    private void FlushPendingFluidMeshes(
        WorldFrameWorkBudget budget,
        ref double fluidMeshBuildMilliseconds)
    {
        var candidates = _pendingFluidMeshFlush.Count;
        var processed = 0;
        while (processed < candidates &&
               _pendingFluidMeshFlush.TryDequeue(out var coord))
        {
            if (processed > 0 &&
                budget.Exhausted(Stopwatch.GetTimestamp()))
            {
                _pendingFluidMeshFlush.EnqueueFront(coord);
                break;
            }

            processed++;
            if (!_presentations.TryGetValue(coord, out var presentation))
                continue;

            fluidMeshBuildMilliseconds += presentation.FlushFluidMesh(
                _fluidMaterials, maximumSurfaceUpdates: 1);
            if (presentation.HasPendingFluidMeshFlush)
                _pendingFluidMeshFlush.Enqueue(coord);
        }
    }

    public ChunkPresentationIntegrationStats
        IntegrateFluidPublications(
            int maximumPerFrame,
            WorldFrameWorkBudget budget)
    {
        var stopwatch = Stopwatch.StartNew();
        var published = 0;
        var stale = 0;
        var processed = 0;
        var gpuAttributePatches = 0;
        var fluidMeshBuildMilliseconds = 0d;

        while (processed < maximumPerFrame &&
               _fluidPublicationOrder.TryDequeue(
                   out var key))
        {
            if (processed > 0 &&
                budget.Exhausted(
                    Stopwatch.GetTimestamp()))
            {
                _fluidPublicationOrder.EnqueueFront(key);
                break;
            }

            processed++;

            if (!_fluidPublications.Remove(
                    key,
                    out var pending))
            {
                continue;
            }

            if (!_world.ContainsChunk(
                    pending.Meshlet.Coord) ||
                !_presentations.TryGetValue(
                    pending.Meshlet.Coord,
                    out var presentation))
            {
                continue;
            }

            if (!_fluidRevisions.IsCurrent(
                    key,
                    pending.ContentRevision) ||
                !pending.Dependencies.IsCurrent(
                    _world))
            {
                _fluidMeshUpdates.EnqueueMeshlets(
                    pending.Meshlet.Coord,
                    ChunkMeshletMask.Single(
                        pending.Meshlet.MeshletIndex));
                stale++;
                continue;
            }

            var patched = presentation.ApplyFluid(
                pending.Meshlet.MeshletIndex,
                pending.Meshlet.Data,
                _fluidMaterials,
                pending.Dependencies);
            if (presentation.HasPendingFluidMeshFlush)
                _pendingFluidMeshFlush.Enqueue(pending.Meshlet.Coord);
            if (patched)
                gpuAttributePatches++;
            published++;
        }

        FlushPendingFluidMeshes(budget, ref fluidMeshBuildMilliseconds);
        stopwatch.Stop();

        return new ChunkPresentationIntegrationStats(
            published, stale, _fluidPublications.Count,
            stopwatch.Elapsed.TotalMilliseconds,
            RenderBuildMilliseconds: fluidMeshBuildMilliseconds,
            GpuAttributePatches: gpuAttributePatches);
    }

    private void PromoteQueuedInitialTerrainPublications(
        ChunkCoord coord,
        ChunkMeshletMask missing)
    {
        // Publication DTOs can already be waiting in the background lane,
        // after their worker finished. Move their queue entries without
        // rebuilding the mesh or copying the result.
        foreach (var meshletIndex in missing.Indices())
        {
            var key = new ChunkMeshletKey(coord, meshletIndex);
            if (_terrainPublications.ContainsKey(key) &&
                _backgroundTerrainPublicationOrder.Remove(key))
                _priorityTerrainPublicationOrder.Enqueue(key);
        }
    }

    private void EnqueueTerrainPublication(
        TerrainMeshletBuild meshlet,
        ulong contentRevision,
        MeshDependencyStamp dependencies,
        bool priority)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var key =
            new ChunkMeshletKey(
                meshlet.Coord,
                meshlet.MeshletIndex);

        if (!priority &&
            _priorityTerrainPublicationOrder.Contains(key))
        {
            return;
        }

        _terrainPublications[key] =
            new TerrainPublication(
                meshlet,
                contentRevision,
                dependencies);

        if (priority)
        {
            _backgroundTerrainPublicationOrder.Remove(key);
            _priorityTerrainPublicationOrder.Enqueue(key);
        }
        else
        {
            _backgroundTerrainPublicationOrder.Enqueue(key);
        }
    }

    private bool TryDequeueTerrainPublication(
        out ChunkMeshletKey key,
        out bool priority)
    {
        if (_priorityTerrainPublicationOrder.TryDequeue(
                out key))
        {
            priority = true;
            return true;
        }

        if (_backgroundTerrainPublicationOrder.TryDequeue(
                out key))
        {
            priority = false;
            return true;
        }

        key = default;
        priority = false;
        return false;
    }

    private void RequeueTerrainPublicationFront(
        ChunkMeshletKey key,
        bool priority)
    {
        if (priority)
        {
            _priorityTerrainPublicationOrder.EnqueueFront(
                key);
        }
        else
        {
            _backgroundTerrainPublicationOrder.EnqueueFront(
                key);
        }
    }

    private void InvalidatePresentedNeighborsForAddition(
        ChunkCoord coord)
    {
        foreach (var invalidation in
                 ChunkTopologyFrontier
                     .PresentedNeighborMeshInvalidationsForAddition(
                         coord,
                         _world,
                         Contains))
        {
            var neighbor = _presentations[invalidation.Neighbor];
            var terrain = neighbor.TerrainMeshletsNeedingRemesh(
                _world, invalidation.Terrain);
            if (!terrain.IsEmpty)
            {
                _terrainRevisions.Bump(invalidation.Neighbor, terrain);
                _worldUpdates.EnqueueMeshlets(invalidation.Neighbor, terrain);
            }

            var fluid = neighbor.FluidMeshletsNeedingRemesh(
                _world, invalidation.Fluid);
            if (!fluid.IsEmpty)
            {
                _fluidRevisions.Bump(invalidation.Neighbor, fluid);
                _fluidMeshUpdates.EnqueueMeshlets(invalidation.Neighbor, fluid);
            }
        }
    }

    private void InvalidatePresentedNeighbors(
        ChunkCoord coord)
    {
        foreach (var invalidation in
                 ChunkTopologyFrontier
                     .PresentedNeighborMeshInvalidationsForRetirement(
                         coord,
                         _world,
                         Contains))
        {
            var neighbor = _presentations[invalidation.Neighbor];
            var terrain = neighbor.TerrainMeshletsNeedingRemesh(
                _world, invalidation.Terrain);
            if (!terrain.IsEmpty)
            {
                _terrainRevisions.Bump(invalidation.Neighbor, terrain);
                _worldUpdates.EnqueueMeshlets(invalidation.Neighbor, terrain);
            }

            var fluid = neighbor.FluidMeshletsNeedingRemesh(
                _world, invalidation.Fluid);
            if (!fluid.IsEmpty)
            {
                _fluidRevisions.Bump(invalidation.Neighbor, fluid);
                _fluidMeshUpdates.EnqueueMeshlets(invalidation.Neighbor, fluid);
            }
        }
    }

    private void RemovePendingPublications(
        ChunkCoord coord)
    {
        for (var meshletIndex = 0;
             meshletIndex < ChunkMeshletMask.Count;
             meshletIndex++)
        {
            var key =
                new ChunkMeshletKey(
                    coord,
                    meshletIndex);

            _terrainPublications.Remove(key);
            _priorityTerrainPublicationOrder.Remove(key);
            _backgroundTerrainPublicationOrder.Remove(key);
            _fluidPublications.Remove(key);
            _fluidPublicationOrder.Remove(key);
        }
    }

    private sealed record TerrainPublication(
        TerrainMeshletBuild Meshlet,
        ulong ContentRevision,
        MeshDependencyStamp Dependencies);

    private sealed record FluidPublication(
        FluidMeshletBuild Meshlet,
        ulong ContentRevision,
        MeshDependencyStamp Dependencies);
}
