namespace Asteria.Core.World;

public sealed class Chunk
{
    public const int Size = 16;
    public const int Area = Size * Size;
    public const int Volume = Area * Size;

    private readonly PaletteStorage<VoxelCell> _cells = new();
    private readonly MicroblockMaskPalette _microblockMasks = new();
    private readonly VoxelLight[] _light = new VoxelLight[Volume];

    public ulong Revision { get; private set; }

    public int NonEmptyVoxelCount => _cells.OccupiedCount;

    public int PaletteEntryCount => _cells.ActivePaletteEntryCount;

    public int MicroblockMaskCount => _microblockMasks.Count;

    public bool IsEmpty => NonEmptyVoxelCount == 0;

    public VoxelCell GetCell(int x, int y, int z)
    {
        ValidateCoordinates(x, y, z);
        return _cells.Get(ToIndex(x, y, z));
    }

    public VoxelCell GetCellOrEmpty(int x, int y, int z)
    {
        if (!Contains(x, y, z))
        {
            return VoxelCell.Empty;
        }

        return _cells.Get(ToIndex(x, y, z));
    }

    public BlockRuntimeId GetBlock(int x, int y, int z) => GetCell(x, y, z).Block;

    public bool SetCell(int x, int y, int z, VoxelCell cell)
    {
        ValidateCoordinates(x, y, z);
        if (!_cells.Set(ToIndex(x, y, z), cell))
        {
            return false;
        }

        Revision++;
        return true;
    }

    public bool SetBlock(int x, int y, int z, BlockRuntimeId block) =>
        SetCell(x, y, z, new VoxelCell(block));

    public VoxelLight GetLight(int x, int y, int z)
    {
        ValidateCoordinates(x, y, z);
        return _light[ToIndex(x, y, z)];
    }

    public void SetLight(int x, int y, int z, VoxelLight light)
    {
        ValidateCoordinates(x, y, z);
        _light[ToIndex(x, y, z)] = light;
    }

    public void ClearLight()
    {
        Array.Clear(_light);
    }

    public MicroblockMask GetMicroblockMask(int x, int y, int z)
    {
        var cell = GetCell(x, y, z);
        return cell.IsEmpty
            ? MicroblockMask.Empty
            : _microblockMasks.Get(cell.MicroblockMaskId);
    }

    public bool SetMicroblockMask(int x, int y, int z, MicroblockMask mask)
    {
        var cell = GetCell(x, y, z);
        if (cell.IsEmpty)
        {
            throw new InvalidOperationException("Cannot sculpt an empty voxel.");
        }

        if (mask.IsEmpty)
        {
            return SetCell(x, y, z, VoxelCell.Empty);
        }

        var maskId = _microblockMasks.Intern(mask);
        return SetCell(x, y, z, cell.WithMicroblockMaskId(maskId));
    }

    public static bool Contains(int x, int y, int z) =>
        (uint)x < Size && (uint)y < Size && (uint)z < Size;

    private static int ToIndex(int x, int y, int z) => x + Size * (z + Size * y);

    private static void ValidateCoordinates(int x, int y, int z)
    {
        if (!Contains(x, y, z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Voxel ({x}, {y}, {z}) is outside a {Size}³ chunk.");
        }
    }
}
