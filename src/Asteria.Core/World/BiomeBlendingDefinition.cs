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
        BiomeInfluenceCurve influenceCurve = BiomeInfluenceCurve.SmoothStep,
        IEnumerable<BiomeContourHarmonicDefinition>? contourHarmonics = null,
        double sizeExponent = 0.12d,
        double seedBiasAmplitude = 0.045d,
        double continuationBonus = 0.055d)
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
            !Enum.IsDefined(influenceCurve) ||
            !double.IsFinite(sizeExponent) ||
            sizeExponent is < 0d or > 0.5d ||
            !double.IsFinite(seedBiasAmplitude) ||
            seedBiasAmplitude is < 0d or > 0.25d ||
            !double.IsFinite(continuationBonus) ||
            continuationBonus is < 0d or > 0.25d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scoreBand),
                "Biome blending requires bounded finite band, jitter, warp periods, strengths and influence curve.");
        }

        var harmonics = contourHarmonics?.ToArray() ??
        [
            new BiomeContourHarmonicDefinition(3, 0.13d),
            new BiomeContourHarmonicDefinition(5, 0.07d),
        ];
        if (harmonics.Length > 4 ||
            harmonics.Any(harmonic => harmonic is null) ||
            harmonics.Sum(harmonic => harmonic.Amplitude) > 0.75d)
        {
            throw new ArgumentException(
                "Contours allow at most four harmonics with total amplitude <= 0.75.",
                nameof(contourHarmonics));
        }

        ContourHarmonics = Array.AsReadOnly(harmonics);
        SizeExponent = sizeExponent;
        SeedBiasAmplitude = seedBiasAmplitude;
        ContinuationBonus = continuationBonus;
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

    public IReadOnlyList<BiomeContourHarmonicDefinition> ContourHarmonics { get; }
    public double SizeExponent { get; }
    public double SeedBiasAmplitude { get; }
    public double ContinuationBonus { get; }

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

public sealed class BiomeContourHarmonicDefinition
{
    public BiomeContourHarmonicDefinition(int lobes, double amplitude)
    {
        if (lobes is < 1 or > 12 ||
            !double.IsFinite(amplitude) ||
            amplitude is < 0d or > 0.4d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lobes),
                "Contour lobes must be 1..12 and amplitude finite within 0..0.4.");
        }

        Lobes = lobes;
        Amplitude = amplitude;
    }

    public int Lobes { get; }
    public double Amplitude { get; }
}
