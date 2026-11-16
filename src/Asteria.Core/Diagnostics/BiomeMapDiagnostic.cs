using Asteria.Core.World;

namespace Asteria.Core.Diagnostics;

public enum BiomeMapMode : byte
{
    Primary = 0,
    Influences = 1,
}

public readonly record struct BiomeMapColor(
    byte Red,
    byte Green,
    byte Blue)
{
    public string Hex =>
        $"{Red:X2}{Green:X2}{Blue:X2}";
}

public sealed record BiomeMapLegendEntry(
    string BiomeId,
    BiomeMapColor Color);

public sealed class BiomeMapRaster
{
    internal BiomeMapRaster(
        int width,
        int depth,
        BiomeMapColor[] pixels,
        IReadOnlyList<BiomeMapLegendEntry> legend)
    {
        Width = width;
        Depth = depth;
        Pixels = pixels;
        Legend = legend;
    }

    public int Width { get; }

    public int Depth { get; }

    public IReadOnlyList<BiomeMapColor> Pixels { get; }

    public IReadOnlyList<BiomeMapLegendEntry> Legend { get; }

    public BiomeMapColor this[
        int x,
        int z]
    {
        get
        {
            if ((uint)x >=
                    (uint)Width ||
                (uint)z >=
                    (uint)Depth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x));
            }

            return Pixels[
                z *
                Width +
                x];
        }
    }
}

/// <summary>
/// Pure diagnostic consumer of the authoritative biome field. It assigns
/// stable display colors to biome ids and never owns or reconstructs layout.
/// </summary>
public static class BiomeMapDiagnostic
{
    public static BiomeMapRaster Render(
        BiomeField biomes,
        int originX,
        int originZ,
        int width,
        int depth,
        int step = 1,
        BiomeMapMode mode =
            BiomeMapMode.Primary)
    {
        ArgumentNullException.ThrowIfNull(
            biomes);

        if (width <= 0 ||
            depth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Biome-map dimensions must be positive.");
        }

        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(step),
                "Biome-map step must be positive.");
        }

        var samples =
            biomes.SampleGrid(
                originX,
                originZ,
                width,
                depth,
                step);
        var biomeIds =
            new HashSet<string>(
                StringComparer.Ordinal);

        for (var z = 0;
             z < depth;
             z++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var sample =
                    samples[
                        x,
                        z];

                if (mode ==
                    BiomeMapMode.Primary)
                {
                    biomeIds.Add(
                        sample.Primary);
                    continue;
                }

                foreach (var influence in
                         sample.Influences)
                {
                    biomeIds.Add(
                        influence.BiomeId);
                }
            }
        }

        var colors =
            biomeIds
                .OrderBy(
                    id => id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    id => id,
                    StableColor,
                    StringComparer.Ordinal);
        var pixels =
            new BiomeMapColor[
                checked(
                    width *
                    depth)];

        for (var z = 0;
             z < depth;
             z++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var sample =
                    samples[
                        x,
                        z];
                pixels[
                    z *
                    width +
                    x] =
                    mode ==
                    BiomeMapMode.Primary
                        ? colors[
                            sample.Primary]
                        : Blend(
                            sample,
                            colors);
            }
        }

        var legend =
            colors
                .Select(entry =>
                    new BiomeMapLegendEntry(
                        entry.Key,
                        entry.Value))
                .ToArray();

        return new BiomeMapRaster(
            width,
            depth,
            pixels,
            Array.AsReadOnly(
                legend));
    }

    private static BiomeMapColor Blend(
        BiomeSample sample,
        IReadOnlyDictionary<
            string,
            BiomeMapColor> colors)
    {
        var red = 0d;
        var green = 0d;
        var blue = 0d;

        foreach (var influence in
                 sample.Influences)
        {
            var color =
                colors[
                    influence.BiomeId];
            red +=
                color.Red *
                influence.Weight;
            green +=
                color.Green *
                influence.Weight;
            blue +=
                color.Blue *
                influence.Weight;
        }

        return new BiomeMapColor(
            Quantize(red),
            Quantize(green),
            Quantize(blue));
    }

    private static byte Quantize(
        double value) =>
        checked(
            (byte)Math.Clamp(
                (int)Math.Round(
                    value,
                    MidpointRounding.AwayFromZero),
                byte.MinValue,
                byte.MaxValue));

    private static BiomeMapColor StableColor(
        string biomeId)
    {
        var hash =
            Fnv1a(
                biomeId);
        var hue =
            (hash %
             360UL) /
            360d;

        return HsvToRgb(
            hue,
            0.62d,
            0.86d);
    }

    private static ulong Fnv1a(
        string value)
    {
        var hash =
            14695981039346656037UL;

        foreach (var character in
                 value)
        {
            hash =
                unchecked(
                    (hash ^
                     character) *
                    1099511628211UL);
        }

        return hash;
    }

    private static BiomeMapColor HsvToRgb(
        double hue,
        double saturation,
        double value)
    {
        var scaled =
            hue *
            6d;
        var sector =
            (int)Math.Floor(
                scaled);
        var fraction =
            scaled -
            sector;
        var p =
            value *
            (1d -
             saturation);
        var q =
            value *
            (1d -
             fraction *
             saturation);
        var t =
            value *
            (1d -
             (1d -
              fraction) *
             saturation);

        var (red, green, blue) =
            (sector %
             6) switch
            {
                0 =>
                    (
                        value,
                        t,
                        p),
                1 =>
                    (
                        q,
                        value,
                        p),
                2 =>
                    (
                        p,
                        value,
                        t),
                3 =>
                    (
                        p,
                        q,
                        value),
                4 =>
                    (
                        t,
                        p,
                        value),
                _ =>
                    (
                        value,
                        p,
                        q),
            };

        return new BiomeMapColor(
            ToByte(
                red),
            ToByte(
                green),
            ToByte(
                blue));
    }

    private static byte ToByte(
        double value) =>
        checked(
            (byte)Math.Clamp(
                (int)Math.Round(
                    value *
                    255d,
                    MidpointRounding.AwayFromZero),
                byte.MinValue,
                byte.MaxValue));
}
