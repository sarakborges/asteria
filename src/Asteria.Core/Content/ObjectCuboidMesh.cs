using System.Numerics;

namespace Asteria.Core.Content;

/// <summary>
/// Engine-independent, bounded six-face cuboid geometry. Each face uses the
/// part's own atlas rectangle; the same mesh is rendered in world batches,
/// the placement preview and the hand/portrait.
/// </summary>
public static class ObjectCuboidMesh
{
    public static ObjectMeshVertex[] Build(ObjectVisualDefinition visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (visual.Kind != ObjectVisualKind.CuboidSet ||
            visual.Parts is not { Count: > 0 and <= 64 })
            throw new ArgumentException("Requires valid cuboidSet visual.", nameof(visual));
        var vertices = new ObjectMeshVertex[visual.Parts.Count * 36];
        var index = 0;
        foreach (var part in visual.Parts)
        {
            var x0 = part.From.X; var y0 = part.From.Y; var z0 = part.From.Z;
            var x1 = part.To.X; var y1 = part.To.Y; var z1 = part.To.Z;
            var a = new Vector2(part.Uv.X, part.Uv.W);
            var b = new Vector2(part.Uv.Z, part.Uv.W);
            var c = new Vector2(part.Uv.Z, part.Uv.Y);
            var d = new Vector2(part.Uv.X, part.Uv.Y);

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                Vector3 normal)
            {
                vertices[index++] = new(p0, normal, a);
                vertices[index++] = new(p1, normal, b);
                vertices[index++] = new(p2, normal, c);
                vertices[index++] = new(p0, normal, a);
                vertices[index++] = new(p2, normal, c);
                vertices[index++] = new(p3, normal, d);
            }

            // Face ordering, UVs and normals match MineClone's primitive
            // contract; two triangles per quad with outward winding.
            Quad(new(x0, y0, z1), new(x0, y0, z0),
                new(x0, y1, z0), new(x0, y1, z1), -Vector3.UnitX);
            Quad(new(x1, y0, z0), new(x1, y0, z1),
                new(x1, y1, z1), new(x1, y1, z0), Vector3.UnitX);
            Quad(new(x0, y1, z0), new(x1, y1, z0),
                new(x1, y1, z1), new(x0, y1, z1), Vector3.UnitY);
            Quad(new(x0, y0, z1), new(x1, y0, z1),
                new(x1, y0, z0), new(x0, y0, z0), -Vector3.UnitY);
            Quad(new(x1, y0, z1), new(x0, y0, z1),
                new(x0, y1, z1), new(x1, y1, z1), Vector3.UnitZ);
            Quad(new(x0, y0, z0), new(x1, y0, z0),
                new(x1, y1, z0), new(x0, y1, z0), -Vector3.UnitZ);
        }
        return vertices;
    }
}
