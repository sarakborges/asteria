namespace Asteria.Core.World;

/// <summary>
/// Three-dimensional, smooth patches of cave spike growth. Horizontal and
/// vertical scales are independent so chambers can host broad shelves of
/// formations without banding through the entire cave column.
/// </summary>
public sealed class CaveSpikeClusterDefinition
{
    public CaveSpikeClusterDefinition(
        int horizontalScale,
        int verticalScale,
        float threshold,
        float transitionWidth)
    {
        if (horizontalScale is < 2 or > 512)
            throw new ArgumentOutOfRangeException(nameof(horizontalScale));
        if (verticalScale is < 2 or > 256)
            throw new ArgumentOutOfRangeException(nameof(verticalScale));
        if (!float.IsFinite(threshold) || threshold is < -1f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        if (!float.IsFinite(transitionWidth) ||
            transitionWidth is < 0f or > 2f)
            throw new ArgumentOutOfRangeException(nameof(transitionWidth));

        HorizontalScale = horizontalScale;
        VerticalScale = verticalScale;
        Threshold = threshold;
        TransitionWidth = transitionWidth;
    }

    public int HorizontalScale { get; }
    public int VerticalScale { get; }
    public float Threshold { get; }
    public float TransitionWidth { get; }
}
