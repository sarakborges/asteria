namespace Asteria.Core.World;

public sealed class Chunk
{
    public const int Size = 16;
    public const int Area = Size * Size;
    public const int Volume = Area * Size;

    private readonly PaletteStorage<VoxelCell> _cells = new();
    private readonly PaletteStorage<FluidCell> _fluids = new();
    private readonly MicroblockMaskPalette _microblockMasks = new();
    private readonly VoxelLight[] _light = new VoxelLight[Volume];

    public ulong Revision { get; private set; }

    public int NonEmptyVoxelCount => _cells.OccupiedCount;

    public int PaletteEntryCount => _cells.ActivePaletteEntryCount;

    public int FluidCount => _fluids.OccupiedCount;

    public int FluidPaletteEntryCount => _fluids.ActivePaletteEntryCount;

    public int MicroblockMaskCount => _microblockMasks.Count;

    public bool IsEmpty =>
        NonEmptyVoxelCount == 0 &&
        FluidCount == 0;

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

    public FluidCell GetFluid(int x, int y, int z)
    {
        ValidateCoordinates(x, y, z);
        return _fluids.Get(ToIndex(x, y, z));
    }

    public FluidCell GetFluidOrEmpty(int x, int y, int z)
    {
        if (!Contains(x, y, z))
        {
            return FluidCell.Empty;
        }

        return _fluids.Get(ToIndex(x, y, z));
    }

    public bool SetFluid(
        int x,
        int y,
        int z,
        FluidCell fluid)
    {
        ValidateCoordinates(x, y, z);

        if (!_fluids.Set(
                ToIndex(x, y, z),
                fluid))
        {
            return false;
        }

        Revision++;
        return true;
    }

    public void VisitFluidCells(
        Action<int, int, int, FluidCell> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        for (var y = 0; y < Size; y++)
        {
            for (var z = 0; z < Size; z++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var fluid =
                        GetFluid(x, y, z);

                    if (!fluid.IsEmpty)
                    {
                        visit(x, y, z, fluid);
                    }
                }
            }
        }
    }

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

    public Chunk CloneForWorker()
    {
        var clone = new Chunk();

        for (var y = 0; y < Size; y++)
        {
            for (var z = 0; z < Size; z++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var cell = GetCell(x, y, z);
                    var fluid = GetFluid(x, y, z);

                    if (!fluid.IsEmpty)
                    {
                        clone.SetFluid(
                            x,
                            y,
                            z,
                            fluid);
                    }

                    if (cell.IsEmpty)
                    {
                        continue;
                    }

                    if (cell.HasMicroblockGeometry)
                    {
                        clone.SetCell(
                            x,
                            y,
                            z,
                            cell.WithMicroblockMaskId(0));
                        clone.SetMicroblockMask(
                            x,
                            y,
                            z,
                            GetMicroblockMask(x, y, z));
                    }
                    else
                    {
                        clone.SetCell(x, y, z, cell);
                    }
                }
            }
        }

        Array.Copy(_light, clone._light, Volume);
        clone.Revision = Revision;
        return clone;
    }

    public void CopyLightFrom(Chunk source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Array.Copy(source._light, _light, Volume);
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
