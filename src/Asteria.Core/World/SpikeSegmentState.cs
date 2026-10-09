namespace Asteria.Core.World;

/// <summary>
/// Packed, chunk-independent profile of one voxel of a vertical spike.
/// 4 bits segment index, 4 bits (height - 1), 1 bit ceiling orientation.
/// Index 0 always touches the solid anchor.
/// </summary>
public static class SpikeSegmentState
{
    public const int MaximumHeight = 16;

    public static ushort Encode(int index, int height, bool down)
    {
        if (height is < 1 or > MaximumHeight ||
            index < 0 || index >= height)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return (ushort)(index | ((height - 1) << 4) |
            (down ? 0x100 : 0));
    }

    public static int Index(ushort state) => state & 0xf;
    public static int Height(ushort state) => ((state >> 4) & 0xf) + 1;
    public static bool IsDown(ushort state) => (state & 0x100) != 0;

    /// <summary>
    /// Four square, axis-aligned tiers per voxel. Both mesh and fine
    /// occupancy use this exact snapped half-width, including at seams.
    /// </summary>
    public const int TiersPerVoxel = 4;

    public static float HalfWidthAt(
        BlockShapeDefinition shape, ushort state, float localY)
    {
        var tier = Math.Clamp(
            (int)MathF.Floor(localY * TiersPerVoxel),
            0, TiersPerVoxel - 1);
        var sampleY = (tier + 0.5f) / TiersPerVoxel;
        var radius = RadiusAt(shape, state, sampleY);
        return Math.Clamp(
            MathF.Ceiling(radius * BlockGeometry.Resolution) /
                BlockGeometry.Resolution,
            1f / BlockGeometry.Resolution, 0.5f);
    }

    public static float RadiusAt(
        BlockShapeDefinition shape,
        ushort state,
        float localY)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.Kind != BlockShapeKind.Spike)
            throw new ArgumentException("Expected a spike shape.", nameof(shape));

        var index = Math.Min(Index(state), Height(state) - 1);
        var progress = (index +
            (IsDown(state) ? 1f - localY : localY)) /
            Height(state);
        return shape.SpikeBaseRadius +
            (shape.SpikeTipRadius - shape.SpikeBaseRadius) *
            MathF.Pow(Math.Clamp(progress, 0f, 1f), shape.SpikeTaperPower);
    }
}
