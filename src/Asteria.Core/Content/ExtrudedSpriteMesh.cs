using System.Numerics;

namespace Asteria.Core.Content;

/// <summary>
/// Engine-independent alpha-contour extrusion. The full textured top/bottom
/// use alpha scissor; only the exposed edges of opaque texels receive walls.
/// Image decoding belongs to the presentation adapter, not Core.
/// </summary>
public readonly record struct ObjectMeshVertex(
    Vector3 Position, Vector3 Normal, Vector2 Uv);

public static class ExtrudedSpriteMesh
{
    public const int MaximumTexturePixels = 65_536;
    public const int MaximumQuads = 131_072;

    public static ObjectMeshVertex[] Build(
        ObjectVisualDefinition visual, int imageWidth, int imageHeight,
        ReadOnlySpan<byte> alpha)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (visual.Kind != ObjectVisualKind.ExtrudedSprite)
            throw new ArgumentException("Requires extrudedSprite visual.", nameof(visual));
        if (imageWidth <= 0 || imageHeight <= 0 ||
            (long)imageWidth * imageHeight > MaximumTexturePixels)
            throw new ArgumentOutOfRangeException(nameof(imageWidth),
                $"Extruded sprite image must contain 1..{MaximumTexturePixels} pixels.");
        var pixelCount = checked(imageWidth * imageHeight);
        if (alpha.Length != pixelCount)
            throw new ArgumentException("Sprite alpha mask must cover every texel.", nameof(alpha));

        var solid = new bool[pixelCount];
        var threshold = (int)MathF.Ceiling(visual.AlphaCutoff * byte.MaxValue);
        var opaqueCount = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            // An alpha-zero texel has no silhouette even at an authored cutoff of zero.
            solid[i] = alpha[i] > 0 && alpha[i] >= threshold;
            if (solid[i]) opaqueCount++;
        }
        if (opaqueCount == 0)
            throw new ArgumentException("Extruded sprite must contain at least one opaque texel.", nameof(alpha));

        bool IsSolid(int x, int y) =>
            (uint)x < imageWidth && (uint)y < imageHeight &&
            solid[y * imageWidth + x];

        var edges = 0;
        for (var y = 0; y < imageHeight; y++)
        for (var x = 0; x < imageWidth; x++)
        {
            if (!IsSolid(x, y)) continue;
            if (!IsSolid(x - 1, y)) edges++;
            if (!IsSolid(x + 1, y)) edges++;
            if (!IsSolid(x, y - 1)) edges++;
            if (!IsSolid(x, y + 1)) edges++;
        }
        var quadCount = checked(edges + 2);
        if (quadCount > MaximumQuads)
            throw new ArgumentException(
                $"Extrusion exceeds {MaximumQuads} quads.", nameof(alpha));

        var vertices = new List<ObjectMeshVertex>(quadCount * 6);
        var halfWidth = visual.Width * .5f;
        var halfDepth = visual.Depth * .5f;
        var bottom = visual.BaseOffset;
        var top = bottom + visual.Height;

        static void Quad(List<ObjectMeshVertex> result,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector3 normal, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            result.Add(new(a, normal, uvA));
            result.Add(new(b, normal, uvB));
            result.Add(new(c, normal, uvC));
            result.Add(new(a, normal, uvA));
            result.Add(new(c, normal, uvC));
            result.Add(new(d, normal, uvD));
        }

        // Texture orientation: image top row is +Z, bottom row is -Z.
        Quad(vertices,
            new(-halfWidth, top, halfDepth), new(halfWidth, top, halfDepth),
            new(halfWidth, top, -halfDepth), new(-halfWidth, top, -halfDepth),
            Vector3.UnitY, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        Quad(vertices,
            new(-halfWidth, bottom, -halfDepth), new(halfWidth, bottom, -halfDepth),
            new(halfWidth, bottom, halfDepth), new(-halfWidth, bottom, halfDepth),
            -Vector3.UnitY, new(0, 1), new(1, 1), new(1, 0), new(0, 0));

        for (var y = 0; y < imageHeight; y++)
        for (var x = 0; x < imageWidth; x++)
        {
            if (!IsSolid(x, y)) continue;
            var u0 = x / (float)imageWidth;
            var u1 = (x + 1) / (float)imageWidth;
            var v0 = y / (float)imageHeight;
            var v1 = (y + 1) / (float)imageHeight;
            var x0 = visual.Width * (u0 - .5f);
            var x1 = visual.Width * (u1 - .5f);
            var z0 = visual.Depth * (.5f - v0);
            var z1 = visual.Depth * (.5f - v1);
            var uvA = new Vector2(u0, v1);
            var uvB = new Vector2(u1, v1);
            var uvC = new Vector2(u1, v0);
            var uvD = new Vector2(u0, v0);

            if (!IsSolid(x - 1, y))
                Quad(vertices,
                    new(x0, bottom, z1), new(x0, bottom, z0),
                    new(x0, top, z0), new(x0, top, z1),
                    -Vector3.UnitX, uvA, uvB, uvC, uvD);
            if (!IsSolid(x + 1, y))
                Quad(vertices,
                    new(x1, bottom, z0), new(x1, bottom, z1),
                    new(x1, top, z1), new(x1, top, z0),
                    Vector3.UnitX, uvA, uvB, uvC, uvD);
            if (!IsSolid(x, y - 1))
                Quad(vertices,
                    new(x0, bottom, z0), new(x1, bottom, z0),
                    new(x1, top, z0), new(x0, top, z0),
                    Vector3.UnitZ, uvA, uvB, uvC, uvD);
            if (!IsSolid(x, y + 1))
                Quad(vertices,
                    new(x1, bottom, z1), new(x0, bottom, z1),
                    new(x0, top, z1), new(x1, top, z1),
                    -Vector3.UnitZ, uvA, uvB, uvC, uvD);
        }

        return vertices.ToArray();
    }
}
