using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class ChunkPresentation
{
    private readonly MeshInstance3D[] _meshes =
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
            var collision = new CollisionShape3D
            {
                Name = $"Meshlet_{index}",
            };

            _meshes[index] = mesh;
            _collisions[index] = collision;
            Root.AddChild(mesh);
            collisionBody.AddChild(collision);
        }
    }

    public ChunkCoord Coord { get; }

    public Node3D Root { get; }

    public void Apply(
        int meshletIndex,
        ChunkMeshData data,
        Material material)
    {
        _meshes[meshletIndex].Mesh =
            ChunkMeshBuilder.CreateMesh(data, material);

        _collisions[meshletIndex].Shape =
            data.Vertices.Length == 0
                ? null
                : ChunkMeshBuilder.CreateCollisionShape(data);

        if (!_published[meshletIndex])
        {
            _published[meshletIndex] = true;
            _publishedCount++;
        }
    }
}
