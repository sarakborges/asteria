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
        string part, Vector3 position, Vector3 normal, out Vector2 uv)
    {
        var layout = part switch
        {
            "HeadMesh" => new Box(0, 0, 8, 8, 8),
            "HairLayer" => new Box(32, 0, 8, 8, 8),
            "BodyMesh" => new Box(16, 16, 8, 12, 4),
            "RightArmMesh" => new Box(40, 16, 4, 12, 4),
            "LeftArmMesh" => new Box(32, 48, 4, 12, 4),
            "RightLegMesh" => new Box(0, 16, 4, 12, 4),
            "LeftLegMesh" => new Box(16, 48, 4, 12, 4),
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
