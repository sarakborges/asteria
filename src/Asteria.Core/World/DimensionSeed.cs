namespace Asteria.Core.World;

public static class DimensionSeed
{
    private static readonly GenerationDomain RootDomain =
        GenerationDomain.Named(
            "dimension-seed/v1");

    public static ulong Derive(
        ulong worldSeed,
        DimensionId dimension)
    {
        var dimensionDomain =
            GenerationDomain.Named(
                "dimension-seed/v1/" +
                dimension.Value);

        return WorldGenerationEntropy.Sample2D(
            worldSeed ^
            RootDomain.Key,
            dimensionDomain,
            0,
            0);
    }
}
