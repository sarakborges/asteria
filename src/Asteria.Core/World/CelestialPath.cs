using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Deterministic authored sun/moon direction for one Sphere clock. The
/// Godot presentation consumes this pure orbit and owns all rendered nodes.
/// Directions use the canonical world axes: +Y is up and -Z is north.
/// </summary>
public static class CelestialPath
{
    public static Vector3? Direction(
        DayNightCelestialDefinition body,
        DayNightCycleDefinition cycle,
        ulong tickInDay)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(cycle);

        var progress = cycle.ProgressBetweenPhases(
            tickInDay, body.RisePhase, body.SetPhase);
        if (progress is null)
            return null;

        var rise = body.RiseAzimuthDegrees * (Math.PI / 180d);
        var set = body.SetAzimuthDegrees * (Math.PI / 180d);
        // Match the canonical celestial path: interpolation takes the
        // shortest turn across 0/360, never a 340-degree detour.
        var delta = (set - rise + Math.PI) % (2d * Math.PI);
        if (delta < 0d)
            delta += 2d * Math.PI;
        delta -= Math.PI;

        var azimuth = rise + delta * progress.Value;
        var altitude = Math.Sin(progress.Value * Math.PI) *
            body.MaxAltitudeDegrees * (Math.PI / 180d);
        var horizontal = Math.Cos(altitude);

        return Vector3.Normalize(new Vector3(
            (float)(Math.Sin(azimuth) * horizontal),
            (float)Math.Sin(altitude),
            (float)(-Math.Cos(azimuth) * horizontal)));
    }
}
