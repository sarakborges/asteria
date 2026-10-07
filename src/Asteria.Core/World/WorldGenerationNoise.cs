namespace Asteria.Core.World;

internal static class WorldGenerationNoise
{
    public static double FractalNoise2D(
        ulong seed,
        GenerationDomain domain,
        double x,
        double z,
        double frequency,
        int octaves = 4)
    {
        if (!double.IsFinite(frequency) ||
            frequency <= 0d ||
            octaves < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        var value = 0d;
        var normalization = 0d;
        var amplitude = 1d;
        var octaveFrequency = frequency;

        for (var octave = 0; octave < octaves; octave++)
        {
            var period = 1d / octaveFrequency;
            value +=
                WorldGenerationEntropy.SmoothNoise2D(
                    unchecked(seed + (ulong)octave * 0x9e3779b97f4a7c15UL),
                    domain,
                    x,
                    z,
                    period) *
                amplitude;
            normalization += amplitude;
            amplitude *= 0.5d;
            octaveFrequency *= 2d;
        }

        return value / normalization;
    }
}
