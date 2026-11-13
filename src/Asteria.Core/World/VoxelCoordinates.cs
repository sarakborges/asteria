namespace Asteria.Core.World;

public readonly record struct ChunkCoord(int X, int Y, int Z)
{
    public static ChunkCoord Zero => default;
}

/// <summary>
/// Horizontal identity of a vertical column of 16x16x16 chunks.
/// This is a scheduling/query coordinate only; section content remains owned
/// by the corresponding three-dimensional ChunkCoord values.
/// </summary>
public readonly record struct ChunkColumnCoord(int X, int Z) :
    IComparable<ChunkColumnCoord>
{
    public static ChunkColumnCoord FromSection(ChunkCoord section) =>
        new(section.X, section.Z);

    public ChunkCoord ToSection(int sectionY) =>
        new(X, sectionY, Z);

    public int CompareTo(ChunkColumnCoord other)
    {
        var result = X.CompareTo(other.X);
        return result != 0 ? result : Z.CompareTo(other.Z);
    }
}

public readonly record struct LocalVoxelCoord(int X, int Y, int Z);

public readonly record struct ChunkVoxelAddress(ChunkCoord Chunk, LocalVoxelCoord Local);

public static class VoxelCoordinates
{
    public static ChunkVoxelAddress FromWorld(int x, int y, int z)
    {
        var (chunkX, localX) = SplitAxis(x);
        var (chunkY, localY) = SplitAxis(y);
        var (chunkZ, localZ) = SplitAxis(z);

        return new ChunkVoxelAddress(
            new ChunkCoord(chunkX, chunkY, chunkZ),
            new LocalVoxelCoord(localX, localY, localZ));
    }

    public static (int X, int Y, int Z) ToWorld(ChunkCoord chunk, LocalVoxelCoord local)
    {
        if (!Chunk.Contains(local.X, local.Y, local.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(local), "Local voxel coordinate must be inside a chunk.");
        }

        return (
            checked(chunk.X * Chunk.Size + local.X),
            checked(chunk.Y * Chunk.Size + local.Y),
            checked(chunk.Z * Chunk.Size + local.Z));
    }

    public static (int X, int Y, int Z) ChunkOrigin(ChunkCoord chunk) =>
        (
            checked(chunk.X * Chunk.Size),
            checked(chunk.Y * Chunk.Size),
            checked(chunk.Z * Chunk.Size));

    public static void VisitChunkCoordsWhoseVoxelHaloContains(
        WorldVoxelCoord worldPosition,
        Action<ChunkCoord> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        var address = FromWorld(
            worldPosition.X,
            worldPosition.Y,
            worldPosition.Z);
        var (minX, maxX) =
            HaloAxisRange(address.Local.X);
        var (minY, maxY) =
            HaloAxisRange(address.Local.Y);
        var (minZ, maxZ) =
            HaloAxisRange(address.Local.Z);

        for (var yOffset = minY;
             yOffset <= maxY;
             yOffset++)
        {
            for (var zOffset = minZ;
                 zOffset <= maxZ;
                 zOffset++)
            {
                for (var xOffset = minX;
                     xOffset <= maxX;
                     xOffset++)
                {
                    visit(new ChunkCoord(
                        address.Chunk.X + xOffset,
                        address.Chunk.Y + yOffset,
                        address.Chunk.Z + zOffset));
                }
            }
        }
    }

    private static (int Min, int Max)
        HaloAxisRange(int local) =>
        local switch
        {
            0 => (-1, 0),
            Chunk.Size - 1 => (0, 1),
            _ => (0, 0),
        };

    private static (int Chunk, int Local) SplitAxis(int world)
    {
        var chunk = Math.DivRem(world, Chunk.Size, out var local);
        if (local < 0)
        {
            chunk--;
            local += Chunk.Size;
        }

        return (chunk, local);
    }
}
