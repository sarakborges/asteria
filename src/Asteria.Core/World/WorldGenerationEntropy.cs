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

    public static ulong Sample3D(
        ulong seed,
        GenerationDomain domain,
        int x,
        int y,
        int z)
    {
        unchecked
        {
            return Mix(
                seed ^
                domain.Key ^
                ((ulong)(uint)x * 0x9e3779b185ebca87UL) ^
                ((ulong)(uint)y * 0x165667b19e3779f9UL) ^
                ((ulong)(uint)z * 0xc2b2ae3d27d4eb4fUL));
        }
    }

    public static double ValueNoise3D(
        ulong seed,
        GenerationDomain domain,
        int x,
        int y,
        int z,
        uint horizontalScale,
        uint verticalScale)
    {
        if (horizontalScale < 2 || verticalScale < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalScale));
        }

        var x0 = FloorDivRem(x, horizontalScale, out var rx);
        var y0 = FloorDivRem(y, verticalScale, out var ry);
        var z0 = FloorDivRem(z, horizontalScale, out var rz);
        var x1 = checked((int)x0 + 1);
        var y1 = checked((int)y0 + 1);
        var z1 = checked((int)z0 + 1);
        var xBase = checked((int)x0);
        var yBase = checked((int)y0);
        var zBase = checked((int)z0);

        var tx = SmoothStep(rx / (double)horizontalScale);
        var ty = SmoothStep(ry / (double)verticalScale);
        var tz = SmoothStep(rz / (double)horizontalScale);

        double Slice(int layerZ) =>
            Lerp(
                Lerp(
                    SignedUnit(Sample3D(seed, domain, xBase, yBase, layerZ)),
                    SignedUnit(Sample3D(seed, domain, x1, yBase, layerZ)),
                    tx),
                Lerp(
                    SignedUnit(Sample3D(seed, domain, xBase, y1, layerZ)),
                    SignedUnit(Sample3D(seed, domain, x1, y1, layerZ)),
                    tx),
                ty);

        return Lerp(Slice(zBase), Slice(z1), tz);
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
