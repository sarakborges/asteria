namespace Asteria.Core.World;

public enum BlockVisualKind : byte
{
    Geometry = 0,
    CrossedSprite = 1,
}

public sealed class BlockVisualDefinition
{
    private BlockVisualDefinition(
        BlockVisualKind kind,
        BlockTextureLayer? texture,
        float width,
        float height,
        int planes,
        float baseOffset)
    {
        Kind = kind;
        Texture = texture;
        Width = width;
        Height = height;
        Planes = planes;
        BaseOffset = baseOffset;
    }

    public BlockVisualKind Kind { get; }

    public BlockTextureLayer? Texture { get; }

    public float Width { get; }

    public float Height { get; }

    public int Planes { get; }

    public float BaseOffset { get; }

    public static BlockVisualDefinition Geometry { get; } =
        new(
            BlockVisualKind.Geometry,
            null,
            1f,
            1f,
            0,
            0f);

    public static BlockVisualDefinition CrossedSprite(
        BlockTextureLayer texture,
        float width = 0.72f,
        float height = 0.62f,
        int planes = 2,
        float baseOffset = 0f)
    {
        ArgumentNullException.ThrowIfNull(texture);

        if (!float.IsFinite(width) ||
            width <= 0f ||
            width > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Crossed-sprite width must be within (0, 1].");
        }

        if (!float.IsFinite(height) ||
            height <= 0f ||
            height > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Crossed-sprite height must be within (0, 1].");
        }

        if (planes is < 2 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(planes),
                "Crossed-sprite planes must be within 2..8.");
        }

        if (!float.IsFinite(baseOffset) ||
            baseOffset < 0f ||
            baseOffset + height > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseOffset),
                "Crossed-sprite base offset must keep the sprite inside its voxel.");
        }

        return new BlockVisualDefinition(
            BlockVisualKind.CrossedSprite,
            texture,
            width,
            height,
            planes,
            baseOffset);
    }
}
