using System.Buffers;

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
    // Prepared value-noise coordinates share lattice indices and interpolation
    // factors across several independent domains. Geology samples many domains
    // at the same transformed position, so avoid repeating floor/division work.
    internal readonly struct ValueNoise3DCoordinates
    {
        private readonly ulong _c000;
        private readonly ulong _c100;
        private readonly ulong _c010;
        private readonly ulong _c110;
        private readonly ulong _c001;
        private readonly ulong _c101;
        private readonly ulong _c011;
        private readonly ulong _c111;
        private readonly double _tx;
        private readonly double _ty;
        private readonly double _tz;

        internal ValueNoise3DCoordinates(
            double x, double y, double z,
            uint horizontalScale, uint verticalScale)
        {
            var gx = x / horizontalScale;
            var gy = y / verticalScale;
            var gz = z / horizontalScale;
            var floorX = Math.Floor(gx);
            var floorY = Math.Floor(gy);
            var floorZ = Math.Floor(gz);
            var x0 = WorldGenerationEntropy.ClampToInt(floorX);
            var y0 = WorldGenerationEntropy.ClampToInt(floorY);
            var z0 = WorldGenerationEntropy.ClampToInt(floorZ);
            var x1 = x0 == int.MaxValue ? int.MaxValue : x0 + 1;
            var y1 = y0 == int.MaxValue ? int.MaxValue : y0 + 1;
            var z1 = z0 == int.MaxValue ? int.MaxValue : z0 + 1;

            var x0Hash = WorldGenerationEntropy.HashXCoordinate(x0);
            var x1Hash = WorldGenerationEntropy.HashXCoordinate(x1);
            var y0Hash = WorldGenerationEntropy.HashYCoordinate(y0);
            var y1Hash = WorldGenerationEntropy.HashYCoordinate(y1);
            var z0Hash = WorldGenerationEntropy.HashZCoordinate(z0);
            var z1Hash = WorldGenerationEntropy.HashZCoordinate(z1);
            _c000 = x0Hash ^ y0Hash ^ z0Hash;
            _c100 = x1Hash ^ y0Hash ^ z0Hash;
            _c010 = x0Hash ^ y1Hash ^ z0Hash;
            _c110 = x1Hash ^ y1Hash ^ z0Hash;
            _c001 = x0Hash ^ y0Hash ^ z1Hash;
            _c101 = x1Hash ^ y0Hash ^ z1Hash;
            _c011 = x0Hash ^ y1Hash ^ z1Hash;
            _c111 = x1Hash ^ y1Hash ^ z1Hash;
            _tx = WorldGenerationEntropy.SmoothStep(gx - floorX);
            _ty = WorldGenerationEntropy.SmoothStep(gy - floorY);
            _tz = WorldGenerationEntropy.SmoothStep(gz - floorZ);
        }

        internal double Sample(ulong seed, GenerationDomain domain)
        {
            var domainSeed = seed ^ domain.Key;
            var firstZ = WorldGenerationEntropy.Lerp(
                WorldGenerationEntropy.Lerp(
                    WorldGenerationEntropy.SignedUnit(
                        WorldGenerationEntropy.Mix(domainSeed ^ _c000)),
                    WorldGenerationEntropy.SignedUnit(
                        WorldGenerationEntropy.Mix(domainSeed ^ _c100)),
                    _tx),
                WorldGenerationEntropy.Lerp(
                    WorldGenerationEntropy.SignedUnit(
                        WorldGenerationEntropy.Mix(domainSeed ^ _c010)),
                    WorldGenerationEntropy.SignedUnit(
                        WorldGenerationEntropy.Mix(domainSeed ^ _c110)),
                    _tx),
                _ty);
            var secondZ = WorldGenerationEntropy.Lerp(
                WorldGenerationEntropy.Lerp(
                    WorldGenerationEntropy.SignedUnit(
                        WorldGenerationEntropy.Mix(domainSeed ^ _c001)),
                    WorldGenerationEntropy.SignedUnit(
                        WorldGenerationEntropy.Mix(domainSeed ^ _c101)),
                    _tx),
                WorldGenerationEntropy.Lerp(
                    WorldGenerationEntropy.SignedUnit(
                        WorldGenerationEntropy.Mix(domainSeed ^ _c011)),
                    WorldGenerationEntropy.SignedUnit(
                        WorldGenerationEntropy.Mix(domainSeed ^ _c111)),
                    _tx),
                _ty);
            return WorldGenerationEntropy.Lerp(firstZ, secondZ, _tz);
        }

        internal double Sample(
            ulong seed,
            GenerationDomain domain,
            ValueNoise3DLatticeCache latticeCache)
        {
            ArgumentNullException.ThrowIfNull(latticeCache);
            latticeCache.EnsureAlive();
            var domainSeed = seed ^ domain.Key;
            var firstZ = WorldGenerationEntropy.Lerp(
                WorldGenerationEntropy.Lerp(
                    latticeCache.SampleUnchecked(domainSeed ^ _c000),
                    latticeCache.SampleUnchecked(domainSeed ^ _c100),
                    _tx),
                WorldGenerationEntropy.Lerp(
                    latticeCache.SampleUnchecked(domainSeed ^ _c010),
                    latticeCache.SampleUnchecked(domainSeed ^ _c110),
                    _tx),
                _ty);
            var secondZ = WorldGenerationEntropy.Lerp(
                WorldGenerationEntropy.Lerp(
                    latticeCache.SampleUnchecked(domainSeed ^ _c001),
                    latticeCache.SampleUnchecked(domainSeed ^ _c101),
                    _tx),
                WorldGenerationEntropy.Lerp(
                    latticeCache.SampleUnchecked(domainSeed ^ _c011),
                    latticeCache.SampleUnchecked(domainSeed ^ _c111),
                    _tx),
                _ty);
            return WorldGenerationEntropy.Lerp(firstZ, secondZ, _tz);
        }
    }

    /// <summary>
    /// Bounded, chunk-local memoization of domain-salted 3D lattice values.
    /// Direct-mapped collisions only evict an entry; they never affect results.
    /// </summary>
    internal sealed class ValueNoise3DLatticeCache : IDisposable
    {
        private const int DefaultCapacity = 16384;

        private readonly int _mask;
        private readonly ulong[] _keys;
        private readonly double[] _values;
        private readonly bool[] _occupied;
        private bool _disposed;

        internal ValueNoise3DLatticeCache(int capacity = DefaultCapacity)
        {
            if (capacity < 1 || (capacity & (capacity - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _mask = capacity - 1;
            _keys = ArrayPool<ulong>.Shared.Rent(capacity);
            _values = ArrayPool<double>.Shared.Rent(capacity);
            _occupied = ArrayPool<bool>.Shared.Rent(capacity);
            Array.Clear(_occupied, 0, capacity);
        }

        internal void EnsureAlive() =>
            ObjectDisposedException.ThrowIf(_disposed, this);

        internal double Sample(ulong domainCoordinate)
        {
            EnsureAlive();
            return SampleUnchecked(domainCoordinate);
        }

        // Callers sampling a full 3D value-noise lattice validate the cache
        // once before resolving its eight corners, avoiding eight repeated
        // lifetime and nullable-buffer checks for the same query.
        internal double SampleUnchecked(ulong domainCoordinate)
        {
            var folded = domainCoordinate ^ (domainCoordinate >> 32);
            var index = (int)(folded & (uint)_mask);
            if (_occupied[index] && _keys[index] == domainCoordinate)
                return _values[index];

            var value = WorldGenerationEntropy.SignedUnit(
                WorldGenerationEntropy.Mix(domainCoordinate));
            _keys[index] = domainCoordinate;
            _values[index] = value;
            _occupied[index] = true;
            return value;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ArrayPool<ulong>.Shared.Return(_keys);
            ArrayPool<double>.Shared.Return(_values);
            ArrayPool<bool>.Shared.Return(_occupied);
        }
    }

    internal static ValueNoise3DCoordinates PrepareValueNoise3D(
        double x, double y, double z,
        uint horizontalScale, uint verticalScale)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            throw new ArgumentOutOfRangeException(nameof(x), "Noise coordinates must be finite.");
        if (horizontalScale < 2 || verticalScale < 2)
            throw new ArgumentOutOfRangeException(nameof(horizontalScale));

        return new ValueNoise3DCoordinates(
            x, y, z, horizontalScale, verticalScale);
    }
    /// <summary>
    /// Chunk-local sampler for one 3D value-noise channel. Adjacent voxels share
    /// lattice corners, so materialization hashes the compact lattice once and
    /// reuses it instead of recomputing eight corner hashes per voxel.
    /// </summary>
    internal sealed class ValueNoise3DGridSampler
    {
        private readonly record struct AxisSample(int Lower, double Blend);

        private readonly AxisSample[] _x;
        private readonly AxisSample[] _y;
        private readonly AxisSample[] _z;
        private readonly double[] _lattice;
        private readonly int _minimumX;
        private readonly int _minimumY;
        private readonly int _minimumZ;
        private readonly int _latticeWidth;
        private readonly int _latticeHeight;
        private readonly int _latticeDepth;
        private readonly double[] _xInterpolatedColumn;
        private readonly double[] _yInterpolatedColumn;
        private int _cachedLocalX = -1;

        public ValueNoise3DGridSampler(
            ulong seed,
            GenerationDomain domain,
            int originX,
            int originY,
            int originZ,
            int width,
            int height,
            int depth,
            uint horizontalScale,
            uint verticalScale)
        {
            if (originY < 0 ||
                width <= 0 ||
                height <= 0 ||
                depth <= 0 ||
                horizontalScale < 2 ||
                verticalScale < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            _ = checked(originX + width - 1);
            _ = checked(originY + height - 1);
            _ = checked(originZ + depth - 1);

            _x = PrepareAxis(originX, width, horizontalScale,
                out _minimumX, out var maximumX);
            _y = PrepareAxis(originY, height, verticalScale,
                out _minimumY, out var maximumY);
            _z = PrepareAxis(originZ, depth, horizontalScale,
                out _minimumZ, out var maximumZ);

            _latticeWidth = checked(maximumX - _minimumX + 1);
            _latticeHeight = checked(maximumY - _minimumY + 1);
            _latticeDepth = checked(maximumZ - _minimumZ + 1);
            _lattice = new double[
                checked(checked(_latticeWidth * _latticeHeight) * _latticeDepth)];
            // A materialization samples every X/Z column at several Y values.
            // Cache the X-interpolated lattice planes for that active column;
            // the remaining Y/Z interpolation keeps the exact scalar order.
            _xInterpolatedColumn = new double[
                checked(_latticeHeight * _latticeDepth)];
            // After X interpolation, each local-Y interpolation is also
            // independent of local Z. Cache those values for every lattice-Z
            // plane so adjacent columns need only the final Z interpolation.
            _yInterpolatedColumn = new double[
                checked(_y.Length * _latticeDepth)];

            for (var z = _minimumZ; z <= maximumZ; z++)
            for (var y = _minimumY; y <= maximumY; y++)
            for (var x = _minimumX; x <= maximumX; x++)
            {
                _lattice[Index(x, y, z)] =
                    WorldGenerationEntropy.SignedUnit(
                        WorldGenerationEntropy.Sample3D(seed, domain, x, y, z));
            }
        }

        public double Sample(int localX, int localY, int localZ)
        {
            if ((uint)localX >= (uint)_x.Length ||
                (uint)localY >= (uint)_y.Length ||
                (uint)localZ >= (uint)_z.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(localX));
            }

            if (_cachedLocalX != localX)
                PrepareXInterpolatedColumn(localX);

            var z = _z[localZ];
            var yRowOffset = localY * _latticeDepth;
            var zOffset = z.Lower - _minimumZ;
            return Lerp(
                _yInterpolatedColumn[yRowOffset + zOffset],
                _yInterpolatedColumn[yRowOffset + zOffset + 1],
                z.Blend);
        }

        private void PrepareXInterpolatedColumn(int localX)
        {
            // X interpolation is independent of the requested local Z. Keep
            // it across every Z column in this batch, especially for the
            // one-column-wide horizontal boundary reads.
            var x = _x[localX];
            var x0 = x.Lower;
            var x1 = x0 + 1;

            for (var zOffset = 0; zOffset < _latticeDepth; zOffset++)
            {
                var latticeZ = _minimumZ + zOffset;
                var rowOffset = zOffset * _latticeHeight;
                for (var yOffset = 0; yOffset < _latticeHeight; yOffset++)
                {
                    var latticeY = _minimumY + yOffset;
                    _xInterpolatedColumn[rowOffset + yOffset] = Lerp(
                        Lattice(x0, latticeY, latticeZ),
                        Lattice(x1, latticeY, latticeZ),
                        x.Blend);
                }
            }

            for (var localY = 0; localY < _y.Length; localY++)
            {
                var y = _y[localY];
                var yOffset = y.Lower - _minimumY;
                var targetRowOffset = localY * _latticeDepth;
                for (var zOffset = 0; zOffset < _latticeDepth; zOffset++)
                {
                    var sourceRowOffset = zOffset * _latticeHeight;
                    _yInterpolatedColumn[targetRowOffset + zOffset] = Lerp(
                        _xInterpolatedColumn[sourceRowOffset + yOffset],
                        _xInterpolatedColumn[sourceRowOffset + yOffset + 1],
                        y.Blend);
                }
            }

            _cachedLocalX = localX;
        }

        private double Lattice(int x, int y, int z) =>
            _lattice[
                ((z - _minimumZ) * _latticeHeight +
                 (y - _minimumY)) * _latticeWidth +
                (x - _minimumX)];

        private static AxisSample[] PrepareAxis(
            int origin,
            int length,
            uint scale,
            out int minimumLattice,
            out int maximumLattice)
        {
            var result = new AxisSample[length];
            minimumLattice = int.MaxValue;
            maximumLattice = int.MinValue;
            for (var index = 0; index < length; index++)
            {
                var world = checked(origin + index);
                var lower = checked((int)FloorDivRem(world, scale, out var remainder));
                result[index] = new AxisSample(
                    lower,
                    WorldGenerationEntropy.SmoothStep(remainder / (double)scale));
                minimumLattice = Math.Min(minimumLattice, lower);
                maximumLattice = Math.Max(maximumLattice, checked(lower + 1));
            }

            return result;
        }

        private static long FloorDivRem(
            int value,
            long divisor,
            out long remainder)
        {
            var quotient = Math.DivRem((long)value, divisor, out remainder);
            if (remainder >= 0)
                return quotient;

            remainder += divisor;
            return quotient - 1;
        }

        private static double Lerp(double left, double right, double amount) =>
            left + (right - left) * amount;

        private int Index(int x, int y, int z) =>
            ((z - _minimumZ) * _latticeHeight +
             (y - _minimumY)) * _latticeWidth +
            (x - _minimumX);
    }

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

    private const ulong XCoordinateMultiplier = 0x9e3779b185ebca87UL;
    private const ulong YCoordinateMultiplier = 0x165667b19e3779f9UL;
    private const ulong ZCoordinateMultiplier = 0xc2b2ae3d27d4eb4fUL;

    private static ulong HashXCoordinate(int x) =>
        unchecked((ulong)(uint)x * XCoordinateMultiplier);

    private static ulong HashYCoordinate(int y) =>
        unchecked((ulong)(uint)y * YCoordinateMultiplier);

    private static ulong HashZCoordinate(int z) =>
        unchecked((ulong)(uint)z * ZCoordinateMultiplier);

    private static ulong Sample3DHashed(
        ulong seed,
        GenerationDomain domain,
        ulong coordinateHash)
    {
        unchecked
        {
            return Mix(seed ^ domain.Key ^ coordinateHash);
        }
    }

    public static ulong Sample3D(
        ulong seed,
        GenerationDomain domain,
        int x,
        int y,
        int z) =>
        Sample3DHashed(
            seed,
            domain,
            HashXCoordinate(x) ^
            HashYCoordinate(y) ^
            HashZCoordinate(z));

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

        var firstZ = SampleValueNoise3DSlice(
            seed, domain, xBase, x1, yBase, y1, zBase, tx, ty);
        var secondZ = SampleValueNoise3DSlice(
            seed, domain, xBase, x1, yBase, y1, z1, tx, ty);
        return Lerp(firstZ, secondZ, tz);
    }

    private static double SampleValueNoise3DSlice(
        ulong seed,
        GenerationDomain domain,
        int x0,
        int x1,
        int y0,
        int y1,
        int z,
        double tx,
        double ty)
    {
        var lowerLeft = SignedUnit(Sample3D(seed, domain, x0, y0, z));
        var lowerRight = SignedUnit(Sample3D(seed, domain, x1, y0, z));
        var upperLeft = SignedUnit(Sample3D(seed, domain, x0, y1, z));
        var upperRight = SignedUnit(Sample3D(seed, domain, x1, y1, z));
        return Lerp(
            Lerp(lowerLeft, lowerRight, tx),
            Lerp(upperLeft, upperRight, tx),
            ty);
    }

    /// <summary>
    /// Continuous-coordinate 3D value noise. This overload supports rotated
    /// world-space sampling without rounding transformed coordinates to voxels.
    /// </summary>
    public static double ValueNoise3D(
        ulong seed,
        GenerationDomain domain,
        double x,
        double y,
        double z,
        uint horizontalScale,
        uint verticalScale) =>
        PrepareValueNoise3D(
            x, y, z, horizontalScale, verticalScale)
        .Sample(seed, domain);

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
