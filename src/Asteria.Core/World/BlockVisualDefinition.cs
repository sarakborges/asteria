namespace Asteria.Core.World;

public enum BlockVisualKind : byte
{
    Geometry = 0,
    CrossedSprite = 1,
    GroundSprite = 2,
}

public sealed class BlockVisualDefinition
{
    private BlockVisualDefinition(
        BlockVisualKind kind,
        BlockTextureLayer? texture,
        float width,
        float height,
        int planes,
        float baseOffset,
        float targetHeight = 0f)
    {
        Kind = kind;
        Texture = texture;
        Width = width;
        Height = height;
        Planes = planes;
        BaseOffset = baseOffset;
        TargetHeight = targetHeight;
    }

    public BlockVisualKind Kind { get; }

    public BlockTextureLayer? Texture { get; }

    public float Width { get; }

    public float Height { get; }

    public int Planes { get; }

    public float BaseOffset { get; }

    /// <summary>Interaction hitbox height for a compact ground sprite.</summary>
    public float TargetHeight { get; }

    public static BlockVisualDefinition Geometry { get; } =
        new(
            BlockVisualKind.Geometry,
            null,
            1f,
            1f,
            0,
            0f);

    /// <summary>
    /// Horizontal cutout sprite inside a voxel; reusable for ground objects.
    /// </summary>
    public static BlockVisualDefinition GroundSprite(
        BlockTextureLayer texture,
        float width = 0.42f,
        float height = 0.012f,
        float baseOffset = 0.0125f,
        float targetHeight = 0.14f)
    {
        if (!float.IsFinite(width) || width <= 0f || width > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (!float.IsFinite(height) || height <= 0f ||
            !float.IsFinite(baseOffset) || baseOffset < 0f ||
            baseOffset + height > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(height),
                "Ground-sprite height and offset must fit inside its voxel.");
        }

        if (!float.IsFinite(targetHeight) || targetHeight < height ||
            baseOffset + targetHeight > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight),
                "Ground-sprite hitbox must contain its visual inside the voxel.");
        }

        return new BlockVisualDefinition(
            BlockVisualKind.GroundSprite, texture, width, height, 0,
            baseOffset, targetHeight);
    }

    public static BlockVisualDefinition CrossedSprite(
        BlockTextureLayer texture,
        float width = 0.72f,
        float height = 0.62f,
        int planes = 2,
        float baseOffset = 0f)
    {
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
