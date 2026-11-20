namespace Asteria.Core.World;

/// <summary>
/// Builds a compact per-column mask of solid voxels that are within the
/// authored directional-palette depth of any empty voxel along a cardinal
/// axis. Empty bits are expanded toward solids with word shifts; no density
/// samples or per-voxel neighborhood queries are performed here.
/// </summary>
internal static class DirectionalFaceCandidateMask
{
    private enum Axis : byte
    {
        X,
        Z,
        Y,
    }

    public static ushort[] Build(
        int regionSize,
        int regionWordCount,
        int maximumDepth,
        ulong[] xSolidRows,
        ulong[] zSolidRows,
        ulong[] ySolidRows)
    {
        ArgumentNullException.ThrowIfNull(xSolidRows);
        ArgumentNullException.ThrowIfNull(zSolidRows);
        ArgumentNullException.ThrowIfNull(ySolidRows);
        if (maximumDepth < 0 ||
            regionSize != Chunk.Size + maximumDepth * 2 ||
            regionWordCount != (regionSize + 63) / 64)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        }

        var expectedLength = checked(Chunk.Area * regionWordCount);
        if (xSolidRows.Length != expectedLength ||
            zSolidRows.Length != expectedLength ||
            ySolidRows.Length != expectedLength)
        {
            throw new ArgumentException(
                "Directional occupancy rows do not match the region dimensions.");
        }

        var candidates = new ushort[Chunk.Area];
        if (maximumDepth == 0)
            return candidates;

        AddAxisCandidates(
            xSolidRows, regionSize, regionWordCount, maximumDepth,
            Axis.X, candidates);
        AddAxisCandidates(
            zSolidRows, regionSize, regionWordCount, maximumDepth,
            Axis.Z, candidates);
        AddAxisCandidates(
            ySolidRows, regionSize, regionWordCount, maximumDepth,
            Axis.Y, candidates);
        return candidates;
    }

    private static void AddAxisCandidates(
        ulong[] rows,
        int regionSize,
        int wordCount,
        int maximumDepth,
        Axis axis,
        ushort[] candidates)
    {
        var regionMask = regionSize >= 64
            ? ulong.MaxValue
            : (1UL << regionSize) - 1UL;
        var localRangeMask =
            ((1UL << Chunk.Size) - 1UL) << maximumDepth;

        for (var row = 0; row < Chunk.Area; row++)
        {
            var rowOffset = row * wordCount;
            if (wordCount == 1)
            {
                var solid = rows[rowOffset] & regionMask;
                var empty = ~solid & regionMask;
                var influenced = 0UL;
                for (var distance = 1; distance <= maximumDepth; distance++)
                {
                    influenced |= empty >> distance;
                    influenced |= empty << distance;
                }

                influenced &= solid & localRangeMask;
                while (influenced != 0)
                {
                    var regionIndex =
                        System.Numerics.BitOperations.TrailingZeroCount(influenced);
                    MarkCandidate(axis, row, regionIndex - maximumDepth, candidates);
                    influenced &= influenced - 1;
                }

                continue;
            }

            // General path for authored depths whose halo spans multiple words.
            for (var local = 0; local < Chunk.Size; local++)
            {
                var sourceIndex = maximumDepth + local;
                if (!IsSolid(rows, rowOffset, sourceIndex))
                    continue;

                var hasEmptyWithinDepth = false;
                for (var distance = 1;
                     distance <= maximumDepth && !hasEmptyWithinDepth;
                     distance++)
                {
                    hasEmptyWithinDepth =
                        !IsSolid(rows, rowOffset, sourceIndex - distance) ||
                        !IsSolid(rows, rowOffset, sourceIndex + distance);
                }

                if (hasEmptyWithinDepth)
                    MarkCandidate(axis, row, local, candidates);
            }
        }
    }

    private static bool IsSolid(
        ulong[] rows,
        int rowOffset,
        int bitIndex)
    {
        var word = bitIndex >> 6;
        var bit = 1UL << (bitIndex & 63);
        return (rows[rowOffset + word] & bit) != 0;
    }

    private static void MarkCandidate(
        Axis axis,
        int row,
        int localCoordinate,
        ushort[] candidates)
    {
        int columnIndex;
        int y;
        switch (axis)
        {
            case Axis.X:
                y = row / Chunk.Size;
                var z = row % Chunk.Size;
                columnIndex = z * Chunk.Size + localCoordinate;
                break;
            case Axis.Z:
                y = row / Chunk.Size;
                var x = row % Chunk.Size;
                columnIndex = localCoordinate * Chunk.Size + x;
                break;
            case Axis.Y:
                var localZ = row / Chunk.Size;
                var localX = row % Chunk.Size;
                columnIndex = localZ * Chunk.Size + localX;
                y = localCoordinate;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(axis));
        }

        candidates[columnIndex] |= (ushort)(1u << y);
    }
}
