namespace Asteria.Core.World;

/// <summary>
/// Compass bearing follows camera yaw. Only the front-facing third-person
/// camera is reversed relative to player movement, matching MineClone.
/// </summary>
public static class PlayerCompassHeading
{
    public static float QuantizedDegrees(float playerYawRadians, bool frontCamera)
    {
        if (!float.IsFinite(playerYawRadians))
            throw new ArgumentOutOfRangeException(nameof(playerYawRadians));

        var radians = playerYawRadians % (2f * MathF.PI);
        var heading = -radians * (180f / MathF.PI);
        if (frontCamera)
            heading -= 180f;

        heading %= 360f;
        if (heading < 0f)
            heading += 360f;
        return (MathF.Round(heading * 2f) / 2f) % 360f;
    }
}
