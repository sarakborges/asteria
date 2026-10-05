namespace Asteria.Core.World;

public sealed class Chunk
{
    public const int SizeX = 32;
    public const int SizeY = 32;
    public const int SizeZ = 32;

    private readonly BlockId[] _blocks = new BlockId[SizeX * SizeY * SizeZ];

    public BlockId GetBlock(int x, int y, int z)
    {
        ValidateCoordinates(x, y, z);
        return _blocks[ToIndex(x, y, z)];
    }

    public BlockId GetBlockOrAir(int x, int y, int z)
    {
        if ((uint)x >= SizeX || (uint)y >= SizeY || (uint)z >= SizeZ)
        {
            return BlockId.Air;
        }

        return _blocks[ToIndex(x, y, z)];
    }

    public void SetBlock(int x, int y, int z, BlockId block)
    {
        ValidateCoordinates(x, y, z);
        _blocks[ToIndex(x, y, z)] = block;
    }

    private static int ToIndex(int x, int y, int z) => x + SizeX * (z + SizeZ * y);

    private static void ValidateCoordinates(int x, int y, int z)
    {
        if ((uint)x >= SizeX || (uint)y >= SizeY || (uint)z >= SizeZ)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Voxel ({x}, {y}, {z}) is outside the chunk.");
        }
    }
}
