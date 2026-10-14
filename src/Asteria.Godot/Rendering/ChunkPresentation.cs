using System.Diagnostics;
using Asteria.Core.World;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Rendering;

public readonly record struct ChunkPresentationApplyStats(
    double RenderBuildMilliseconds,
    double CollisionBuildMilliseconds,
    int RenderVertices,
    int CollisionTriangles);

public sealed class ChunkPresentation
{
    // Store at most 2048 CPU-side light-refresh vertices per presented chunk.
    // Large fine/sprite meshes remain correct through ordinary remeshing;
    // this cache cannot grow unchecked with render distance or complexity.
    private const int MaximumRelightCacheVerticesPerChunk = 2048;
    private int _relightCacheVertexCount;

    private readonly MeshInstance3D?[] _meshes =
        new MeshInstance3D?[ChunkMeshletMask.Count];
    private readonly MeshInstance3D?[] _fluidMeshes =
        new MeshInstance3D?[ChunkMeshletMask.Count];
    private readonly CollisionShape3D?[] _collisions =
        new CollisionShape3D?[ChunkMeshletMask.Count];
    private readonly MeshDependencyStamp?[] _collisionDependencies =
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
        _publishedCount == ChunkMeshletMask.Count;

    // Empty meshlets are marked published when a chunk is reserved, so this
    // only includes real geometry that still lacks its initial publication.
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
            _meshes[meshletIndex] is null ||
            _relightableDependencies[meshletIndex] is not { } dependency ||
            !dependency.IsCurrent(world))
        {
            data = null;
            return false;
        }

        return true;
    }

    public void SetVisible(bool visible) =>
        Root.Visible = visible;

    public double SetPhysicsEnabled(bool enabled)
    {
        if (_physicsEnabled == enabled)
            return 0d;

        var started = Stopwatch.GetTimestamp();
        _physicsEnabled = enabled;
        if (enabled)
        {
            // This is a correctness fallback if prewarming did not complete
            // before the player entered the collision radius.
            while (PrewarmOneCollision())
            {
            }
        }

        foreach (var collision in _collisions)
            if (collision is not null)
                collision.Disabled = !enabled;

        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
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
    internal void ApplyObjects(
        Chunk chunk, VoxelWorld world,
        PackContentRegistry<ObjectDefinition> definitions,
        WorldObjectVisualCatalog visuals, BiomeTintField tints)
    {
        if (_objectRevision == chunk.ObjectRevision)
        {
            RefreshObjectLighting(world);
            return;
        }
        var groups = new SortedDictionary<string, List<(Transform3D Transform,
            System.Numerics.Vector3 Color, WorldVoxelCoord LightPosition)>>(StringComparer.Ordinal);
        BiomeTintSampleGrid? tintSamples = null;
        var origin = new Vector3(Root.Position.X, Root.Position.Y, Root.Position.Z);
        chunk.VisitObjectCells((x, y, z, cell) =>
        {
            if (!definitions.TryGet(cell.Id, out var definition) ||
                definition is null || !definition.Supports(cell.Face))
                return;
            var worldX = Coord.X * Chunk.Size + x;
            var worldY = Coord.Y * Chunk.Size + y;
            var worldZ = Coord.Z * Chunk.Size + z;
            var pose = WorldObjectGeometry.Pose(
                new WorldVoxelCoord(worldX, worldY, worldZ), cell, definition);
            var rotation = new Quaternion(
                pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
            var scale = new Vector3(pose.Scale.X, pose.Scale.Y, pose.Scale.Z);
            var position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            var transform = new Transform3D(new Basis(rotation).Scaled(scale), position - origin);
            if (!groups.TryGetValue(definition.Id, out var batch))
                groups.Add(definition.Id, batch = []);
            var tint = System.Numerics.Vector3.One;
            if (definition.Tint != BlockTint.None)
            {
                tintSamples ??= tints.SampleChunkGrid(Coord.X, Coord.Z);
                tint = tintSamples.Resolve(definition.Tint, definition.TintFallback,
                    worldX, worldZ);
            }
            batch.Add((transform, tint,
                WorldObjectLighting.SamplePosition(
                    new WorldVoxelCoord(worldX, worldY, worldZ), cell.Face)));
        });

        // Resource acquisition happens before old nodes are retired; an
        // invalid authored texture cannot silently erase existing geometry.
        var replacements = new Dictionary<string, MultiMeshInstance3D>(StringComparer.Ordinal);
        var samplePositions = new Dictionary<string, WorldVoxelCoord[]>(StringComparer.Ordinal);
        foreach (var (id, instances) in groups)
        {
            var definition = definitions.Get(id);
            var mesh = visuals.GetMesh(definition);
            var tinted = definition.Tint != BlockTint.None;
            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = tinted,
                UseCustomData = !definition.Unlit,
                Mesh = mesh,
                InstanceCount = instances.Count,
            };
            for (var i = 0; i < instances.Count; i++)
            {
                multimesh.SetInstanceTransform(i, instances[i].Transform);
                if (tinted)
                {
                    var tint = instances[i].Color;
                    multimesh.SetInstanceColor(i, new Color(tint.X, tint.Y, tint.Z));
                }
                if (!definition.Unlit)
                    multimesh.SetInstanceCustomData(i,
                        WorldObjectLighting.ToInstanceColor(
                            world.GetLightOrDark(instances[i].LightPosition)));
            }
            if (!definition.Unlit)
                samplePositions.Add(id,
                    instances.Select(instance => instance.LightPosition).ToArray());
            replacements.Add(id, new MultiMeshInstance3D
            {
                Name = $"WorldObjects_{id.Replace(':', '_')}",
                Multimesh = multimesh,
                // Caster policy belongs to the batch node, receiver policy to
                // its shared per-Object surface materials.
                CastShadow = definition.CastsShadow
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        foreach (var current in _objectBatches.Values)
            current.QueueFree();
        _objectBatches.Clear();
        _objectLightPositions.Clear();
        foreach (var (id, samples) in samplePositions)
            _objectLightPositions.Add(id, samples);
        foreach (var (id, instance) in replacements)
        {
            _objectBatches.Add(id, instance);
            Root.AddChild(instance);
        }
        _objectRevision = chunk.ObjectRevision;
    }

    private void RefreshObjectLighting(VoxelWorld world)
    {
        foreach (var (id, samples) in _objectLightPositions)
        {
            var multiMesh = _objectBatches[id].Multimesh;
            for (var i = 0; i < samples.Length; i++)
                multiMesh.SetInstanceCustomData(i,
                    WorldObjectLighting.ToInstanceColor(
                        world.GetLightOrDark(samples[i])));
        }
    }

    public void Retire()
    {
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

    public void ApplyFluid(
        int meshletIndex,
        ChunkFluidMeshData data,
        FluidMaterialCatalog materials)
    {
        ValidateMeshletIndex(
            meshletIndex);

        if (!data.HasGeometry)
        {
            RemoveNode(
                _fluidMeshes,
                meshletIndex);
            return;
        }

        var mesh =
            _fluidMeshes[
                meshletIndex] ??
            CreateMesh(
                _fluidMeshes,
                meshletIndex,
                $"FluidMeshlet_{meshletIndex}");

        mesh.Mesh =
            ChunkFluidMeshBuilder.CreateMesh(
                data,
                materials);
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
        if (data.HasRenderGeometry)
        {
            var mesh =
                _meshes[
                    meshletIndex] ??
                CreateMesh(
                    _meshes,
                    meshletIndex,
                    $"Meshlet_{meshletIndex}");

            mesh.Mesh =
                ChunkMeshBuilder.CreateMesh(
                    data,
                    materials);
        }
        else
        {
            RemoveNode(
                _meshes,
                meshletIndex);
        }

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

        var renderMilliseconds = Stopwatch.GetElapsedTime(
            renderStart).TotalMilliseconds;
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
        MarkTerrainPublished(meshletIndex);
        return new ChunkPresentationApplyStats(
            renderMilliseconds,
            collisionMilliseconds,
            data.RenderVertexCount,
            data.CollisionTriangleCount);
    }

    private sealed record DeferredCollision(
        System.Numerics.Vector3[] Faces,
        MeshDependencyStamp Dependencies);

    private MeshInstance3D CreateMesh(
        MeshInstance3D?[] storage,
        int meshletIndex,
        string name)
    {
        var mesh =
            new MeshInstance3D
            {
                Name = name,
            };

        storage[
            meshletIndex] =
            mesh;
        Root.AddChild(
            mesh);
        return mesh;
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

    private static void RemoveNode(
        MeshInstance3D?[] storage,
        int meshletIndex)
    {
        var node =
            storage[
                meshletIndex];

        if (node is null)
        {
            return;
        }

        storage[
            meshletIndex] =
            null;
        node.QueueFree();
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
