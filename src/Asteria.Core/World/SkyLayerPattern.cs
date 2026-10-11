namespace Asteria.Core.World;

/// <summary>
/// Deterministic, finite sky-scene geometry. This owns layout only;
/// the engine adapter owns visual lifetime and render publication.
/// </summary>
public static class SkyLayerPattern
{
    public const int StarCount = 96;
    public const int CloudCount = 24;
    public const int CloudPartsPerCloud = 3;
    public const float StarDistance = 110f;
    public const float CloudSpan = 180f;
    private const float GoldenAngle = 2.3999631f;

    public readonly record struct Star(float X, float Y, float Z, float Size);
    public readonly record struct CloudPart(
        float BaseX, float BaseZ, float AltitudeAboveSeaLevel,
        float OffsetX, float OffsetY, float OffsetZ,
        float Width, float Height, float Depth);

    public static Star StarAt(int index)
    {
        if ((uint)index >= StarCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        var progress = (index + 0.5f) / StarCount;
        var y = 0.08f + progress * 0.86f;
        var horizontal = MathF.Sqrt(1f - y * y);
        var angle = index * GoldenAngle;
        var size = 0.16f + Hash01((uint)index) * 0.18f;
        return new Star(
            MathF.Cos(angle) * horizontal,
            y,
            MathF.Sin(angle) * horizontal,
            size);
    }

    public static CloudPart CloudPartAt(int index, int part)
    {
        if ((uint)index >= CloudCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        if ((uint)part >= CloudPartsPerCloud)
            throw new ArgumentOutOfRangeException(nameof(part));

        var seed = (uint)index;
        var baseX = HashSigned(unchecked(seed * 17u + 3u)) * CloudSpan * 0.5f;
        var baseZ = HashSigned(unchecked(seed * 29u + 11u)) * CloudSpan * 0.5f;
        var altitude = 34f + Hash01(unchecked(seed * 37u + 5u)) * 14f;
        var width = 9f + Hash01(unchecked(seed * 43u + 7u)) * 8f;
        var depth = 4f + Hash01(unchecked(seed * 53u + 13u)) * 5f;
        return part switch
        {
            0 => new CloudPart(baseX, baseZ, altitude,
                0f, 0f, 0f, width, 0.7f, depth),
            1 => new CloudPart(baseX, baseZ, altitude,
                width * 0.28f, 0.15f, depth * 0.35f,
                width * 0.55f, 0.7f, depth * 0.75f),
            _ => new CloudPart(baseX, baseZ, altitude,
                -width * 0.32f, -0.05f, -depth * 0.28f,
                width * 0.42f, 0.7f, depth * 0.62f),
        };
    }

    public static float WorldTileCoordinate(
        float baseCoordinate, float drift, float cameraCoordinate)
    {
        var position = baseCoordinate + drift;
        var nearestTile = MathF.Round(
            (cameraCoordinate - position) / CloudSpan,
            MidpointRounding.AwayFromZero);
        return position + nearestTile * CloudSpan;
    }

    public static float StarVisibility(DayNightSample sample) =>
        (sample.Phase, sample.NextPhase) switch
        {
            (DayNightPhase.Dusk, DayNightPhase.Night) =>
                (float)sample.Transition,
            (DayNightPhase.Night, DayNightPhase.Dawn) =>
                1f - (float)sample.Transition,
            (DayNightPhase.Night, _) => 1f,
            _ => 0f,
        };

    private static float HashSigned(uint value) => Hash01(value) * 2f - 1f;

    private static float Hash01(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
        }
        return value / (float)uint.MaxValue;
    }
}
