namespace Asteria.Core.World;

internal static class ChunkCoordOrdering
{
    public static readonly IComparer<ChunkCoord> YThenZThenX =
        Comparer<ChunkCoord>.Create(
            static (left, right) =>
            {
                var comparison =
                    left.Y.CompareTo(right.Y);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison =
                    left.Z.CompareTo(right.Z);
                return comparison != 0
                    ? comparison
                    : left.X.CompareTo(right.X);
            });
}
