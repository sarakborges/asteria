using System.Diagnostics;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public readonly record struct ChunkPresentationIntegrationStats(
    int Published,
    int Stale,
    int Remaining,
    double ElapsedMilliseconds)
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
    private readonly DeduplicatedQueue<ChunkMeshletKey>
        _priorityTerrainPublicationOrder = new();
    private readonly DeduplicatedQueue<ChunkMeshletKey>
        _backgroundTerrainPublicationOrder = new();
    private readonly Dictionary<ChunkMeshletKey, TerrainPublication>
        _terrainPublications = [];
    private readonly DeduplicatedQueue<ChunkMeshletKey>
        _fluidPublicationOrder = new();
    private readonly Dictionary<ChunkMeshletKey, FluidPublication>
        _fluidPublications = [];

    private ulong _appliedSelectionRevision;

    public ChunkPresentationController(
        Node3D parent,
        VoxelWorld world,
        WorldUpdateQueue worldUpdates,
        FluidMeshUpdateQueue fluidMeshUpdates,
        MeshletContentRevisions terrainRevisions,
        MeshletContentRevisions fluidRevisions,
        VoxelTerrainMaterialSet terrainMaterials,
        FluidMaterialCatalog fluidMaterials)
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

            var presentation =
                new ChunkPresentation(coord.Value);

            presentation.SetVisible(
                residency.ShouldPresentationBeVisible(
                    coord.Value,
                    currentlyVisible: false));
            presentation.SetPhysicsEnabled(
                presentation.IsVisible &&
                residency.ShouldPresentationHaveCollision(coord.Value));

            _presentations.Add(
                coord.Value,
                presentation);
            _parent.AddChild(presentation.Root);

            var terrainMeshlets =
                chunk.OccupiedTerrainMeshlets();
            presentation.MarkTerrainPublished(
                ChunkMeshletMask.All.Except(terrainMeshlets));
            if (!terrainMeshlets.IsEmpty)
            {
                _worldUpdates.EnqueueMeshlets(
                    coord.Value,
                    terrainMeshlets);
            }

            var fluidMeshlets =
                chunk.OccupiedFluidMeshlets();
            if (!fluidMeshlets.IsEmpty)
            {
                _fluidMeshUpdates.EnqueueMeshlets(
                    coord.Value,
                    fluidMeshlets);
            }

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

        foreach (var (coord, presentation) in
                 _presentations)
        {
            presentation.SetVisible(
                residency.ShouldPresentationBeVisible(
                    coord,
                    presentation.IsVisible));
            presentation.SetPhysicsEnabled(
                presentation.IsVisible &&
                residency.ShouldPresentationHaveCollision(coord));
        }

        _appliedSelectionRevision =
            residency.PresentationSelectionRevision;
    }

    public bool Retire(ChunkCoord coord)
    {
        RemovePendingPublications(coord);

        if (!_presentations.Remove(
                coord,
                out var presentation))
        {
            return false;
        }

        presentation.Retire();
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

            presentation.Apply(
                pending.Meshlet.MeshletIndex,
                pending.Meshlet.Data,
                _terrainMaterials,
                pending.Dependencies);
            published++;
        }

        stopwatch.Stop();

        return new ChunkPresentationIntegrationStats(
            published,
            stale,
            _terrainPublications.Count,
            stopwatch.Elapsed.TotalMilliseconds);
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

            presentation.ApplyFluid(
                pending.Meshlet.MeshletIndex,
                pending.Meshlet.Data,
                _fluidMaterials);
            published++;
        }

        stopwatch.Stop();

        return new ChunkPresentationIntegrationStats(
            published,
            stale,
            _fluidPublications.Count,
            stopwatch.Elapsed.TotalMilliseconds);
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
            if (!invalidation.Terrain.IsEmpty)
            {
                _terrainRevisions.Bump(
                    invalidation.Neighbor,
                    invalidation.Terrain);
                _worldUpdates.EnqueueMeshlets(
                    invalidation.Neighbor,
                    invalidation.Terrain);
            }

            if (!invalidation.Fluid.IsEmpty)
            {
                _fluidRevisions.Bump(
                    invalidation.Neighbor,
                    invalidation.Fluid);
                _fluidMeshUpdates.EnqueueMeshlets(
                    invalidation.Neighbor,
                    invalidation.Fluid);
            }
        }
    }

    private void InvalidatePresentedNeighbors(
        ChunkCoord coord)
    {
        foreach (var (neighbor, meshlets) in
                 ChunkTopologyFrontier
                     .PresentedNeighborMeshlets(
                         coord,
                         Contains))
        {
            _terrainRevisions.Bump(
                neighbor,
                meshlets);
            _worldUpdates.EnqueueMeshlets(
                neighbor,
                meshlets);

            _fluidRevisions.Bump(
                neighbor,
                meshlets);
            _fluidMeshUpdates.EnqueueMeshlets(
                neighbor,
                meshlets);
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
