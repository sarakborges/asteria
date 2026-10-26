namespace Asteria.Core.World;

public sealed class Chunk
{
    public const int Size = 16;
    public const int Area = Size * Size;
    public const int Volume = Area * Size;

    private readonly PaletteStorage<VoxelCell> _cells;
    private readonly PaletteStorage<FluidCell> _fluids;
    private readonly MicroblockMaskPalette _microblockMasks;
    private Dictionary<int, BlockSurfaceState>? _surfaceStates;
    private Dictionary<int, List<ObjectCell>>? _objects;

    public Chunk()
    {
        _cells = new PaletteStorage<VoxelCell>();
        _fluids = new PaletteStorage<FluidCell>();
        _microblockMasks = new MicroblockMaskPalette();
    }

    private Chunk(
        PaletteStorage<VoxelCell> cells,
        PaletteStorage<FluidCell> fluids,
        MicroblockMaskPalette microblockMasks,
        Dictionary<int, BlockSurfaceState>? surfaceStates,
        Dictionary<int, List<ObjectCell>>? objects,
        ulong revision,
        ulong objectRevision)
    {
        _cells = cells;
        _fluids = fluids;
        _microblockMasks = microblockMasks;
        _surfaceStates = surfaceStates;
        _objects = objects;
        Revision = revision;
        ObjectRevision = objectRevision;
    }

    public ulong Revision { get; private set; }

    /// <summary>Separate from block/fluid revisions for future batched Object presentation.</summary>
    public ulong ObjectRevision { get; private set; }

    public int ObjectCount { get; private set; }

    public const int MaximumObjectsPerSupport = 8;

    public int NonEmptyVoxelCount => _cells.OccupiedCount;

    public int PaletteEntryCount => _cells.ActivePaletteEntryCount;

    public int FluidCount => _fluids.OccupiedCount;

    public int FluidPaletteEntryCount => _fluids.ActivePaletteEntryCount;

    public int MicroblockMaskCount => _microblockMasks.Count;

    public bool IsEmpty =>
        NonEmptyVoxelCount == 0 &&
        FluidCount == 0;

    public bool HasTerrainContent =>
        NonEmptyVoxelCount > 0;

    public bool HasFluidContent =>
        FluidCount > 0;

    public ChunkMeshletMask OccupiedTerrainMeshlets() =>
        OccupiedMeshlets(_cells);

    public ChunkMeshletMask OccupiedFluidMeshlets() =>
        OccupiedMeshlets(_fluids);

    public bool HasTerrainInMeshlet(int meshletIndex) =>
        HasOccupiedMeshlet(_cells, meshletIndex);

    public bool HasFluidInMeshlet(int meshletIndex) =>
        HasOccupiedMeshlet(_fluids, meshletIndex);

    public int TerrainVoxelCountInMeshlet(int meshletIndex) =>
        CountOccupiedMeshlet(_cells, meshletIndex);

    public int FluidVoxelCountInMeshlet(int meshletIndex) =>
        CountOccupiedMeshlet(_fluids, meshletIndex);

    private static bool HasOccupiedMeshlet<T>(
        PaletteStorage<T> storage,
        int meshletIndex)
        where T : struct, IEquatable<T> =>
        storage.OccupiedCountInMeshlet(meshletIndex) > 0;

    private static int CountOccupiedMeshlet<T>(
        PaletteStorage<T> storage,
        int meshletIndex)
        where T : struct, IEquatable<T> =>
        storage.OccupiedCountInMeshlet(meshletIndex);

    private static ChunkMeshletMask OccupiedMeshlets<T>(
        PaletteStorage<T> storage)
        where T : struct, IEquatable<T>
    {
        if (storage.OccupiedCount == 0)
        {
            return ChunkMeshletMask.None;
        }

        byte bits = 0;
        for (var index = 0; index < ChunkMeshletMask.Count; index++)
        {
            if (storage.OccupiedCountInMeshlet(index) > 0)
                bits |= (byte)(1 << index);
        }

        return new ChunkMeshletMask(bits);
    }

    public bool DependencyBoundaryHasContent(
        int xOffset,
        int yOffset,
        int zOffset) =>
        DependencyBoundaryHas(
            xOffset,
            yOffset,
            zOffset,
            fluidOnly: false);

    public bool DependencyBoundaryHasFluid(
        int xOffset,
        int yOffset,
        int zOffset) =>
        DependencyBoundaryHas(
            xOffset,
            yOffset,
            zOffset,
            fluidOnly: true);

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
        return SetCellAtIndex(ToIndex(x, y, z), cell);
    }

    /// <summary>
    /// Writes a block using its already-linearized chunk index. Intended for
    /// bounded internal loops that have established X/Y/Z coordinates; normal
    /// callers keep using the coordinate-validated SetBlock/SetCell APIs.
    /// </summary>
    internal bool SetBlockAtLinearIndex(int voxelIndex, BlockRuntimeId block)
    {
        if ((uint)voxelIndex >= Volume)
            throw new ArgumentOutOfRangeException(nameof(voxelIndex));

        return SetCellAtIndex(voxelIndex, new VoxelCell(block));
    }

    private bool SetCellAtIndex(int index, VoxelCell cell)
    {
        if (!_cells.Set(index, cell, out var previous))
            return false;

        if (cell.IsEmpty || previous.Block != cell.Block)
        {
            // Surface state can only be attached to a non-empty block. Fresh
            // procedural chunks therefore need no dictionary lookup here.
            if (!previous.IsEmpty &&
                _surfaceStates is { } surfaceStates)
            {
                surfaceStates.Remove(index);
                if (surfaceStates.Count == 0)
                    _surfaceStates = null;
            }

            // ObjectCount is the authoritative aggregate for this chunk. When
            // zero, the support-index dictionary is known to be empty.
            if (ObjectCount > 0 &&
                _objects is { } objects &&
                objects.Remove(index, out var detached))
            {
                ObjectCount -= detached.Count;
                ObjectRevision++;
                if (objects.Count == 0)
                    _objects = null;
            }
        }

        Revision++;
        return true;
    }

    public bool SetBlock(int x, int y, int z, BlockRuntimeId block) =>
        SetCell(x, y, z, new VoxelCell(block));

    /// <summary>Returns a detached snapshot; callers cannot mutate storage.</summary>
    public IReadOnlyList<ObjectCell> ObjectsAt(int x, int y, int z)
    {
        ValidateCoordinates(x, y, z);
        return _objects is { } objectIndex &&
               objectIndex.TryGetValue(ToIndex(x, y, z), out var objects)
            ? objects.ToArray()
            : Array.Empty<ObjectCell>();
    }

    public bool AddObject(int x, int y, int z, ObjectCell obj)
    {
        ValidateCoordinates(x, y, z);
        if (obj.Id is null)
            throw new ArgumentException("Object must have a valid identity.", nameof(obj));
        var index = ToIndex(x, y, z);
        // A support voxel may contain a solid block or a full fluid source.
        // The Object's definition determines which substrate is legal.
        if (_cells.Get(index).IsEmpty &&
            (_fluids.Get(index).IsEmpty ||
             _fluids.Get(index).Level != FluidCell.MaxLevel ||
             !_fluids.Get(index).IsSource))
            return false;
        _objects ??= [];
        if (_objects.TryGetValue(index, out var current))
        {
            if (current.Count >= MaximumObjectsPerSupport || current.Contains(obj))
                return false;
            current.Add(obj);
        }
        else
        {
            _objects.Add(index, [obj]);
        }
        ObjectCount++;
        ObjectRevision++;
        Revision++;
        return true;
    }

    public bool RemoveObject(int x, int y, int z, ObjectCell obj)
    {
        ValidateCoordinates(x, y, z);
        var index = ToIndex(x, y, z);
        if (_objects is not { } objectIndex ||
            !objectIndex.TryGetValue(index, out var objects) ||
            !objects.Remove(obj))
            return false;
        if (objects.Count == 0)
            objectIndex.Remove(index);
        if (objectIndex.Count == 0)
            _objects = null;
        ObjectCount--;
        ObjectRevision++;
        Revision++;
        return true;
    }

    public void VisitObjectCells(
        Action<int, int, int, ObjectCell> visit, bool ordered = true)
    {
        ArgumentNullException.ThrowIfNull(visit);
        // Rendering/persistence can retain deterministic support order.
        // Spatial queries supply their own hit-distance and identity tie-breaks
        // and should not sort the entire support dictionary each frame.
        if (_objects is not { } objectIndex)
            return;

        IEnumerable<int> supports = ordered
            ? objectIndex.Keys.OrderBy(key => key)
            : objectIndex.Keys;
        foreach (var index in supports)
        {
            var (x, y, z) = FromIndex(index);
            foreach (var obj in objectIndex[index])
                visit(x, y, z, obj);
        }
    }

    public void VisitBlockCells(
        Action<int, int, int, VoxelCell> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        _cells.VisitOccupied(
            (voxelIndex, cell) =>
            {
                var (x, y, z) =
                    FromIndex(voxelIndex);
                visit(x, y, z, cell);
            });
    }

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

        var index = ToIndex(x, y, z);
        if (!_fluids.Set(index, fluid, out var previous))
            return false;

        // Fluid-supported Objects are removed when their supporting surface
        // disappears, changes identity, or drops below the voxel's top face.
        // No solid-block attachments are affected by fluid changes. If the
        // chunk has no Objects, avoid a dictionary lookup on generated fluids.
        if (_cells.Get(index).IsEmpty &&
            (fluid.IsEmpty || fluid.Level != FluidCell.MaxLevel ||
             !fluid.IsSource ||
             previous.Fluid != fluid.Fluid) &&
            ObjectCount > 0 &&
            _objects is { } objectIndex &&
            objectIndex.Remove(index, out var detached))
        {
            ObjectCount -= detached.Count;
            ObjectRevision++;
            if (objectIndex.Count == 0)
                _objects = null;
        }

        Revision++;
        return true;
    }

    public int VisitFluidCellsInMeshlet(
        int meshletIndex,
        Action<int, int, int, FluidCell> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        var bounds = ChunkMeshletMask.Bounds(meshletIndex);
        return _fluids.VisitOccupiedInBox(
            bounds.MinX, bounds.MinY, bounds.MinZ,
            ChunkMeshletMask.Edge,
            ChunkMeshletMask.Edge,
            ChunkMeshletMask.Edge,
            (voxelIndex, fluid) =>
            {
                var (x, y, z) = FromIndex(voxelIndex);
                visit(x, y, z, fluid);
            });
    }

    public void VisitFluidCells(
        Action<int, int, int, FluidCell> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        VisitFluidCells(
            0,
            int.MaxValue,
            visit,
            out _);
    }

    /// <summary>
    /// Visits a bounded range of occupied fluid cells beginning at the cursor.
    /// The returned cursor can be used to resume the scan without revisiting
    /// earlier voxel indices. A cursor equal to <see cref="Volume"/> is complete.
    /// </summary>
    public int VisitFluidCells(
        int startVoxelIndex,
        int maximumCells,
        Action<int, int, int, FluidCell> visit,
        out int visitedCount)
    {
        ArgumentNullException.ThrowIfNull(visit);

        return _fluids.VisitOccupied(
            startVoxelIndex,
            maximumCells,
            (voxelIndex, fluid) =>
            {
                var (x, y, z) = FromIndex(voxelIndex);
                visit(x, y, z, fluid);
            },
            out visitedCount);
    }




    // Fills one horizontal Y layer of the light field. The voxel storage
    // order is X/Z within each Y layer, so this avoids thousands of
    // coordinate validations and method calls for horizontally uniform light.

public Chunk CloneForWorker() =>
        new(
            _cells.Clone(),
            _fluids.Clone(),
            _microblockMasks.Clone(),
            _surfaceStates is { Count: > 0 } surfaceStates
                ? new Dictionary<int, BlockSurfaceState>(surfaceStates)
                : null,
            _objects is { Count: > 0 } objectIndex
                ? objectIndex.ToDictionary(entry => entry.Key,
                    entry => new List<ObjectCell>(entry.Value))
                : null,
            Revision,
            ObjectRevision)
        { ObjectCount = this.ObjectCount };

    public int VisitLayeredCellsInMeshlet(
        int meshletIndex,
        Action<int, int, int, BlockSurfaceState> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        var surfaceStates = _surfaceStates;
        if (surfaceStates is not { Count: > 0 })
            return 0;

        var bounds = ChunkMeshletMask.Bounds(meshletIndex);
        List<int>? selected = null;
        foreach (var (voxelIndex, state) in surfaceStates)
        {
            if (state.Layers.Count == 0)
                continue;

            var (x, y, z) = FromIndex(voxelIndex);
            if (x < bounds.MinX || x >= bounds.MaxXExclusive ||
                y < bounds.MinY || y >= bounds.MaxYExclusive ||
                z < bounds.MinZ || z >= bounds.MaxZExclusive)
                continue;

            selected ??= [];
            selected.Add(voxelIndex);
        }

        if (selected is null)
            return 0;

        // Match the chunk's normal Y/Z/X traversal order independent of
        // insertion/removal history in the sparse state dictionary.
        selected.Sort();
        foreach (var voxelIndex in selected)
        {
            var (x, y, z) = FromIndex(voxelIndex);
            visit(x, y, z, surfaceStates[voxelIndex]);
        }

        return selected.Count;
    }

    public BlockSurfaceState GetSurfaceState(int x, int y, int z)
    {
        ValidateCoordinates(x, y, z);
        return _surfaceStates is { } surfaceStates &&
               surfaceStates.TryGetValue(ToIndex(x, y, z), out var state)
            ? state
            : BlockSurfaceState.Empty;
    }

    public bool SetSurfaceState(int x, int y, int z, BlockSurfaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateCoordinates(x, y, z);
        if (GetCell(x, y, z).IsEmpty)
            return false;

        var index = ToIndex(x, y, z);
        var existing = GetSurfaceState(x, y, z);
        if (existing.Equals(state))
            return false;

        if (state.IsEmpty)
        {
            _surfaceStates?.Remove(index);
            if (_surfaceStates is { Count: 0 })
                _surfaceStates = null;
        }
        else
        {
            (_surfaceStates ??= [])[index] = state;
        }

        Revision++;
        return true;
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

    private bool DependencyBoundaryHas(
        int xOffset,
        int yOffset,
        int zOffset,
        bool fluidOnly)
    {
        ValidateDependencyOffset(
            xOffset,
            yOffset,
            zOffset);

        var (minX, maxX) =
            DependencyAxisRange(
                xOffset);
        var (minY, maxY) =
            DependencyAxisRange(
                yOffset);
        var (minZ, maxZ) =
            DependencyAxisRange(
                zOffset);

        for (var y = minY;
             y <= maxY;
             y++)
        {
            for (var z = minZ;
                 z <= maxZ;
                 z++)
            {
                for (var x = minX;
                     x <= maxX;
                     x++)
                {
                    var index =
                        ToIndex(
                            x,
                            y,
                            z);
                    var fluid =
                        _fluids.Get(
                            index);

                    if (!fluid.IsEmpty)
                    {
                        return true;
                    }

                    if (!fluidOnly &&
                        !_cells.Get(
                                index)
                            .IsEmpty)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static (int Min, int Max)
        DependencyAxisRange(
            int offset) =>
        offset switch
        {
            < 0 => (0, 0),
            > 0 => (Size - 1, Size - 1),
            _ => (0, Size - 1),
        };

    private static void ValidateDependencyOffset(
        int x,
        int y,
        int z)
    {
        if (x is < -1 or > 1 ||
            y is < -1 or > 1 ||
            z is < -1 or > 1 ||
            (x == 0 &&
             y == 0 &&
             z == 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Dependency offset must be a non-zero 3D neighbor offset.");
        }
    }

    private static int ToIndex(
        int x,
        int y,
        int z) =>
        x + Size * (z + Size * y);

    private static (int X, int Y, int Z) FromIndex(
        int index)
    {
        var y = index / Area;
        var remainder = index % Area;
        var z = remainder / Size;
        var x = remainder % Size;
        return (x, y, z);
    }

    private static void ValidateCoordinates(int x, int y, int z)
    {
        if (!Contains(x, y, z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Voxel ({x}, {y}, {z}) is outside a {Size}³ chunk.");
        }
    }
}
