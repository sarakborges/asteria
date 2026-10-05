namespace Asteria.Core.World;

public readonly record struct ChunkCoord(int X, int Y, int Z);

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
