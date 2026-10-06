namespace Asteria.Core.World;

internal readonly record struct GenerationDomain(ulong Key)
{
    public static GenerationDomain Named(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        var hash =
            0xcbf29ce484222325UL;
        foreach (var character in name)
        {
            hash ^=
                character;
            hash *=
                0x100000001b3UL;
        }

        return new GenerationDomain(
            Mix(hash));
    }

    private static long FloorDivRem(
        int value,
        long divisor,
        out long remainder)
    {
        var quotient =
            Math.DivRem(
                (long)value,
                divisor,
                out remainder);

        if (remainder >= 0)
        {
            return quotient;
        }

        remainder += divisor;
        return quotient - 1;
    }

    private static ulong Mix(
        ulong value)
    {
        value ^=
            value >> 30;
        value *=
            0xbf58476d1ce4e5b9UL;
        value ^=
            value >> 27;
        value *=
            0x94d049bb133111ebUL;
        value ^=
            value >> 31;
        return value;
    }
}

internal static class WorldGenerationEntropy
{
    public static ulong Sample2D(
        ulong seed,
        GenerationDomain domain,
        int x,
        int z)
    {
        unchecked
        {
            var value =
                seed ^
                domain.Key ^
                ((ulong)(uint)x *
                 0x9e3779b185ebca87UL) ^
                ((ulong)(uint)z *
                 0xc2b2ae3d27d4eb4fUL);

            return Mix(value);
        }
    }

    public static double SmoothNoise2D(
        ulong seed,
        GenerationDomain domain,
        double x,
        double z,
        double period)
    {
        if (!double.IsFinite(period) ||
            period <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(period));
        }

        var sampleX =
            x / period;
        var sampleZ =
            z / period;
        var gridX =
            ClampToInt(
                Math.Floor(sampleX));
        var gridZ =
            ClampToInt(
                Math.Floor(sampleZ));
        var nextX =
            gridX == int.MaxValue
                ? int.MaxValue
                : gridX + 1;
        var nextZ =
            gridZ == int.MaxValue
                ? int.MaxValue
                : gridZ + 1;
        var tx =
            Fade(
                sampleX -
                Math.Floor(sampleX));
        var tz =
            Fade(
                sampleZ -
                Math.Floor(sampleZ));

        var a =
            SignedUnit(
                Sample2D(
                    seed,
                    domain,
                    gridX,
                    gridZ));
        var b =
            SignedUnit(
                Sample2D(
                    seed,
                    domain,
                    nextX,
                    gridZ));
        var c =
            SignedUnit(
                Sample2D(
                    seed,
                    domain,
                    gridX,
                    nextZ));
        var d =
            SignedUnit(
                Sample2D(
                    seed,
                    domain,
                    nextX,
                    nextZ));

        return Lerp(
            Lerp(a, b, tx),
            Lerp(c, d, tx),
            tz);
    }

    public static double ValueNoise2D(
        ulong seed,
        GenerationDomain domain,
        int x,
        int z,
        uint scale)
    {
        if (scale < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale));
        }

        var scaleValue =
            (long)scale;
        var cellX =
            FloorDivRem(
                x,
                scaleValue,
                out var remainderX);
        var cellZ =
            FloorDivRem(
                z,
                scaleValue,
                out var remainderZ);
        var x0 =
            checked((int)cellX);
        var z0 =
            checked((int)cellZ);
        var x1 =
            checked(
                x0 + 1);
        var z1 =
            checked(
                z0 + 1);
        var tx =
            SmoothStep(
                remainderX /
                (double)scale);
        var tz =
            SmoothStep(
                remainderZ /
                (double)scale);

        var n00 =
            SignedUnit(
                Sample2D(
                    seed,
                    domain,
                    x0,
                    z0));
        var n10 =
            SignedUnit(
                Sample2D(
                    seed,
                    domain,
                    x1,
                    z0));
        var n01 =
            SignedUnit(
                Sample2D(
                    seed,
                    domain,
                    x0,
                    z1));
        var n11 =
            SignedUnit(
                Sample2D(
                    seed,
                    domain,
                    x1,
                    z1));

        return Lerp(
            Lerp(
                n00,
                n10,
                tx),
            Lerp(
                n01,
                n11,
                tx),
            tz);
    }

    public static double Unit(
        ulong value) =>
        (value >> 11) /
        (double)((1UL << 53) - 1UL);

    public static double SignedUnit(
        ulong value) =>
        Unit(value) * 2d - 1d;

    public static double SmoothStep(
        double value)
    {
        var clamped =
            Math.Clamp(
                value,
                0d,
                1d);
        return clamped *
               clamped *
               (3d - 2d * clamped);
    }

    private static ulong Mix(
        ulong value)
    {
        value ^=
            value >> 30;
        value *=
            0xbf58476d1ce4e5b9UL;
        value ^=
            value >> 27;
        value *=
            0x94d049bb133111ebUL;
        value ^=
            value >> 31;
        return value;
    }

    private static double Fade(
        double value) =>
        value *
        value *
        value *
        (value *
         (value * 6d - 15d) +
         10d);

    private static double Lerp(
        double left,
        double right,
        double amount) =>
        left +
        (right - left) *
        amount;

    private static int ClampToInt(
        double value) =>
        value switch
        {
            <= int.MinValue => int.MinValue,
            >= int.MaxValue => int.MaxValue,
            _ => (int)value,
        };
}
