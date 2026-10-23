using System.Diagnostics;
using Asteria.Core.World;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Rendering;

public readonly record struct ChunkPresentationApplyStats(
    double RenderBuildMilliseconds,
    double CollisionBuildMilliseconds,
    int RenderVertices,
    int CollisionTriangles,
    bool PatchedGpuAttributes = false);

public readonly record struct CollisionActivationStats(double Milliseconds, int EmergencyShapes);

public sealed class ChunkPresentation
{
    // Store at most 2048 CPU-side light-refresh vertices per presented chunk.
    // Large fine/sprite meshes remain correct through ordinary remeshing;
    // this cache cannot grow unchecked with render distance or complexity.
    private const int MaximumRelightCacheVerticesPerChunk = 2048;
    private int _relightCacheVertexCount;

    // Terrain is merged by render batch (material/shadow policy) within a
    // chunk. Per-meshlet ranges preserve local edits and partial light uploads.
    private sealed class TerrainSurfaceState
    {
        public List<ChunkMeshVertex> Vertices { get; } = [];
        public int SurfaceIndex { get; set; }
        public bool HasSurface { get; set; }
    }

    private readonly record struct TerrainVertexSpan(int Start, int Count);

    private readonly Dictionary<TerrainRenderBatch, TerrainSurfaceState>
        _terrainSurfaceStates = [];
    private readonly DeduplicatedQueue<TerrainRenderBatch> _terrainDirtyBatches = new();
    private readonly Dictionary<TerrainRenderBatch, TerrainVertexSpan>[]
        _terrainMeshletSpans = CreateTerrainMeshletSpans();
    private MeshInstance3D? _terrainMeshInstance;
    private ArrayMesh? _terrainMesh;
    private bool _terrainMeshDirty;
    private MeshInstance3D? _fluidMeshInstance;
    private ArrayMesh? _fluidMesh;
    private readonly int[] _fluidSurfaceStarts =
        new int[ChunkMeshletMask.Count];
    private readonly int[] _fluidSurfaceCounts =
        new int[ChunkMeshletMask.Count];
    // A separate bounded immutable cache for small published fluid meshlets.
    private const int MaximumFluidRelightVerticesPerChunk = 2048;
    private readonly ChunkFluidMeshData?[] _relightableFluid =
        new ChunkFluidMeshData?[ChunkMeshletMask.Count];
    private readonly MeshDependencyStamp?[] _relightableFluidDependencies =
        new MeshDependencyStamp?[ChunkMeshletMask.Count];
    private int _fluidRelightVertexCount;
    private readonly CollisionShape3D?[] _collisions =
        new CollisionShape3D?[ChunkMeshletMask.Count];
    private readonly MeshDependencyStamp?[] _collisionDependencies =
        new MeshDependencyStamp?[ChunkMeshletMask.Count];
    // All published meshlets retain a compact dependency stamp even when
    // their vertex buffers are too large for the separate relight cache.
    private readonly MeshDependencyStamp?[] _publishedTerrainDependencies =
        new MeshDependencyStamp?[ChunkMeshletMask.Count];
    private readonly MeshDependencyStamp?[] _publishedFluidDependencies =
        new MeshDependencyStamp?[ChunkMeshletMask.Count];
    // Derived geometry cache is bounded to relightable presented meshlets.
    // It is dropped with the presentation on residency retirement.
    private readonly ChunkMeshData?[] _relightableGeometry =
        new ChunkMeshData?[ChunkMeshletMask.Count];
    private readonly MeshDependencyStamp?[] _relightableDependencies =
        new MeshDependencyStamp?[ChunkMeshletMask.Count];
    // Only unpublished physics faces are held; they are replaced on remesh
    // and released on proximity prewarm, activation or presentation retirement.
    private readonly DeferredCollision?[] _deferredCollisions =
        new DeferredCollision?[ChunkMeshletMask.Count];
    private readonly bool[] _published =
        new bool[ChunkMeshletMask.Count];

    private StaticBody3D? _collisionBody;
    private int _publishedCount;
    private bool _physicsEnabled;
    private ulong _objectRevision = ulong.MaxValue;
    private readonly Dictionary<string, MultiMeshInstance3D> _objectBatches =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorldVoxelCoord[]> _objectLightPositions =
        new(StringComparer.Ordinal);
    private string[] _objectLightRefreshGroups = [];
    private int _objectLightRefreshGroupIndex;
    private int _objectLightRefreshInstanceIndex;
    private bool _objectLightRefreshInvalidated;
    private ObjectPresentationBuildState? _objectPresentationBuild;

    private readonly record struct ObjectBuildSource(
        int X, int Y, int Z, ObjectCell Cell);

    private readonly record struct ObjectBuildInstance(
        Transform3D Transform,
        System.Numerics.Vector3 Color,
        WorldVoxelCoord LightPosition);

    private sealed class ObjectPresentationBuildState(
        ulong revision,
        ObjectBuildSource[] sources)
    {
        public ulong Revision { get; } = revision;
        public ObjectBuildSource[] Sources { get; } = sources;
        public int NextSourceIndex { get; set; }
        public SortedDictionary<string, List<ObjectBuildInstance>> Groups { get; } =
            new(StringComparer.Ordinal);
        public BiomeTintSampleGrid? TintSamples { get; set; }
        public bool GroupsPrepared { get; set; }
        public string[] GroupIds { get; set; } = [];
        public int GroupIndex { get; set; }
        public int InstanceIndex { get; set; }
        public MultiMesh? CurrentMultiMesh { get; set; }
        public MultiMeshInstance3D? CurrentNode { get; set; }
        public ObjectDefinition? CurrentDefinition { get; set; }
        public Dictionary<string, MultiMeshInstance3D> Replacements { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, WorldVoxelCoord[]> SamplePositions { get; } =
            new(StringComparer.Ordinal);
    }

    public ChunkPresentation(ChunkCoord coord)
    {
        Coord = coord;
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);

        Root = new Node3D
        {
            Name = $"Chunk_{coord.X}_{coord.Y}_{coord.Z}",
            Position = new Vector3(
                originX,
                originY,
                originZ),
        };
    }

    public ChunkCoord Coord { get; }

    public Node3D Root { get; }

    public bool IsVisible => Root.Visible;

    public bool HasDeferredCollisions =>
        _deferredCollisions.Any(pending => pending is not null);

    public bool IsFullyPublished =>
        _publishedCount == ChunkMeshletMask.Count && !_terrainMeshDirty;

    public bool HasPendingTerrainMeshFlush => _terrainMeshDirty;

    // A wholly empty chunk has no GPU state worth keeping in the warm cache.
    public bool HasReusableVisualResources =>
        (_terrainMesh?.GetSurfaceCount() ?? 0) > 0 ||
        (_fluidMesh?.GetSurfaceCount() ?? 0) > 0 ||
        _objectBatches.Count != 0;

    // Empty meshlets are marked published when a chunk is reserved, so this
    // only includes real geometry that still lacks its initial publication.
    // A topology event is only a candidate invalidation. Presentation
    // membership may change even when the already-published geometry is
    // structurally identical to the current resident 3x3x3 footprint.
    public ChunkMeshletMask TerrainMeshletsNeedingRemesh(
        VoxelWorld world, ChunkMeshletMask candidates)
    {
        byte bits = 0;
        foreach (var index in candidates.Indices())
            if (_publishedTerrainDependencies[index] is not { } dependency ||
                !dependency.HasUnchangedGeometryAfterRestore(world))
                bits |= (byte)(1 << index);
        return new ChunkMeshletMask(bits);
    }

    public ChunkMeshletMask FluidMeshletsNeedingRemesh(
        VoxelWorld world, ChunkMeshletMask candidates)
    {
        byte bits = 0;
        foreach (var index in candidates.Indices())
            if (_publishedFluidDependencies[index] is not { } dependency ||
                !dependency.HasUnchangedGeometryAfterRestore(world))
                bits |= (byte)(1 << index);
        return new ChunkMeshletMask(bits);
    }

    public ChunkMeshletMask ReusableTerrainMeshlets(
        VoxelWorld world, ChunkMeshletMask occupied)
    {
        byte bits = 0;
        foreach (var index in occupied.Indices())
            if (_published[index] &&
                _publishedTerrainDependencies[index] is { } dependency &&
                dependency.HasUnchangedGeometryAfterRestore(world))
                bits |= (byte)(1 << index);
        return new ChunkMeshletMask(bits);
    }

    public ChunkMeshletMask ReusableFluidMeshlets(
        VoxelWorld world, ChunkMeshletMask occupied)
    {
        byte bits = 0;
        foreach (var index in occupied.Indices())
            if (_publishedFluidDependencies[index] is { } dependency &&
                dependency.HasUnchangedGeometryAfterRestore(world))
                bits |= (byte)(1 << index);
        return new ChunkMeshletMask(bits);
    }

    public void DiscardInvalidCachedMeshlets(
        ChunkMeshletMask terrain, ChunkMeshletMask fluid)
    {
        foreach (var index in terrain.Indices())
        {
            RemoveTerrainSurfaces(index);
            RemoveCollision(index);
            _collisionDependencies[index] = null;
            _deferredCollisions[index] = null;
            _publishedTerrainDependencies[index] = null;
            _relightCacheVertexCount -=
                _relightableGeometry[index]?.RenderVertexCount ?? 0;
            _relightableGeometry[index] = null;
            _relightableDependencies[index] = null;
            if (_published[index])
            {
                _published[index] = false;
                _publishedCount--;
            }
        }

        foreach (var index in fluid.Indices())
        {
            RemoveFluidSurfaces(index);
            _publishedFluidDependencies[index] = null;
            _fluidRelightVertexCount -=
                _relightableFluid[index]?.Batches.Sum(
                    batch => batch.Vertices.Length) ?? 0;
            _relightableFluid[index] = null;
            _relightableFluidDependencies[index] = null;
        }
    }

    public ChunkMeshletMask UnpublishedTerrainMeshlets
    {
        get
        {
            byte bits = 0;
            for (var index = 0; index < _published.Length; index++)
            {
                if (!_published[index])
                    bits |= (byte)(1 << index);
            }
            return new ChunkMeshletMask(bits);
        }
    }

    public bool IsTerrainMeshletPublished(int index)
    {
        ValidateMeshletIndex(index);
        return _published[index];
    }

    public bool TryGetRelightableGeometry(
        int meshletIndex, VoxelWorld world, out ChunkMeshData? data)
    {
        ValidateMeshletIndex(meshletIndex);
        ArgumentNullException.ThrowIfNull(world);

        data = _relightableGeometry[meshletIndex];
        if (data is not { CanRelightWithoutRemesh: true } ||
            _terrainMeshletSpans[meshletIndex].Count == 0 ||
            _relightableDependencies[meshletIndex] is not { } dependency ||
            !dependency.HasUnchangedGeometryAfterRestore(world))
        {
            data = null;
            return false;
        }

        return true;
    }

    public bool TryGetRelightableFluid(
        int meshletIndex, VoxelWorld world, out ChunkFluidMeshData? data)
    {
        ValidateMeshletIndex(meshletIndex);
        ArgumentNullException.ThrowIfNull(world);
        data = _relightableFluid[meshletIndex];
        if (data is null || _fluidSurfaceCounts[meshletIndex] == 0 ||
            _relightableFluidDependencies[meshletIndex] is not { } dependency ||
            !dependency.HasUnchangedGeometryAfterRestore(world))
        {
            data = null;
            return false;
        }
        return true;
    }

    public void SetVisible(bool visible) =>
        Root.Visible = visible;

    public CollisionActivationStats SetPhysicsEnabled(bool enabled)
    {
        if (_physicsEnabled == enabled)
            return default;

        var started = Stopwatch.GetTimestamp();
        _physicsEnabled = enabled;
        var emergencyShapes = 0;
        if (enabled)
        {
            // Preserve collision correctness on an unprewarmed activation.
            while (PrewarmOneCollision())
                emergencyShapes++;
        }

        foreach (var collision in _collisions)
            if (collision is not null)
                collision.Disabled = !enabled;

        return new CollisionActivationStats(
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            emergencyShapes);
    }

    public bool PrewarmOneCollision()
    {
        for (var index = 0; index < _deferredCollisions.Length; index++)
        {
            if (_deferredCollisions[index] is not { } pending)
                continue;

            var collision = _collisions[index] ?? CreateCollision(index);
            collision.Shape = ChunkMeshBuilder.CreateCollisionShape(pending.Faces);
            _collisionDependencies[index] = pending.Dependencies;
            _deferredCollisions[index] = null;
            return true;
        }
        return false;
    }

    /// <summary>One MultiMesh per object definition per chunk, never one
    /// Godot node per ObjectCell. ObjectRevision gates the entire rebuild.</summary>
    internal void RequestObjectLightingRefresh() =>
        _objectLightRefreshInvalidated = true;

    internal bool ApplyObjects(
        Chunk chunk, VoxelWorld world,
        PackContentRegistry<ObjectDefinition> definitions,
        WorldObjectVisualCatalog visuals, BiomeTintField tints,
        int maximumObjectsPerSlice)
    {
        if (maximumObjectsPerSlice <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumObjectsPerSlice));

        if (_objectRevision == chunk.ObjectRevision)
        {
            if (_objectLightRefreshInvalidated)
            {
                ResetObjectLightingRefresh();
                _objectLightRefreshInvalidated = false;
            }
            return RefreshObjectLighting(world, maximumObjectsPerSlice);
        }

        if (_objectPresentationBuild is null ||
            _objectPresentationBuild.Revision != chunk.ObjectRevision)
            StartObjectPresentationBuild(chunk);

        var build = _objectPresentationBuild ??
            throw new InvalidOperationException("Object presentation build was not initialized.");
        var processed = 0;

        // Prepare object transforms and tint data in bounded slices. Keep the
        // previous presentation alive until the complete replacement is ready.
        while (build.NextSourceIndex < build.Sources.Length &&
               processed < maximumObjectsPerSlice)
        {
            var source = build.Sources[build.NextSourceIndex++];
            processed++;

            if (!definitions.TryGet(source.Cell.Id, out var definition) ||
                definition is null || !definition.Supports(source.Cell.Face))
                continue;

            var worldX = Coord.X * Chunk.Size + source.X;
            var worldY = Coord.Y * Chunk.Size + source.Y;
            var worldZ = Coord.Z * Chunk.Size + source.Z;
            var pose = WorldObjectGeometry.Pose(
                new WorldVoxelCoord(worldX, worldY, worldZ),
                source.Cell, definition);
            var rotation = new Quaternion(
                pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
            var scale = new Vector3(pose.Scale.X, pose.Scale.Y, pose.Scale.Z);
            var position = new Vector3(
                pose.Position.X, pose.Position.Y, pose.Position.Z);
            var transform = new Transform3D(
                new Basis(rotation).Scaled(scale), position - new Vector3(
                    Root.Position.X, Root.Position.Y, Root.Position.Z));
            var tint = System.Numerics.Vector3.One;
            if (definition.Tint != BlockTint.None)
            {
                build.TintSamples ??= tints.SampleChunkGrid(Coord.X, Coord.Z);
                tint = build.TintSamples.Resolve(
                    definition.Tint, definition.TintFallback, worldX, worldZ);
            }

            if (!build.Groups.TryGetValue(definition.Id, out var instances))
                build.Groups.Add(definition.Id, instances = []);
            instances.Add(new ObjectBuildInstance(
                transform, tint,
                WorldObjectLighting.SamplePosition(
                    new WorldVoxelCoord(worldX, worldY, worldZ), source.Cell.Face)));
        }

        if (build.NextSourceIndex < build.Sources.Length)
            return false;

        if (!build.GroupsPrepared)
        {
            build.GroupIds = build.Groups.Keys.ToArray();
            build.GroupsPrepared = true;
        }

        // Allocate each MultiMesh independently, then fill its instance buffer
        // in bounded slices rather than synchronously writing every object in
        // the chunk on the main thread.
        while (build.GroupIndex < build.GroupIds.Length &&
               processed < maximumObjectsPerSlice)
        {
            var id = build.GroupIds[build.GroupIndex];
            var instances = build.Groups[id];
            if (build.CurrentMultiMesh is null)
            {
                var definition = definitions.Get(id);
                var tinted = definition.Tint != BlockTint.None;
                build.CurrentMultiMesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = tinted,
                    UseCustomData = !definition.Unlit,
                    Mesh = visuals.GetMesh(definition),
                    InstanceCount = instances.Count,
                };
                build.CurrentDefinition = definition;
                build.CurrentNode = new MultiMeshInstance3D
                {
                    Name = $"WorldObjects_{id.Replace(':', '_')}",
                    Multimesh = build.CurrentMultiMesh,
                    CastShadow = definition.CastsShadow
                        ? GeometryInstance3D.ShadowCastingSetting.On
                        : GeometryInstance3D.ShadowCastingSetting.Off,
                };
                if (!definition.Unlit)
                    build.SamplePositions.Add(id,
                        instances.Select(instance => instance.LightPosition).ToArray());
            }

            var currentDefinition = build.CurrentDefinition ??
                throw new InvalidOperationException("Object presentation group lost its definition.");
            var multiMesh = build.CurrentMultiMesh;
            while (build.InstanceIndex < instances.Count &&
                   processed < maximumObjectsPerSlice)
            {
                var index = build.InstanceIndex;
                var instance = instances[index];
                multiMesh.SetInstanceTransform(index, instance.Transform);
                if (currentDefinition.Tint != BlockTint.None)
                {
                    var tint = instance.Color;
                    multiMesh.SetInstanceColor(index,
                        new Color(tint.X, tint.Y, tint.Z));
                }
                if (!currentDefinition.Unlit)
                    multiMesh.SetInstanceCustomData(index,
                        WorldObjectLighting.ToInstanceColor(
                            world.GetLightOrDark(instance.LightPosition)));
                build.InstanceIndex++;
                processed++;
            }

            if (build.InstanceIndex >= instances.Count)
            {
                build.Replacements.Add(
                    id,
                    build.CurrentNode ??
                    throw new InvalidOperationException("Object presentation node was not initialized."));
                build.CurrentNode = null;
                build.CurrentMultiMesh = null;
                build.CurrentDefinition = null;
                build.GroupIndex++;
                build.InstanceIndex = 0;
            }
        }

        if (build.GroupIndex < build.GroupIds.Length)
            return false;

        // Swap only after all data has been prepared, preserving the previous
        // rendered objects while the new presentation is built across frames.
        foreach (var current in _objectBatches.Values)
            current.QueueFree();
        _objectBatches.Clear();
        _objectLightPositions.Clear();
        foreach (var (id, samples) in build.SamplePositions)
            _objectLightPositions.Add(id, samples);
        foreach (var (id, instance) in build.Replacements)
        {
            _objectBatches.Add(id, instance);
            Root.AddChild(instance);
        }

        _objectRevision = build.Revision;
        _objectPresentationBuild = null;
        ResetObjectLightingRefresh();
        // If lighting changed while this build was in progress, queue one
        // bounded refresh pass before marking the presentation current.
        return !_objectLightRefreshInvalidated;
    }

    private void StartObjectPresentationBuild(Chunk chunk)
    {
        CancelObjectPresentationBuild();
        var sources = new List<ObjectBuildSource>(chunk.ObjectCount);
        chunk.VisitObjectCells((x, y, z, cell) =>
            sources.Add(new ObjectBuildSource(x, y, z, cell)));
        _objectPresentationBuild = new ObjectPresentationBuildState(
            chunk.ObjectRevision, sources.ToArray());
        ResetObjectLightingRefresh();
    }

    private void CancelObjectPresentationBuild()
    {
        if (_objectPresentationBuild is not { } build)
            return;

        build.CurrentNode?.Free();
        foreach (var replacement in build.Replacements.Values)
            replacement.Free();
        _objectPresentationBuild = null;
    }

    private bool RefreshObjectLighting(
        VoxelWorld world, int maximumUpdates)
    {
        if (_objectLightRefreshGroups.Length == 0)
        {
            _objectLightRefreshGroups = _objectLightPositions.Keys
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            _objectLightRefreshGroupIndex = 0;
            _objectLightRefreshInstanceIndex = 0;
        }

        var processed = 0;
        while (_objectLightRefreshGroupIndex < _objectLightRefreshGroups.Length &&
               processed < maximumUpdates)
        {
            var id = _objectLightRefreshGroups[_objectLightRefreshGroupIndex];
            if (!_objectBatches.TryGetValue(id, out var batch) ||
                !_objectLightPositions.TryGetValue(id, out var samples))
            {
                _objectLightRefreshGroupIndex++;
                _objectLightRefreshInstanceIndex = 0;
                continue;
            }

            var multiMesh = batch.Multimesh;
            while (_objectLightRefreshInstanceIndex < samples.Length &&
                   processed < maximumUpdates)
            {
                var index = _objectLightRefreshInstanceIndex++;
                multiMesh.SetInstanceCustomData(index,
                    WorldObjectLighting.ToInstanceColor(
                        world.GetLightOrDark(samples[index])));
                processed++;
            }

            if (_objectLightRefreshInstanceIndex >= samples.Length)
            {
                _objectLightRefreshGroupIndex++;
                _objectLightRefreshInstanceIndex = 0;
            }
        }

        if (_objectLightRefreshGroupIndex < _objectLightRefreshGroups.Length)
            return false;

        ResetObjectLightingRefresh();
        return true;
    }

    private void ResetObjectLightingRefresh()
    {
        _objectLightRefreshGroups = [];
        _objectLightRefreshGroupIndex = 0;
        _objectLightRefreshInstanceIndex = 0;
    }

    // Only the Godot presentation is suspended. The authoritative chunk is
    // already owned by VoxelWorld's archive, not by this display object.
    public void Suspend()
    {
        CancelObjectPresentationBuild();
        ResetObjectLightingRefresh();
        _objectLightRefreshInvalidated = true;
        SetPhysicsEnabled(false);
        Root.Visible = false;
    }

    // Suspended roots are detached from SceneTree and therefore must be
    // explicitly released on cache eviction or dimension retirement.
    public void DisposeSuspended()
    {
        CancelObjectPresentationBuild();
        Root.Free();
    }

    public void Retire()
    {
        CancelObjectPresentationBuild();
        Root.QueueFree();
    }

    public void MarkTerrainPublished(
        ChunkMeshletMask meshlets)
    {
        foreach (var meshletIndex in
                 meshlets.Indices())
        {
            MarkTerrainPublished(
                meshletIndex);
        }
    }

    public bool ApplyFluid(
        int meshletIndex,
        ChunkFluidMeshData data,
        FluidMaterialCatalog materials,
        MeshDependencyStamp dependencies,
        bool lightOnly = false)
    {
        ValidateMeshletIndex(meshletIndex);
        ArgumentNullException.ThrowIfNull(dependencies);

        _fluidRelightVertexCount -=
            _relightableFluid[meshletIndex]?.Batches.Sum(
                batch => batch.Vertices.Length) ?? 0;
        _relightableFluid[meshletIndex] = null;
        _relightableFluidDependencies[meshletIndex] = null;
        _publishedFluidDependencies[meshletIndex] = dependencies;

        var expectedSurfaces = data.Batches.Count(
            batch => batch.Vertices.Length > 0);
        var patched = lightOnly &&
            _fluidMesh is { } published &&
            _fluidSurfaceCounts[meshletIndex] == expectedSurfaces &&
            ChunkFluidMeshBuilder.TryUpdateLighting(
                published, data,
                _fluidSurfaceStarts[meshletIndex],
                _fluidSurfaceCounts[meshletIndex]);

        if (!patched)
        {
            RemoveFluidSurfaces(meshletIndex);
            if (data.HasGeometry)
            {
                EnsureFluidMesh();
                var start = _fluidMesh!.GetSurfaceCount();
                var appendedSurfaces = ChunkFluidMeshBuilder.AppendSurfaces(
                    _fluidMesh, data, materials);
                if (appendedSurfaces > 0)
                {
                    _fluidSurfaceStarts[meshletIndex] = start;
                    _fluidSurfaceCounts[meshletIndex] = appendedSurfaces;
                }
            }
        }

        var count = data.Batches.Sum(batch => batch.Vertices.Length);
        if (count <= MaximumFluidRelightVerticesPerChunk -
                     _fluidRelightVertexCount)
        {
            _relightableFluid[meshletIndex] = data;
            _relightableFluidDependencies[meshletIndex] = dependencies;
            _fluidRelightVertexCount += count;
        }
        return patched;
    }

    public ChunkPresentationApplyStats Apply(
        int meshletIndex,
        ChunkMeshData data,
        VoxelTerrainMaterialSet materials,
        MeshDependencyStamp dependencies,
        bool lightOnly = false)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ValidateMeshletIndex(
            meshletIndex);

        // The cached geometry was validated before worker capture and the
        // worker dependencies are checked again before publication. Relighting
        // its vertices cannot change terrain physics or deferred collision.
        var preserveCollision = lightOnly &&
            _relightableGeometry[meshletIndex] is not null;

        var renderStart = Stopwatch.GetTimestamp();
        var patchedGpuAttributes = false;
        var lightOnlyHandled = lightOnly &&
            TryUpdateTerrainLightOnly(
                meshletIndex, data, out patchedGpuAttributes);
        if (!lightOnlyHandled)
            ReplaceTerrainMeshletGeometry(meshletIndex, data.RenderBatches);
        var renderMilliseconds = Stopwatch.GetElapsedTime(
            renderStart).TotalMilliseconds;

        // Replacing an already-published geometry snapshot invalidates its
        // old cached light bindings. New cache is immutable and bounded to
        // this presentation's lifetime.
        _relightCacheVertexCount -=
            _relightableGeometry[meshletIndex]?.RenderVertexCount ?? 0;
        var retainForRelighting =
            data.CanRelightWithoutRemesh && data.HasRenderGeometry &&
            data.RenderVertexCount <=
                MaximumRelightCacheVerticesPerChunk - _relightCacheVertexCount;
        _relightableGeometry[meshletIndex] =
            retainForRelighting ? data : null;
        if (retainForRelighting)
            _relightCacheVertexCount += data.RenderVertexCount;
        _relightableDependencies[meshletIndex] =
            retainForRelighting ? dependencies : null;

        var collisionStart = Stopwatch.GetTimestamp();

        if (preserveCollision)
        {
            // No shape creation, destruction, or physics-body mutation.
        }
        else if (data.HasCollision)
        {
            var geometryIsCurrent =
                _collisions[meshletIndex] is not null &&
                _collisionDependencies[meshletIndex] is { } previous &&
                previous.HasSameWorldGeometryInputsAs(dependencies);

            if (geometryIsCurrent)
            {
                _deferredCollisions[meshletIndex] = null;
            }
            else if (_physicsEnabled)
            {
                var collision = _collisions[meshletIndex] ??
                    CreateCollision(meshletIndex);
                collision.Shape = ChunkMeshBuilder.CreateCollisionShape(data);
                _collisionDependencies[meshletIndex] = dependencies;
                _deferredCollisions[meshletIndex] = null;
            }
            else
            {
                _deferredCollisions[meshletIndex] =
                    new DeferredCollision(data.CollisionFaces, dependencies);
            }
        }
        else
        {
            _deferredCollisions[meshletIndex] = null;
            RemoveCollision(meshletIndex);
            _collisionDependencies[meshletIndex] = null;
        }

        var collisionMilliseconds = Stopwatch.GetElapsedTime(
            collisionStart).TotalMilliseconds;
        _publishedTerrainDependencies[meshletIndex] = dependencies;
        MarkTerrainPublished(meshletIndex);
        return new ChunkPresentationApplyStats(
            renderMilliseconds,
            collisionMilliseconds,
            data.RenderVertexCount,
            data.CollisionTriangleCount,
            patchedGpuAttributes);
    }

    private sealed record DeferredCollision(
        System.Numerics.Vector3[] Faces,
        MeshDependencyStamp Dependencies);

    private static Dictionary<TerrainRenderBatch, TerrainVertexSpan>[]
        CreateTerrainMeshletSpans()
    {
        var maps = new Dictionary<TerrainRenderBatch, TerrainVertexSpan>[
            ChunkMeshletMask.Count];
        for (var index = 0; index < maps.Length; index++)
            maps[index] = [];
        return maps;
    }

    private bool TryUpdateTerrainLightOnly(
        int meshletIndex,
        ChunkMeshData data,
        out bool patchedGpuAttributes)
    {
        patchedGpuAttributes = false;
        var spans = _terrainMeshletSpans[meshletIndex];
        var batches = data.RenderBatches
            .Where(batch => batch.Vertices.Length > 0)
            .ToArray();

        if (spans.Count != batches.Length)
            return false;

        foreach (var batch in batches)
        {
            if (!spans.TryGetValue(batch.Batch, out var span) ||
                span.Count != batch.Vertices.Length ||
                !_terrainSurfaceStates.ContainsKey(batch.Batch))
                return false;
        }

        // Retain per-material CPU ranges. If another structural publication
        // dirtied the chunk mesh this frame, one merged rebuild will include
        // these light changes without a separate native upload.
        foreach (var batch in batches)
        {
            var span = spans[batch.Batch];
            var state = _terrainSurfaceStates[batch.Batch];
            for (var index = 0; index < batch.Vertices.Length; index++)
                state.Vertices[span.Start + index] = batch.Vertices[index];
        }

        if (_terrainMeshDirty || _terrainMesh is null)
        {
            foreach (var batch in batches)
                _terrainDirtyBatches.Enqueue(batch.Batch);
            _terrainMeshDirty = _terrainDirtyBatches.Count > 0;
            return true;
        }

        foreach (var batch in batches)
        {
            var span = spans[batch.Batch];
            var state = _terrainSurfaceStates[batch.Batch];
            if (ChunkMeshBuilder.TryUpdateLightingRange(
                    _terrainMesh, state.SurfaceIndex, span.Start,
                    state.Vertices.Count, batch))
                continue;

            // A format mismatch must not leave partially patched output.
            // Queue this material surface for a bounded replacement on the
            // presentation path; already-patched batch ranges remain current.
            _terrainDirtyBatches.Enqueue(batch.Batch);
            _terrainMeshDirty = true;
            return true;
        }

        patchedGpuAttributes = batches.Length > 0;
        return true;
    }

    private void ReplaceTerrainMeshletGeometry(
        int meshletIndex,
        IReadOnlyList<ChunkRenderBatchData> batches)
    {
        RemoveTerrainMeshletGeometry(meshletIndex);
        var spans = _terrainMeshletSpans[meshletIndex];

        foreach (var batch in batches)
        {
            if (batch.Vertices.Length == 0)
                continue;

            if (!_terrainSurfaceStates.TryGetValue(batch.Batch, out var state))
            {
                state = new TerrainSurfaceState();
                _terrainSurfaceStates.Add(batch.Batch, state);
            }

            var start = state.Vertices.Count;
            state.Vertices.AddRange(batch.Vertices);
            spans.Add(
                batch.Batch,
                new TerrainVertexSpan(start, batch.Vertices.Length));
            _terrainDirtyBatches.Enqueue(batch.Batch);
            _terrainMeshDirty = true;
        }
    }

    private void RemoveTerrainMeshletGeometry(int meshletIndex)
    {
        var spans = _terrainMeshletSpans[meshletIndex];
        if (spans.Count == 0)
            return;

        foreach (var (batch, span) in
                 spans.OrderByDescending(entry => entry.Value.Start))
        {
            if (!_terrainSurfaceStates.TryGetValue(batch, out var state))
                continue;

            state.Vertices.RemoveRange(span.Start, span.Count);
            _terrainDirtyBatches.Enqueue(batch);
            for (var otherIndex = 0;
                 otherIndex < _terrainMeshletSpans.Length;
                 otherIndex++)
            {
                if (otherIndex == meshletIndex ||
                    !_terrainMeshletSpans[otherIndex].TryGetValue(
                        batch, out var otherSpan))
                    continue;

                if (otherSpan.Start > span.Start)
                    _terrainMeshletSpans[otherIndex][batch] =
                        otherSpan with { Start = otherSpan.Start - span.Count };
            }

        }

        spans.Clear();
        _terrainMeshDirty = _terrainDirtyBatches.Count > 0;
    }

    /// <summary>
    /// Rebuilds one surface per render batch after a publication slice, rather
    /// than one surface per meshlet. The CPU ranges preserve meshlet-local
    /// ownership for subsequent edits and partial light uploads.
    /// </summary>
    private void EnsureTerrainMesh()
    {
        if (_terrainMesh is not null)
            return;

        _terrainMesh = new ArrayMesh();
        _terrainMeshInstance = new MeshInstance3D
        {
            Name = "Terrain",
            Mesh = _terrainMesh,
        };
        Root.AddChild(_terrainMeshInstance);
    }

    /// <summary>
    /// Publishes a bounded number of dirty material surfaces. The caller
    /// coordinates this slice with the shared frame deadline and other chunks.
    /// </summary>
    public double FlushTerrainMesh(
        VoxelTerrainMaterialSet materials,
        int maximumSurfaceUpdates = 1)
    {
        if (maximumSurfaceUpdates <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSurfaceUpdates));
        if (!_terrainMeshDirty)
            return 0d;

        var stopwatch = Stopwatch.StartNew();
        var processed = 0;
        while (processed < maximumSurfaceUpdates &&
               _terrainDirtyBatches.TryDequeue(out var batch))
        {
            if (!_terrainSurfaceStates.TryGetValue(batch, out var state))
            {
                processed++;
                continue;
            }

            if (state.HasSurface)
            {
                var removedSurface = state.SurfaceIndex;
                _terrainMesh!.SurfaceRemove(removedSurface);
                foreach (var other in _terrainSurfaceStates.Values)
                {
                    if (other.HasSurface && other.SurfaceIndex > removedSurface)
                        other.SurfaceIndex--;
                }
                state.HasSurface = false;
            }

            if (state.Vertices.Count == 0)
            {
                _terrainSurfaceStates.Remove(batch);
                processed++;
                continue;
            }

            EnsureTerrainMesh();
            var surfaceIndex = _terrainMesh!.GetSurfaceCount();
            var data = new ChunkMeshData(
                [new ChunkRenderBatchData(batch, state.Vertices.ToArray())],
                Array.Empty<System.Numerics.Vector3>());
            if (ChunkMeshBuilder.AppendSurfaces(_terrainMesh, data, materials) > 0)
            {
                state.SurfaceIndex = surfaceIndex;
                state.HasSurface = true;
            }

            processed++;
        }

        _terrainMeshDirty = _terrainDirtyBatches.Count > 0;
        if (!_terrainMeshDirty &&
            _terrainMesh is { } mesh &&
            mesh.GetSurfaceCount() == 0)
        {
            _terrainMeshInstance?.QueueFree();
            _terrainMeshInstance = null;
            _terrainMesh = null;
        }

        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private void EnsureFluidMesh()
    {
        if (_fluidMesh is not null)
            return;

        _fluidMesh = new ArrayMesh();
        _fluidMeshInstance = new MeshInstance3D
        {
            Name = "Fluids",
            Mesh = _fluidMesh,
        };
        Root.AddChild(_fluidMeshInstance);
    }

    private void RemoveTerrainSurfaces(int meshletIndex) =>
        RemoveTerrainMeshletGeometry(meshletIndex);

    private void RemoveFluidSurfaces(int meshletIndex) =>
        RemoveSharedMeshletSurfaces(
            ref _fluidMesh,
            ref _fluidMeshInstance,
            _fluidSurfaceStarts,
            _fluidSurfaceCounts,
            meshletIndex);

    private static void RemoveSharedMeshletSurfaces(
        ref ArrayMesh? mesh,
        ref MeshInstance3D? instance,
        int[] starts,
        int[] counts,
        int meshletIndex)
    {
        var count = counts[meshletIndex];
        if (count == 0)
        {
            starts[meshletIndex] = 0;
            return;
        }

        var start = starts[meshletIndex];
        var end = start + count;
        if (mesh is { } shared)
        {
            for (var surface = end - 1; surface >= start; surface--)
                shared.SurfaceRemove(surface);
        }

        for (var index = 0; index < counts.Length; index++)
        {
            if (index != meshletIndex && counts[index] > 0 &&
                starts[index] >= end)
                starts[index] -= count;
        }

        starts[meshletIndex] = 0;
        counts[meshletIndex] = 0;
        if (mesh is not { } remaining || remaining.GetSurfaceCount() != 0)
            return;

        instance?.QueueFree();
        instance = null;
        mesh = null;
        Array.Clear(starts);
        Array.Clear(counts);
    }

    private CollisionShape3D CreateCollision(
        int meshletIndex)
    {
        _collisionBody ??=
            CreateCollisionBody();

        var collision =
            new CollisionShape3D
            {
                Name =
                    $"Meshlet_{meshletIndex}",
                Disabled =
                    !_physicsEnabled,
            };

        _collisions[
            meshletIndex] =
            collision;
        _collisionBody.AddChild(
            collision);
        return collision;
    }

    private StaticBody3D CreateCollisionBody()
    {
        var body =
            new StaticBody3D
            {
                Name = "Collision",
            };
        Root.AddChild(
            body);
        return body;
    }

    private void RemoveCollision(
        int meshletIndex)
    {
        var collision =
            _collisions[
                meshletIndex];

        if (collision is null)
        {
            return;
        }

        _collisions[
            meshletIndex] =
            null;
        collision.QueueFree();

        if (_collisions.Any(
                value =>
                    value is not null))
        {
            return;
        }

        _collisionBody?.QueueFree();
        _collisionBody = null;
    }

    private void MarkTerrainPublished(
        int meshletIndex)
    {
        if (_published[
                meshletIndex])
        {
            return;
        }

        _published[
            meshletIndex] = true;
        _publishedCount++;
    }

    private static void ValidateMeshletIndex(
        int meshletIndex)
    {
        if ((uint)meshletIndex >=
            ChunkMeshletMask.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(meshletIndex));
        }
    }
}
