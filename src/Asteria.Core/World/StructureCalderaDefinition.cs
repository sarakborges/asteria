namespace Asteria.Core.World;

/// <summary>
/// Compact, authored radial structure. The worldgen structure field owns its
/// placement; the chunk materializer writes only its intersecting columns.
/// This avoids expanding an entire mountain into per-voxel template payloads.
/// </summary>
public sealed class StructureCalderaDefinition
{
    public StructureCalderaDefinition(
        int radius,
        int craterRadius,
        int floorRadius,
        int height,
        int floorHeight,
        int lavaLevel,
        int embedDepth,
        int biomeMargin,
        string wallBlock,
        string floorBlock,
        string fluid)
    {
        if (radius is < 30 or > 64 ||
            craterRadius is < 12 or > 32 ||
            floorRadius is < 2 or > 24 ||
            craterRadius >= radius - 8 ||
            floorRadius >= craterRadius - 4 ||
            height is < 16 or > 64 ||
            floorHeight is < 2 or > 16 ||
            lavaLevel <= floorHeight + 2 ||
            lavaLevel >= height - 6 ||
            embedDepth is < 1 or > 12 ||
            biomeMargin is < 6 or > 32 ||
            radius + biomeMargin > 96)
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "Caldera requires a closed raised rim, a deep lava basin and an interior biome clearance.");
        BlockDefinition.ValidateId(wallBlock);
        BlockDefinition.ValidateId(floorBlock);
        FluidDefinition.ValidateId(fluid);

        Radius = radius;
        CraterRadius = craterRadius;
        FloorRadius = floorRadius;
        Height = height;
        FloorHeight = floorHeight;
        LavaLevel = lavaLevel;
        EmbedDepth = embedDepth;
        BiomeMargin = biomeMargin;
        WallBlock = wallBlock;
        FloorBlock = floorBlock;
        Fluid = fluid;
    }

    public int Radius { get; }
    public int CraterRadius { get; }
    public int FloorRadius { get; }
    public int Height { get; }
    public int FloorHeight { get; }
    public int LavaLevel { get; }
    public int EmbedDepth { get; }
    public int BiomeMargin { get; }
    public string WallBlock { get; }
    public string FloorBlock { get; }
    public string Fluid { get; }

    public bool Contains(int x, int z) =>
        (long)x * x + (long)z * z <= (long)Radius * Radius;

    public int SurfaceHeightAt(int x, int z)
    {
        var distance = Math.Sqrt((double)x * x + (double)z * z);
        if (distance > Radius)
            return -1;
        if (distance <= FloorRadius)
            return FloorHeight;
        if (distance < CraterRadius)
            return FloorHeight + (int)Math.Round(
                (Height - FloorHeight) *
                SmoothStep((distance - FloorRadius) /
                    (CraterRadius - FloorRadius)));
        return (int)Math.Round(Height *
            SmoothStep((Radius - distance) / (Radius - CraterRadius)));
    }

    public bool HasLavaAt(int x, int z, int y) =>
        y > SurfaceHeightAt(x, z) &&
        y <= LavaLevel &&
        (long)x * x + (long)z * z <
        (long)CraterRadius * CraterRadius;

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        return t * t * (3d - 2d * t);
    }
}
