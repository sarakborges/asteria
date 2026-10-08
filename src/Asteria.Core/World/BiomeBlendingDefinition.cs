namespace Asteria.Core.World;

/// <summary>
/// Sphere-wide organic biome placement and influence controls. All values
/// describe geometry rather than cache/search implementation details.
/// </summary>
public sealed class BiomeBlendingDefinition
{
    public static BiomeBlendingDefinition Default { get; } = new();

    public BiomeBlendingDefinition(
        double scoreBand = 0.50d,
        double jitterFraction = 0.32d,
        double coarseWarpPeriod = 4d,
        double fineWarpPeriod = 1.35d,
        double coarseWarpStrength = 0.42d,
        double fineWarpStrength = 0.16d,
        BiomeInfluenceCurve influenceCurve = BiomeInfluenceCurve.SmoothStep)
    {
        if (!double.IsFinite(scoreBand) ||
            scoreBand is < 0.05d or > 2d ||
            !double.IsFinite(jitterFraction) ||
            jitterFraction is < 0d or > 0.49d ||
            !double.IsFinite(coarseWarpPeriod) ||
            coarseWarpPeriod is < 1d or > 16d ||
            !double.IsFinite(fineWarpPeriod) ||
            fineWarpPeriod is < 0.5d or > 8d ||
            !double.IsFinite(coarseWarpStrength) ||
            coarseWarpStrength is < 0d or > 0.6d ||
            !double.IsFinite(fineWarpStrength) ||
            fineWarpStrength is < 0d or > 0.3d ||
            coarseWarpStrength + fineWarpStrength > 0.75d ||
            !Enum.IsDefined(influenceCurve))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scoreBand),
                "Biome blending requires bounded finite band, jitter, warp periods, strengths and influence curve.");
        }

        ScoreBand = scoreBand;
        JitterFraction = jitterFraction;
        CoarseWarpPeriod = coarseWarpPeriod;
        FineWarpPeriod = fineWarpPeriod;
        CoarseWarpStrength = coarseWarpStrength;
        FineWarpStrength = fineWarpStrength;
        InfluenceCurve = influenceCurve;
    }

    public double ScoreBand { get; }
    public double JitterFraction { get; }
    public double CoarseWarpPeriod { get; }
    public double FineWarpPeriod { get; }
    public double CoarseWarpStrength { get; }
    public double FineWarpStrength { get; }
    public BiomeInfluenceCurve InfluenceCurve { get; }

    public double WeightAt(double proximity) =>
        InfluenceCurve switch
        {
            BiomeInfluenceCurve.Linear => proximity,
            BiomeInfluenceCurve.SmoothStep =>
                WorldGenerationEntropy.SmoothStep(proximity),
            BiomeInfluenceCurve.SmootherStep =>
                proximity * proximity * proximity *
                (proximity * (proximity * 6d - 15d) + 10d),
            _ => throw new InvalidOperationException(
                "Invalid authored biome influence curve."),
        };
}

public enum BiomeInfluenceCurve
{
    Linear,
    SmoothStep,
    SmootherStep,
}
