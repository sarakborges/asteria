using System.Numerics;

namespace Asteria.Core.Content;

/// <summary>
/// The six canonical faces of a 64×64 humanoid skin atlas. Source GLB
/// cuboids have local [-0.5, +0.5] coordinates and cardinal normals.
/// The same mapping is used for 64, 128, 256 or 512 pixel skin atlases.
/// </summary>
public static class PlayerSkinUvMapper
{
    private readonly record struct Box(int X, int Y, int Width, int Height, int Depth);

    public static bool TryMap(
        string part, Vector3 position, Vector3 normal, out Vector2 uv) =>
        TryMapBox(part, position, normal, outer: false, out uv);

    /// <summary>
    /// Additional jacket/sleeve/trouser cuboids use the skin atlas's
    /// independent transparent outer-layer regions.
    /// </summary>
    public static bool TryMapOuter(
        string part, Vector3 position, Vector3 normal, out Vector2 uv) =>
        TryMapBox(part, position, normal, outer: true, out uv);

    private static bool TryMapBox(
        string part, Vector3 position, Vector3 normal, bool outer, out Vector2 uv)
    {
        var layout = (part, outer) switch
        {
            ("HeadMesh", false) => new Box(0, 0, 8, 8, 8),
            ("HairLayer", false) => new Box(32, 0, 8, 8, 8),
            ("BodyMesh", false) => new Box(16, 16, 8, 12, 4),
            ("RightArmMesh", false) => new Box(40, 16, 4, 12, 4),
            ("LeftArmMesh", false) => new Box(32, 48, 4, 12, 4),
            ("RightLegMesh", false) => new Box(0, 16, 4, 12, 4),
            ("LeftLegMesh", false) => new Box(16, 48, 4, 12, 4),
            ("HeadMesh", true) => new Box(32, 0, 8, 8, 8),
            ("BodyMesh", true) => new Box(16, 32, 8, 12, 4),
            ("RightArmMesh", true) => new Box(40, 32, 4, 12, 4),
            ("LeftArmMesh", true) => new Box(48, 48, 4, 12, 4),
            ("RightLegMesh", true) => new Box(0, 32, 4, 12, 4),
            ("LeftLegMesh", true) => new Box(0, 48, 4, 12, 4),
            _ => default
        };
        if (layout.Width == 0)
        {
            uv = default;
            return false;
        }

        // Rectangles match the Minecraft/MineClone skin atlas convention;
        // importers' default cuboid UVs do not select the correct rectangles.
        int x, y, width, height;
        float s, t;
        if (normal.Z > 0.5f)
        {
            (x, y, width, height) =
                (layout.X + layout.Depth, layout.Y + layout.Depth, layout.Width, layout.Height);
            (s, t) = (position.X + 0.5f, 0.5f - position.Y);
        }
        else if (normal.Z < -0.5f)
        {
            (x, y, width, height) = (
                layout.X + layout.Depth * 2 + layout.Width,
                layout.Y + layout.Depth, layout.Width, layout.Height);
            (s, t) = (0.5f - position.X, 0.5f - position.Y);
        }
        else if (normal.X < -0.5f)
        {
            (x, y, width, height) =
                (layout.X, layout.Y + layout.Depth, layout.Depth, layout.Height);
            (s, t) = (position.Z + 0.5f, 0.5f - position.Y);
        }
        else if (normal.X > 0.5f)
        {
            (x, y, width, height) = (
                layout.X + layout.Depth + layout.Width,
                layout.Y + layout.Depth, layout.Depth, layout.Height);
            (s, t) = (0.5f - position.Z, 0.5f - position.Y);
        }
        else if (normal.Y > 0.5f)
        {
            (x, y, width, height) =
                (layout.X + layout.Depth, layout.Y, layout.Width, layout.Depth);
            (s, t) = (position.X + 0.5f, position.Z + 0.5f);
        }
        else if (normal.Y < -0.5f)
        {
            (x, y, width, height) = (
                layout.X + layout.Depth + layout.Width, layout.Y,
                layout.Width, layout.Depth);
            (s, t) = (position.X + 0.5f, 0.5f - position.Z);
        }
        else
        {
            uv = default;
            return false;
        }

        uv = new Vector2((x + s * width) / 64f, (y + t * height) / 64f);
        return true;
    }
}
