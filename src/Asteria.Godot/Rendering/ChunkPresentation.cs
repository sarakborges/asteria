using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class ChunkPresentation
{
    private readonly MeshInstance3D[] _meshes =
        new MeshInstance3D[ChunkMeshletMask.Count];
    private readonly MeshInstance3D[] _fluidMeshes =
        new MeshInstance3D[ChunkMeshletMask.Count];
    private readonly CollisionShape3D[] _collisions =
        new CollisionShape3D[ChunkMeshletMask.Count];
    private readonly bool[] _published =
        new bool[ChunkMeshletMask.Count];
    private int _publishedCount;

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

        var collisionBody = new StaticBody3D
        {
            Name = "Collision",
        };
        Root.AddChild(collisionBody);

        for (var index = 0;
             index < ChunkMeshletMask.Count;
             index++)
        {
            var mesh = new MeshInstance3D
            {
                Name = $"Meshlet_{index}",
            };
            var fluidMesh = new MeshInstance3D
            {
                Name = $"FluidMeshlet_{index}",
            };
            var collision = new CollisionShape3D
            {
                Name = $"Meshlet_{index}",
            };

            _meshes[index] = mesh;
            _fluidMeshes[index] = fluidMesh;
            _collisions[index] = collision;
            Root.AddChild(mesh);
            Root.AddChild(fluidMesh);
            collisionBody.AddChild(collision);
        }
    }

    public ChunkCoord Coord { get; }

    public Node3D Root { get; }

    public bool IsVisible => Root.Visible;

    public bool IsFullyPublished =>
        _publishedCount == ChunkMeshletMask.Count;

    public void SetVisible(bool visible)
    {
        Root.Visible = visible;
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
            if (_published[
                    meshletIndex])
            {
                continue;
            }

            _published[
                meshletIndex] = true;
            _publishedCount++;
        }
    }

    public void ApplyFluid(
        int meshletIndex,
        ChunkFluidMeshData data,
        FluidMaterialCatalog materials)
    {
        _fluidMeshes[meshletIndex].Mesh =
            data.HasGeometry
                ? ChunkFluidMeshBuilder.CreateMesh(
                    data,
                    materials)
                : null;
    }

    public void Apply(
        int meshletIndex,
        ChunkMeshData data,
        VoxelTerrainMaterialSet materials)
    {
        _meshes[meshletIndex].Mesh =
            data.HasRenderGeometry
                ? ChunkMeshBuilder.CreateMesh(
                    data,
                    materials)
                : null;

        _collisions[meshletIndex].Shape =
            data.HasCollision
                ? ChunkMeshBuilder.CreateCollisionShape(data)
                : null;

        if (!_published[meshletIndex])
        {
            _published[meshletIndex] = true;
            _publishedCount++;
        }
    }
}
