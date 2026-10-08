namespace Asteria.Core.World;

/// <summary>
/// Optional conditions shared by material patches and ground decorators.
/// Slope is the maximum adjacent height difference in blocks per horizontal
/// block; both altitude and slope use the authoritative final terrain field.
/// </summary>
public sealed class SurfacePlacementConditions
{
    public SurfacePlacementConditions(
        int? minY = null,
        int? maxY = null,
        double? minSlope = null,
        double? maxSlope = null)
    {
        if (minY is < 0 ||
            maxY is < 0 ||
            (minY.HasValue && maxY.HasValue && minY > maxY) ||
            (minSlope.HasValue &&
             (!double.IsFinite(minSlope.Value) ||
              minSlope.Value < 0d ||
              minSlope.Value > 512d)) ||
            (maxSlope.HasValue &&
             (!double.IsFinite(maxSlope.Value) ||
              maxSlope.Value < 0d ||
              maxSlope.Value > 512d)) ||
            (minSlope.HasValue && maxSlope.HasValue &&
             minSlope > maxSlope))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minY),
                "Placement altitude must be non-negative and slope within 0..512, with ordered bounds.");
        }

        MinY = minY;
        MaxY = maxY;
        MinSlope = minSlope;
        MaxSlope = maxSlope;
    }

    public bool RequiresSlope => MinSlope.HasValue || MaxSlope.HasValue;

    public int? MinY { get; }
    public int? MaxY { get; }
    public double? MinSlope { get; }
    public double? MaxSlope { get; }

    public bool Allows(in SurfacePlacementContext context) =>
        (!MinY.HasValue || context.Y >= MinY) &&
        (!MaxY.HasValue || context.Y <= MaxY) &&
        (!MinSlope.HasValue || context.Slope >= MinSlope) &&
        (!MaxSlope.HasValue || context.Slope <= MaxSlope);
}

public readonly record struct SurfacePlacementContext(int Y, double Slope)
{
    public static SurfacePlacementContext Sample(
        SurfaceTerrainField terrain,
        int worldX,
        int worldZ,
        bool includeSlope = true,
        bool baseSurface = false)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var y = HeightAt(terrain, worldX, worldZ, baseSurface);
        if (!includeSlope)
        {
            return new SurfacePlacementContext(y, 0d);
        }
        var xPlus = worldX == int.MaxValue ? worldX : worldX + 1;
        var xMinus = worldX == int.MinValue ? worldX : worldX - 1;
        var zPlus = worldZ == int.MaxValue ? worldZ : worldZ + 1;
        var zMinus = worldZ == int.MinValue ? worldZ : worldZ - 1;
        var slope = Math.Max(
            Math.Max(
                Math.Abs((long)y - HeightAt(terrain, xPlus, worldZ, baseSurface)),
                Math.Abs((long)y - HeightAt(terrain, xMinus, worldZ, baseSurface))),
            Math.Max(
                Math.Abs((long)y - HeightAt(terrain, worldX, zPlus, baseSurface)),
                Math.Abs((long)y - HeightAt(terrain, worldX, zMinus, baseSurface))));
        return new SurfacePlacementContext(y, slope);
    }

    private static int HeightAt(
        SurfaceTerrainField terrain,
        int worldX,
        int worldZ,
        bool baseSurface) =>
        baseSurface
            ? terrain.SampleBaseSurface(worldX, worldZ).BaseY
            : terrain.SurfaceHeight(worldX, worldZ);
}
