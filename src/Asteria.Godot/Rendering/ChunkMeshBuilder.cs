using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public static class ChunkMeshBuilder
{
    private static readonly int[] TriangleOrder = [0, 1, 2, 0, 2, 3];

    public static ArrayMesh Build(Chunk chunk)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        for (var y = 0; y < Chunk.SizeY; y++)
        {
            for (var z = 0; z < Chunk.SizeZ; z++)
            {
                for (var x = 0; x < Chunk.SizeX; x++)
                {
                    var block = chunk.GetBlock(x, y, z);
                    if (block == BlockId.Air)
                    {
                        continue;
                    }

                    var origin = new Vector3(x, y, z);
                    var color = GetBlockColor(block);

                    if (chunk.GetBlockOrAir(x + 1, y, z) == BlockId.Air)
                        AddFace(surface, origin, Vector3.Right, color, FacePositiveX);
                    if (chunk.GetBlockOrAir(x - 1, y, z) == BlockId.Air)
                        AddFace(surface, origin, Vector3.Left, color, FaceNegativeX);
                    if (chunk.GetBlockOrAir(x, y + 1, z) == BlockId.Air)
                        AddFace(surface, origin, Vector3.Up, color, FacePositiveY);
                    if (chunk.GetBlockOrAir(x, y - 1, z) == BlockId.Air)
                        AddFace(surface, origin, Vector3.Down, color, FaceNegativeY);
                    if (chunk.GetBlockOrAir(x, y, z + 1) == BlockId.Air)
                        AddFace(surface, origin, Vector3.Back, color, FacePositiveZ);
                    if (chunk.GetBlockOrAir(x, y, z - 1) == BlockId.Air)
                        AddFace(surface, origin, Vector3.Forward, color, FaceNegativeZ);
                }
            }
        }

        var mesh = surface.Commit();
        var material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1f,
        };
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    private static void AddFace(SurfaceTool surface, Vector3 origin, Vector3 normal, Color color, Vector3[] corners)
    {
        foreach (var index in TriangleOrder)
        {
            surface.SetNormal(normal);
            surface.SetColor(color);
            surface.AddVertex(origin + corners[index]);
        }
    }

    private static Color GetBlockColor(BlockId block) => block switch
    {
        BlockId.Grass => new Color("70b85b"),
        BlockId.Dirt => new Color("8a6547"),
        BlockId.Stone => new Color("8b9199"),
        _ => Colors.Magenta,
    };

    private static readonly Vector3[] FacePositiveX =
    [
        new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1),
    ];

    private static readonly Vector3[] FaceNegativeX =
    [
        new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0),
    ];

    private static readonly Vector3[] FacePositiveY =
    [
        new(0, 1, 1), new(1, 1, 1), new(1, 1, 0), new(0, 1, 0),
    ];

    private static readonly Vector3[] FaceNegativeY =
    [
        new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1),
    ];

    private static readonly Vector3[] FacePositiveZ =
    [
        new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1),
    ];

    private static readonly Vector3[] FaceNegativeZ =
    [
        new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0),
    ];
}
