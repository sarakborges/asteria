namespace Asteria.Core.World;

internal enum DirectionalFaceDirection : byte
{
    Up, NegativeX, PositiveX, NegativeZ, PositiveZ, Down,
}

internal readonly record struct DirectionalFaceVoxelData(
    bool IsTerrainVoxel, ulong Up, ulong NegativeX, ulong PositiveX,
    ulong NegativeZ, ulong PositiveZ, ulong Down, BlockRuntimeId BaseBlock)
{
    public ulong Candidates(DirectionalFaceDirection direction) => direction switch
    {
        DirectionalFaceDirection.Up => Up,
        DirectionalFaceDirection.NegativeX => NegativeX,
        DirectionalFaceDirection.PositiveX => PositiveX,
        DirectionalFaceDirection.NegativeZ => NegativeZ,
        DirectionalFaceDirection.PositiveZ => PositiveZ,
        DirectionalFaceDirection.Down => Down,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public DirectionalFaceVoxelData WithCandidates(
        DirectionalFaceDirection direction, ulong candidates) => direction switch
    {
        DirectionalFaceDirection.Up => this with { Up = candidates },
        DirectionalFaceDirection.NegativeX => this with { NegativeX = candidates },
        DirectionalFaceDirection.PositiveX => this with { PositiveX = candidates },
        DirectionalFaceDirection.NegativeZ => this with { NegativeZ = candidates },
        DirectionalFaceDirection.PositiveZ => this with { PositiveZ = candidates },
        DirectionalFaceDirection.Down => this with { Down = candidates },
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public bool HasCandidates => (Up | NegativeX | PositiveX | NegativeZ | PositiveZ | Down) != 0;
}

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
    private PaletteStorage<DirectionalFaceVoxelData>? _directionalFaceData;
    private bool _directionalFaceDataInitialized;
    private readonly VoxelLight[] _light;

    /// <summary>
    /// Efficiently fills a fresh chunk's block storage before normal worldgen
    /// passes begin. Initial writes cannot replace occupied cells; disposing
    /// the writer commits one revision for the completed batch.
    /// </summary>
    internal sealed class InitialBlockWriter : IDisposable
    {
        private Chunk? _chunk;
        private bool _changed;

        internal InitialBlockWriter(Chunk chunk)
        {
            _chunk = chunk;
        }

        public bool SetBlockAtLinearIndex(
            int voxelIndex,
            BlockRuntimeId block)
        {
            var chunk = _chunk ??
                throw new ObjectDisposedException(nameof(InitialBlockWriter));
            if ((uint)voxelIndex >= Volume)
                throw new ArgumentOutOfRangeException(nameof(voxelIndex));

            var changed = chunk._cells.SetInitial(
                voxelIndex,
                new VoxelCell(block));
            _changed |= changed;
            return changed;
        }

        public void Complete()
        {
            var chunk = _chunk;
            if (chunk is null)
                return;

            if (_changed)
                chunk.Revision++;

            _chunk = null;
        }

        public void Dispose() => Complete();
    }

    /// <summary>
    /// Batches generation-time fluid additions into one chunk revision. This
    /// writer may coexist with fluids already authored by structures, but it
    /// never replaces occupied fluid cells and must run before Objects are added.
    /// </summary>
    internal sealed class InitialFluidWriter : IDisposable
    {
        private Chunk? _chunk;
        private bool _changed;

        internal InitialFluidWriter(Chunk chunk)
        {
            _chunk = chunk;
        }

        public bool IsEmptyAtLinearIndex(int voxelIndex)
        {
            var chunk = _chunk ??
                throw new ObjectDisposedException(nameof(InitialFluidWriter));
            if ((uint)voxelIndex >= Volume)
                throw new ArgumentOutOfRangeException(nameof(voxelIndex));

            return chunk._fluids.Get(voxelIndex).IsEmpty;
        }

        public bool SetFluidAtLinearIndexIfEmpty(
            int voxelIndex,
            FluidCell fluid)
        {
            var chunk = _chunk ??
                throw new ObjectDisposedException(nameof(InitialFluidWriter));
            if ((uint)voxelIndex >= Volume)
                throw new ArgumentOutOfRangeException(nameof(voxelIndex));

            var changed = chunk._fluids.SetInitialIfEmpty(voxelIndex, fluid);
            _changed |= changed;
            return changed;
        }

        public void Complete()
        {
            var chunk = _chunk;
            if (chunk is null)
                return;

            if (_changed)
                chunk.Revision++;

            _chunk = null;
        }

        public void Dispose() => Complete();
    }

    public Chunk()
    {
        _cells = new PaletteStorage<VoxelCell>();
        _fluids = new PaletteStorage<FluidCell>();
        _microblockMasks = new MicroblockMaskPalette();
        _light = new VoxelLight[Volume];
    }

    private Chunk(
        PaletteStorage<VoxelCell> cells,
        PaletteStorage<FluidCell> fluids,
        MicroblockMaskPalette microblockMasks,
        Dictionary<int, BlockSurfaceState>? surfaceStates,
        Dictionary<int, List<ObjectCell>>? objects,
        PaletteStorage<DirectionalFaceVoxelData>? directionalFaceData,
        bool directionalFaceDataInitialized,
        VoxelLight[] light,
        ulong revision,
        ulong objectRevision)
    {
        _cells = cells;
        _fluids = fluids;
        _microblockMasks = microblockMasks;
        _surfaceStates = surfaceStates;
        _objects = objects;
        _directionalFaceData = directionalFaceData;
        _directionalFaceDataInitialized = directionalFaceDataInitialized;
        _light = light;
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

    /// <summary>
    /// Reads a voxel by its already-validated linear index. Internal callers
    /// must establish that the index belongs to this chunk before using it.
    /// </summary>
    internal VoxelCell GetCellAtLinearIndexUnchecked(int voxelIndex) =>
        _cells.Get(voxelIndex);

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

    internal DirectionalFaceVoxelData DirectionalFaceDataAtLinearIndex(int index)
    {
        if ((uint)index >= Volume) throw new ArgumentOutOfRangeException(nameof(index));
        return _directionalFaceData?.Get(index) ?? default;
    }

    internal bool SetInitialDirectionalFaceDataAtLinearIndex(
        int index, DirectionalFaceVoxelData data, bool updateRevision = true)
    {
        if ((uint)index >= Volume) throw new ArgumentOutOfRangeException(nameof(index));
        if (data == default) return false;
        _directionalFaceData ??= new PaletteStorage<DirectionalFaceVoxelData>();
        if (!_directionalFaceData.SetInitial(index, data)) return false;
        if (updateRevision) Revision++;
        return true;
    }

    internal void SetDirectionalFaceDataAtLinearIndex(
        int index, DirectionalFaceVoxelData data, bool updateRevision = true)
    {
        if ((uint)index >= Volume) throw new ArgumentOutOfRangeException(nameof(index));
        if (data == default && _directionalFaceData is null) return;
        _directionalFaceData ??= new PaletteStorage<DirectionalFaceVoxelData>();
        if (!_directionalFaceData.Set(index, data)) return;
        if (_directionalFaceData.OccupiedCount == 0) _directionalFaceData = null;
        if (updateRevision) Revision++;
    }

    internal bool UpdateDirectionalFaceCandidateAtLinearIndex(
        int index,
        DirectionalFaceDirection direction,
        int distance,
        bool exposed,
        bool updateRevision = true)
    {
        if (distance is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(distance));
        var data = DirectionalFaceDataAtLinearIndex(index);
        if (!data.IsTerrainVoxel)
        {
            var source = _cells.Get(index);
            if (source.IsEmpty || source.IsPlayerPlaced || !exposed)
                return false;
            data = new DirectionalFaceVoxelData(
                true, 0, 0, 0, 0, 0, 0, BlockRuntimeId.Air);
        }
        var bit = 1UL << (distance - 1);
        var oldMask = data.Candidates(direction);
        var nextMask = exposed ? oldMask | bit : oldMask & ~bit;
        if (oldMask == nextMask && data.IsTerrainVoxel &&
            DirectionalFaceDataAtLinearIndex(index).IsTerrainVoxel)
            return false;
        SetDirectionalFaceDataAtLinearIndex(
            index, data.WithCandidates(direction, nextMask), updateRevision);
        return true;
    }

    internal bool RestoreDirectionalFaceBaseBlockAtLinearIndex(int index)
    {
        var data = DirectionalFaceDataAtLinearIndex(index);
        if (data.BaseBlock.IsAir) return false;
        SetDirectionalFaceDataAtLinearIndex(
            index, data with { BaseBlock = BlockRuntimeId.Air }, updateRevision: false);
        SetCellAtIndex(index, new VoxelCell(data.BaseBlock), preserveDirectionalFaceData: true);
        return true;
    }

    internal bool SetDirectionalFacePaletteBlockAtLinearIndex(
        int index, BlockRuntimeId replacement)
    {
        if ((uint)index >= Volume) throw new ArgumentOutOfRangeException(nameof(index));
        var data = DirectionalFaceDataAtLinearIndex(index);
        if (!data.IsTerrainVoxel) return false;
        var current = _cells.Get(index);
        if (current.IsEmpty) return false;
        var changedMetadata = false;
        if (data.BaseBlock.IsAir)
        {
            data = data with { BaseBlock = current.Block };
            SetDirectionalFaceDataAtLinearIndex(index, data, updateRevision: false);
            changedMetadata = true;
        }
        var changedBlock = SetCellAtIndex(
            index, new VoxelCell(replacement), preserveDirectionalFaceData: true);
        if (changedMetadata && !changedBlock) Revision++;
        return changedBlock || changedMetadata;
    }

    internal void VisitDirectionalFaceVoxels(Action<int, DirectionalFaceVoxelData> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        _directionalFaceData?.VisitOccupied((index, data) => visit(index, data));
    }

    internal bool HasDirectionalFaceData =>
        _directionalFaceData is { OccupiedCount: > 0 };

    internal bool DirectionalFaceDataInitialized =>
        _directionalFaceDataInitialized;

    internal void MarkDirectionalFaceDataInitialized(bool updateRevision = true)
    {
        if (_directionalFaceDataInitialized)
            return;
        _directionalFaceDataInitialized = true;
        if (updateRevision)
            Revision++;
    }

    internal void SetDirectionalFaceDataInitialized(bool initialized) =>
        _directionalFaceDataInitialized = initialized;

    internal void MarkDirectionalFaceDataChanged()
    {
        _directionalFaceDataInitialized = true;
        Revision++;
    }

    internal InitialBlockWriter BeginInitialBlockWrites()
    {
        if (Revision != 0 ||
            ObjectRevision != 0 ||
            NonEmptyVoxelCount != 0 ||
            FluidCount != 0 ||
            MicroblockMaskCount != 0 ||
            ObjectCount != 0 ||
            _surfaceStates is not null ||
            _objects is not null)
        {
            throw new InvalidOperationException(
                "Initial block writes require a fresh, empty chunk.");
        }

        return new InitialBlockWriter(this);
    }

    /// <summary>
    /// Begins batched generated-fluid writes. Existing block and fluid content
    /// is preserved, but callers must add no Objects until the batch completes.
    /// </summary>
    internal InitialFluidWriter BeginInitialFluidWrites()
    {
        if (ObjectCount != 0 || _objects is not null)
        {
            throw new InvalidOperationException(
                "Initial fluid writes must complete before Objects are added.");
        }

        return new InitialFluidWriter(this);
    }

    private bool SetCellAtIndex(
        int index, VoxelCell cell, bool preserveDirectionalFaceData = false)
    {
        if (!_cells.Set(index, cell, out var previous))
            return false;
        if (!preserveDirectionalFaceData &&
            (previous.Block != cell.Block ||
             previous.IsPlayerPlaced != cell.IsPlayerPlaced) &&
            _directionalFaceData is { } directionalData)
        {
            directionalData.Set(index, default);
            if (directionalData.OccupiedCount == 0)
                _directionalFaceData = null;
        }

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

    /// <summary>
    /// Reads a fluid by its already-validated linear index. Internal callers
    /// must establish that the index belongs to this chunk before using it.
    /// </summary>
    internal FluidCell GetFluidAtLinearIndexUnchecked(int voxelIndex) =>
        _fluids.Get(voxelIndex);

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

    // Fills one horizontal Y layer of the light field. The voxel storage
    // order is X/Z within each Y layer, so this avoids thousands of
    // coordinate validations and method calls for horizontally uniform light.
    internal void FillLightLayer(int y, VoxelLight value)
    {
        if ((uint)y >= Size)
            throw new ArgumentOutOfRangeException(nameof(y));

        Array.Fill(_light, value, y * Area, Area);
    }

    // Initial light of a truly empty chunk is uniform open sky. Filling
    // the packed field once avoids four thousand medium/registry lookups.
    public void FillLight(VoxelLight value)
    {
        Array.Fill(_light, value);
    }

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
            _directionalFaceData?.Clone(),
            _directionalFaceDataInitialized,
            (VoxelLight[])_light.Clone(),
            Revision,
            ObjectRevision)
        { ObjectCount = this.ObjectCount };

    /// <summary>
    /// Creates an isolated lighting snapshot. Surface appearance state is
    /// intentionally omitted because the light solver only reads block/fluid
    /// cells, microblock geometry, attached Objects and the current light field.
    /// </summary>
    internal Chunk CloneForLightingWorker() =>
        new(
            _cells.Clone(),
            _fluids.Clone(),
            _microblockMasks.Clone(),
            [],
            _objects is { Count: > 0 } objectIndex
                ? objectIndex.ToDictionary(entry => entry.Key,
                    entry => new List<ObjectCell>(entry.Value))
                : null,
            null,
            false,
            (VoxelLight[])_light.Clone(),
            Revision,
            ObjectRevision)
        { ObjectCount = this.ObjectCount };

    public bool CopyLightFrom(Chunk source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var changed = false;
        for (var index = 0; index < Volume; index++)
        {
            var light = source._light[index];
            changed |= _light[index] != light;
            _light[index] = light;
        }

        return changed;
    }

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
