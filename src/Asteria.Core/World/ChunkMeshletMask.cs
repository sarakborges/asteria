namespace Asteria.Core.World;

public readonly record struct ChunkMeshletMask(byte Bits)
{
    public const int Edge = 8;
    public const int PerAxis = Chunk.Size / Edge;
    public const int Count = PerAxis * PerAxis * PerAxis;

    public static ChunkMeshletMask None => default;

    public static ChunkMeshletMask All => new(byte.MaxValue);

    public bool IsEmpty => Bits == 0;

    public bool IsAll => Bits == byte.MaxValue;

    public int SelectedCount => System.Numerics.BitOperations.PopCount(Bits);

    public ChunkMeshletMask Union(ChunkMeshletMask other) =>
        new((byte)(Bits | other.Bits));

    public ChunkMeshletMask Except(
        ChunkMeshletMask other) =>
        new(
            (byte)(
                Bits &
                ~other.Bits));

    public bool ContainsIndex(int index)
    {
        ValidateIndex(index);
        return (Bits & (1 << index)) != 0;
    }

    public IEnumerable<int> Indices()
    {
        for (var index = 0; index < Count; index++)
        {
            if (ContainsIndex(index))
            {
                yield return index;
            }
        }
    }

    public static ChunkMeshletMask Single(int index)
    {
        ValidateIndex(index);
        return new ChunkMeshletMask((byte)(1 << index));
    }

    public static ChunkMeshletMask ForWorldPosition(
        ChunkCoord chunkCoord,
        WorldVoxelCoord worldPosition)
    {
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(chunkCoord);
        var localX = worldPosition.X - originX;
        var localY = worldPosition.Y - originY;
        var localZ = worldPosition.Z - originZ;
        byte bits = 0;

        for (var meshletY = 0; meshletY < PerAxis; meshletY++)
        {
            for (var meshletZ = 0; meshletZ < PerAxis; meshletZ++)
            {
                for (var meshletX = 0; meshletX < PerAxis; meshletX++)
                {
                    var minX = meshletX * Edge;
                    var minY = meshletY * Edge;
                    var minZ = meshletZ * Edge;
                    var maxX = minX + Edge;
                    var maxY = minY + Edge;
                    var maxZ = minZ + Edge;

                    if (localX < minX - 1 ||
                        localY < minY - 1 ||
                        localZ < minZ - 1 ||
                        localX > maxX ||
                        localY > maxY ||
                        localZ > maxZ)
                    {
                        continue;
                    }

                    bits |= (byte)(1 << Index(
                        meshletX,
                        meshletY,
                        meshletZ));
                }
            }
        }

        return new ChunkMeshletMask(bits);
    }

    public static ChunkMeshletMask ForDependencyOffset(
        int x,
        int y,
        int z)
    {
        if (x is < -1 or > 1 ||
            y is < -1 or > 1 ||
            z is < -1 or > 1 ||
            (x == 0 && y == 0 && z == 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Dependency offset must be a non-zero 3D neighbor offset.");
        }

        byte bits = 0;

        for (var meshletY = 0; meshletY < PerAxis; meshletY++)
        {
            if (y < 0 && meshletY != 0) continue;
            if (y > 0 && meshletY != PerAxis - 1) continue;

            for (var meshletZ = 0; meshletZ < PerAxis; meshletZ++)
            {
                if (z < 0 && meshletZ != 0) continue;
                if (z > 0 && meshletZ != PerAxis - 1) continue;

                for (var meshletX = 0; meshletX < PerAxis; meshletX++)
                {
                    if (x < 0 && meshletX != 0) continue;
                    if (x > 0 && meshletX != PerAxis - 1) continue;

                    bits |= (byte)(1 << Index(
                        meshletX,
                        meshletY,
                        meshletZ));
                }
            }
        }

        return new ChunkMeshletMask(bits);
    }

    public static (
        int MinX,
        int MinY,
        int MinZ,
        int MaxXExclusive,
        int MaxYExclusive,
        int MaxZExclusive)
        Bounds(int index)
    {
        ValidateIndex(index);

        var meshletX = index % PerAxis;
        var meshletZ = (index / PerAxis) % PerAxis;
        var meshletY = index / (PerAxis * PerAxis);

        var minX = meshletX * Edge;
        var minY = meshletY * Edge;
        var minZ = meshletZ * Edge;

        return (
            minX,
            minY,
            minZ,
            minX + Edge,
            minY + Edge,
            minZ + Edge);
    }

    private static int Index(int x, int y, int z) =>
        x + z * PerAxis + y * PerAxis * PerAxis;

    private static void ValidateIndex(int index)
    {
        if ((uint)index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
