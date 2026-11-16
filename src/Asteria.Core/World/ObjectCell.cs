using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Quantized object-local offset and scale, matching MineClone's 1/1024
/// precision. Values are portable, deterministic and safe to persist.
/// </summary>
public readonly record struct ObjectTransform
{
    public const float Units = 1024f;
    public static ObjectTransform Identity { get; } = new(0, 0, 0, 1024, 1024, 1024);

    public ObjectTransform(short offsetX, short offsetY, short offsetZ,
        ushort scaleX, ushort scaleY, ushort scaleZ)
    {
        if (Math.Abs((int)offsetX) > 8192 || Math.Abs((int)offsetY) > 8192 ||
            Math.Abs((int)offsetZ) > 8192 ||
            scaleX is < 1 or > 8192 || scaleY is < 1 or > 8192 ||
            scaleZ is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(scaleX),
                "Object transform offset and scale must be within the supported range.");
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetZ = offsetZ;
        ScaleX = scaleX;
        ScaleY = scaleY;
        ScaleZ = scaleZ;
    }

    public short OffsetX { get; }
    public short OffsetY { get; }
    public short OffsetZ { get; }
    public ushort ScaleX { get; }
    public ushort ScaleY { get; }
    public ushort ScaleZ { get; }
    public Vector3 Offset => new(OffsetX / Units, OffsetY / Units, OffsetZ / Units);
    public Vector3 Scale => new(ScaleX / Units, ScaleY / Units, ScaleZ / Units);

    public static ObjectTransform FromParts(Vector3 offset, Vector3 scale)
    {
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) ||
            !float.IsFinite(offset.Z) || !float.IsFinite(scale.X) ||
            !float.IsFinite(scale.Y) || !float.IsFinite(scale.Z) ||
            Vector3.Abs(offset).X > 8f || Vector3.Abs(offset).Y > 8f ||
            Vector3.Abs(offset).Z > 8f ||
            scale.X is <= 0 or > 8f || scale.Y is <= 0 or > 8f ||
            scale.Z is <= 0 or > 8f)
            throw new ArgumentOutOfRangeException(nameof(scale));

        var sx = (int)MathF.Round(scale.X * Units);
        var sy = (int)MathF.Round(scale.Y * Units);
        var sz = (int)MathF.Round(scale.Z * Units);
        if (sx == 0 || sy == 0 || sz == 0)
            throw new ArgumentOutOfRangeException(nameof(scale),
                "Object scale is smaller than the supported quantization.");
        return new ObjectTransform(
            checked((short)MathF.Round(offset.X * Units)),
            checked((short)MathF.Round(offset.Y * Units)),
            checked((short)MathF.Round(offset.Z * Units)),
            checked((ushort)sx), checked((ushort)sy), checked((ushort)sz));
    }
}

/// <summary>
/// An object attached to a supporting voxel, not occupying the block or
/// fluid palette. Face and rotation belong to this individual object.
/// </summary>
public readonly record struct ObjectCell
{
    public const int MaximumIdLength = 256;

    public ObjectCell(string id, BlockFace face,
        TextureRotation rotation = TextureRotation.Degrees0,
        ObjectTransform? transform = null)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaximumIdLength)
            throw new ArgumentException("Invalid object ID.", nameof(id));
        BlockDefinition.ValidateId(id);
        if (!Enum.IsDefined(face) || !Enum.IsDefined(rotation))
            throw new ArgumentOutOfRangeException(nameof(face));
        var selected = transform ?? ObjectTransform.Identity;
        if (selected.ScaleX == 0 || selected.ScaleY == 0 || selected.ScaleZ == 0)
            throw new ArgumentOutOfRangeException(nameof(transform));
        Id = id;
        Face = face;
        Rotation = rotation;
        Transform = selected;
    }

    public string Id { get; }
    public BlockFace Face { get; }
    public TextureRotation Rotation { get; }
    public ObjectTransform Transform { get; }
}
