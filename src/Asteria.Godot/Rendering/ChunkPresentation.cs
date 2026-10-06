using Asteria.Core.World;
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
    private readonly bool[] _published =
        new bool[ChunkMeshletMask.Count];

    private StaticBody3D? _collisionBody;
    private int _publishedCount;
    private bool _physicsEnabled = true;

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

    public void SetVisible(bool visible)
    {
        Root.Visible = visible;

        if (_physicsEnabled ==
            visible)
        {
            return;
        }

        _physicsEnabled =
            visible;

        foreach (var collision in
                 _collisions)
        {
            if (collision is not null)
            {
                collision.Disabled =
                    !visible;
            }
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

    public void Apply(
        int meshletIndex,
        ChunkMeshData data,
        VoxelTerrainMaterialSet materials)
    {
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

            collision.Shape =
                ChunkMeshBuilder
                    .CreateCollisionShape(
                        data);
        }
        else
        {
            RemoveCollision(
                meshletIndex);
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
