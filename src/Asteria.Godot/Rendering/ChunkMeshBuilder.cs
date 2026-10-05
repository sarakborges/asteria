using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public static class ChunkMeshBuilder
{
    // Godot treats clockwise triangle winding as the front face.
    private static readonly int[] TriangleOrder = [0, 2, 1, 0, 3, 2];

    public static ArrayMesh Build(Chunk chunk, BlockRegistry blocks)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        for (var y = 0; y < Chunk.Size; y++)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var cell = chunk.GetCell(x, y, z);
                    if (cell.IsEmpty)
                    {
                        continue;
                    }

                    var definition = blocks.GetDefinition(cell.Block);
                    var origin = new Vector3(x, y, z);
                    var color = GetDebugColor(definition.Id);

                    if (FaceIsExposed(chunk, blocks, x + 1, y, z))
                        AddFace(surface, origin, Vector3.Right, color, FacePositiveX);
                    if (FaceIsExposed(chunk, blocks, x - 1, y, z))
                        AddFace(surface, origin, Vector3.Left, color, FaceNegativeX);
                    if (FaceIsExposed(chunk, blocks, x, y + 1, z))
                        AddFace(surface, origin, Vector3.Up, color, FacePositiveY);
                    if (FaceIsExposed(chunk, blocks, x, y - 1, z))
                        AddFace(surface, origin, Vector3.Down, color, FaceNegativeY);
                    if (FaceIsExposed(chunk, blocks, x, y, z + 1))
                        AddFace(surface, origin, Vector3.Back, color, FacePositiveZ);
                    if (FaceIsExposed(chunk, blocks, x, y, z - 1))
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

    private static bool FaceIsExposed(Chunk chunk, BlockRegistry blocks, int x, int y, int z)
    {
        var neighbor = chunk.GetCellOrEmpty(x, y, z);
        return neighbor.IsEmpty || !blocks.GetDefinition(neighbor.Block).IsOpaque;
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

    private static Color GetDebugColor(string blockId) => blockId switch
    {
        TestChunkFactory.GrassId => new Color("70b85b"),
        TestChunkFactory.DirtId => new Color("8a6547"),
        TestChunkFactory.StoneId => new Color("8b9199"),
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
