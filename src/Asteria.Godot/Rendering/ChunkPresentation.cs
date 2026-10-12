using Asteria.Core.World;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class ChunkPresentation
{
    private readonly MeshInstance3D?[] _meshes =
        new MeshInstance3D?[ChunkMeshletMask.Count];
    private readonly MeshInstance3D?[] _fluidMeshes =
        new MeshInstance3D?[ChunkMeshletMask.Count];
    private readonly CollisionShape3D?[] _collisions =
        new CollisionShape3D?[ChunkMeshletMask.Count];
    private readonly MeshDependencyStamp?[] _collisionDependencies =
        new MeshDependencyStamp?[ChunkMeshletMask.Count];
    private readonly bool[] _published =
        new bool[ChunkMeshletMask.Count];

    private StaticBody3D? _collisionBody;
    private int _publishedCount;
    private bool _physicsEnabled;
    private ulong _objectRevision = ulong.MaxValue;
    private readonly Dictionary<string, MultiMeshInstance3D> _objectBatches =
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

    public bool IsFullyPublished =>
        _publishedCount == ChunkMeshletMask.Count;

    public void SetVisible(bool visible) =>
        Root.Visible = visible;

    public void SetPhysicsEnabled(bool enabled)
    {
        if (_physicsEnabled == enabled)
        {
            return;
        }

        _physicsEnabled = enabled;

        foreach (var collision in
                 _collisions)
        {
            if (collision is not null)
            {
                collision.Disabled =
                    !enabled;
            }
        }
    }

    /// <summary>One MultiMesh per object definition per chunk, never one
    /// Godot node per ObjectCell. ObjectRevision gates the entire rebuild.</summary>
    internal void ApplyObjects(
        Chunk chunk, PackContentRegistry<ObjectDefinition> definitions,
        WorldObjectVisualCatalog visuals, BiomeTintField tints)
    {
        if (_objectRevision == chunk.ObjectRevision) return;
        var groups = new SortedDictionary<string, List<(Transform3D Transform,
            System.Numerics.Vector3 Color)>>(StringComparer.Ordinal);
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
            batch.Add((transform, tint));
        });

        // Resource acquisition happens before old nodes are retired; an
        // invalid authored texture cannot silently erase existing geometry.
        var replacements = new Dictionary<string, MultiMeshInstance3D>(StringComparer.Ordinal);
        foreach (var (id, instances) in groups)
        {
            var definition = definitions.Get(id);
            var mesh = visuals.GetMesh(definition);
            var tinted = definition.Tint != BlockTint.None;
            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = tinted,
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
            }
            replacements.Add(id, new MultiMeshInstance3D
            {
                Name = $"WorldObjects_{id.Replace(':', '_')}",
                Multimesh = multimesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        foreach (var current in _objectBatches.Values)
            current.QueueFree();
        _objectBatches.Clear();
        foreach (var (id, instance) in replacements)
        {
            _objectBatches.Add(id, instance);
            Root.AddChild(instance);
        }
        _objectRevision = chunk.ObjectRevision;
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

    public void Apply(
        int meshletIndex,
        ChunkMeshData data,
        VoxelTerrainMaterialSet materials,
        MeshDependencyStamp dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ValidateMeshletIndex(
            meshletIndex);

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

        if (data.HasCollision)
        {
            var collision =
                _collisions[
                    meshletIndex] ??
                CreateCollision(
                    meshletIndex);

            if (_collisionDependencies[meshletIndex] is not
                    { } previous ||
                !previous.HasSameWorldGeometryInputsAs(
                    dependencies))
            {
                collision.Shape =
                    ChunkMeshBuilder
                        .CreateCollisionShape(
                            data);
                _collisionDependencies[meshletIndex] =
                    dependencies;
            }
        }
        else
        {
            RemoveCollision(meshletIndex);
            _collisionDependencies[meshletIndex] = null;
        }

        MarkTerrainPublished(
            meshletIndex);
    }

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
