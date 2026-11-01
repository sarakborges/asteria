namespace Asteria.Core.World;

public readonly record struct WorldVoxelCoord(int X, int Y, int Z)
{
    public static WorldVoxelCoord operator +(
        WorldVoxelCoord value,
        (int X, int Y, int Z) offset) =>
        new(value.X + offset.X, value.Y + offset.Y, value.Z + offset.Z);
}

public readonly record struct VoxelWorldEdit(
    WorldVoxelCoord Position,
    ChunkCoord Chunk,
    LocalVoxelCoord Local,
    VoxelCell Previous,
    VoxelCell Current,
    ulong WorldRevision);

public readonly record struct FluidWorldEdit(
    WorldVoxelCoord Position,
    ChunkCoord Chunk,
    LocalVoxelCoord Local,
    FluidCell Previous,
    FluidCell Current,
    ulong WorldRevision);

public readonly record struct ObjectWorldEdit(
    WorldVoxelCoord Support,
    ObjectCell Object,
    bool Placed,
    ulong WorldRevision);

public readonly record struct VoxelLightChunkStamp(
    ChunkCoord Coord,
    ulong LightingRevision,
    ulong ResidencyEpoch);

public sealed class VoxelWorld
{
    private readonly Dictionary<ChunkCoord, Chunk> _chunks = [];
    // Derived resident-only index. Changed by the same owner that mutates
    // chunk object occupancy; targeting never scans every loaded chunk.
    private readonly HashSet<ChunkCoord> _residentObjectChunks = [];
    private BlockRegistry? _fluidOccupancyBlocks;
    private readonly Dictionary<ChunkCoord, ulong>
        _residencyEpochs = [];
    private readonly Dictionary<ChunkColumnCoord, ulong>
        _columnResidencyRevisions = [];
    // Derived residency index; updated atomically with _chunks on every
    // insert, restore and archive. Avoid scanning all chunks per transition.
    private readonly Dictionary<ChunkColumnCoord, int>
        _residentColumnChunkCounts = [];
    private readonly SessionChunkArchiveStore _archive = new();
    private ulong _nextResidencyEpoch;
    private ulong _nextColumnResidencyRevision;
    private readonly Dictionary<ChunkCoord, ulong> _lightingRevisions = [];

    public ulong Revision { get; private set; }

    public int ChunkCount => _chunks.Count;

    public int ArchivedChunkCount => _archive.ArchivedCount;

    public int DirtyChunkCount => _archive.DirtyCount;

    public IEnumerable<ChunkCoord> LoadedChunkCoords => _chunks.Keys;

    public IEnumerable<ChunkCoord> ResidentObjectChunkCoords => _residentObjectChunks;

    /// <summary>Configures which decorative sprites may share a fluid voxel.</summary>
    public void ConfigureFluidOccupancy(BlockRegistry blocks) =>
        _fluidOccupancyBlocks = blocks ??
            throw new ArgumentNullException(nameof(blocks));

    public bool CanContainFluidAt(WorldVoxelCoord position) =>
        CanContainFluid(GetCellOrEmpty(position));

    internal bool CanContainFluid(VoxelCell cell) =>
        cell.IsEmpty ||
        (_fluidOccupancyBlocks is { } blocks &&
         FluidBlockOccupancy.AllowsFluid(blocks.GetDefinition(cell.Block)));

    /// <summary>
    /// Raised after a chunk becomes resident, whether materialized or restored.
    /// Consumers may reconcile bounded dependencies across newly loaded seams.
    /// </summary>
    public event Action<ChunkCoord>? ChunkBecameResident;

    /// <summary>Raised after a chunk leaves live residency and becomes archived.</summary>
    public event Action<ChunkCoord>? ChunkBecameArchived;

    /// <summary>Invalidates only Object presentation for a resident chunk.</summary>
    public event Action<ChunkCoord>? ObjectChunkChanged;

    // Both resident and archived chunks are authoritative. A newly created
    // world is populated from saved chunks before any worker/session sees it.
    internal IEnumerable<(ChunkCoord Coord, Chunk Chunk, bool Resident, bool Dirty)>
        CaptureChunkSources()
    {
        foreach (var (coord, chunk) in _chunks)
            yield return (coord, chunk, true, _archive.IsDirty(coord, chunk.Revision));

        foreach (var (coord, chunk) in _archive.ArchivedChunks)
            yield return (coord, chunk, false, _archive.IsDirty(coord, chunk.Revision));
    }

    internal void RestoreSavedChunk(ChunkCoord coord, Chunk chunk, bool resident, bool dirty)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (coord.Y < 0 || ContainsChunk(coord) || HasArchivedChunk(coord))
            throw new InvalidOperationException(
                $"Saved chunk {coord} cannot overwrite an existing chunk.");

        if (resident)
        {
            InsertChunk(coord, chunk);
            if (dirty)
                _archive.MarkDirty(coord);
        }
        else
        {
            _archive.RestoreArchived(coord, chunk, dirty);
            Revision++;
        }
    }

    public void InsertChunk(ChunkCoord coord, Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (_archive.HasArchived(coord))
        {
            throw new InvalidOperationException(
                $"Chunk {coord} has archived session state and cannot be overwritten by materialization.");
        }

        if (!_chunks.TryAdd(coord, chunk))
        {
            throw new InvalidOperationException($"Chunk {coord} is already resident.");
        }

        _archive.TrackMaterialized(
            coord,
            chunk.Revision);
        if (chunk.ObjectCount != 0) _residentObjectChunks.Add(coord);
        AssignResidencyEpoch(coord);
        MarkColumnChunkAdded(coord);
        Revision++;
        ChunkBecameResident?.Invoke(coord);
    }

    public bool ContainsChunk(ChunkCoord coord) => _chunks.ContainsKey(coord);

    /// <summary>
    /// Captures light/residency revisions for the resident chunk neighborhood
    /// that can contribute samples to a one-voxel lighting halo at this position.
    /// The destination must contain at least eight entries.
    /// </summary>
    public int CaptureLightingNeighborhood(
        WorldVoxelCoord position,
        Span<VoxelLightChunkStamp> destination)
    {
        if (destination.Length < 8)
            throw new ArgumentException(
                "Lighting neighborhood storage must hold eight chunk stamps.",
                nameof(destination));

        var address = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z);
        Span<int> xOffsets = stackalloc int[2];
        Span<int> yOffsets = stackalloc int[2];
        Span<int> zOffsets = stackalloc int[2];
        var xCount = FillChunkNeighborOffsets(address.Local.X, xOffsets);
        var yCount = FillChunkNeighborOffsets(address.Local.Y, yOffsets);
        var zCount = FillChunkNeighborOffsets(address.Local.Z, zOffsets);
        var count = 0;

        for (var y = 0; y < yCount; y++)
        for (var z = 0; z < zCount; z++)
        for (var x = 0; x < xCount; x++)
        {
            var coord = new ChunkCoord(
                address.Chunk.X + xOffsets[x],
                address.Chunk.Y + yOffsets[y],
                address.Chunk.Z + zOffsets[z]);
            destination[count++] = new VoxelLightChunkStamp(
                coord,
                _lightingRevisions.GetValueOrDefault(coord),
                _residencyEpochs.GetValueOrDefault(coord));
        }

        return count;
    }

    private static int FillChunkNeighborOffsets(
        int localCoordinate,
        Span<int> offsets)
    {
        if (localCoordinate == 0)
        {
            offsets[0] = -1;
            offsets[1] = 0;
            return 2;
        }

        if (localCoordinate == Chunk.Size - 1)
        {
            offsets[0] = 0;
            offsets[1] = 1;
            return 2;
        }

        offsets[0] = 0;
        return 1;
    }

    private void MarkLightingChanged(ChunkCoord coord)
    {
        var revision = _lightingRevisions.GetValueOrDefault(coord);
        _lightingRevisions[coord] =
            revision == ulong.MaxValue ? 1 : revision + 1;
    }

    public bool HasArchivedChunk(ChunkCoord coord) =>
        _archive.HasArchived(coord);

    public ChunkArchiveResult ArchiveChunk(ChunkCoord coord)
    {
        if (!_chunks.Remove(coord, out var chunk))
        {
            return ChunkArchiveResult.NotResident;
        }

        _residencyEpochs.Remove(coord);
        _lightingRevisions.Remove(coord);
        _residentObjectChunks.Remove(coord);
        MarkColumnChunkRemoved(coord);
        var result = _archive.Archive(coord, chunk);
        Revision++;
        ChunkBecameArchived?.Invoke(coord);
        return result;
    }

    public IReadOnlyList<(
        ChunkCoord Coord,
        ChunkArchiveResult Result)>
        ArchiveAllResidentChunks()
    {
        var coordinates =
            _chunks.Keys
                .OrderBy(
                    coord =>
                        coord.Y)
                .ThenBy(
                    coord =>
                        coord.Z)
                .ThenBy(
                    coord =>
                        coord.X)
                .ToArray();
        var results =
            new (
                ChunkCoord Coord,
                ChunkArchiveResult Result)[
                coordinates.Length];

        for (var index = 0;
             index < coordinates.Length;
             index++)
        {
            var coord =
                coordinates[index];
            results[index] =
                (
                    coord,
                    ArchiveChunk(
                        coord));
        }

        return results;
    }

    public ChunkRestoreResult RestoreChunk(ChunkCoord coord)
    {
        if (_chunks.ContainsKey(coord))
        {
            return ChunkRestoreResult.AlreadyResident;
        }

        if (!_archive.TryRestore(coord, out var chunk))
        {
            return ChunkRestoreResult.Missing;
        }

        _chunks.Add(coord, chunk);
        if (chunk.ObjectCount != 0) _residentObjectChunks.Add(coord);
        AssignResidencyEpoch(coord);
        MarkColumnChunkAdded(coord);
        Revision++;
        ChunkBecameResident?.Invoke(coord);
        return ChunkRestoreResult.Restored;
    }

    /// <summary>
    /// Reads an already materialized voxel for restore validation without
    /// moving archived chunks into live residency or generating new terrain.
    /// </summary>
    internal bool TryGetSavedCell(WorldVoxelCoord position, out VoxelCell cell)
    {
        if (position.Y < 0)
        {
            cell = VoxelCell.Empty;
            return false;
        }
        var address = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z);
        if (!_chunks.TryGetValue(address.Chunk, out var chunk) &&
            !_archive.TryPeek(address.Chunk, out chunk!))
        {
            cell = VoxelCell.Empty;
            return false;
        }

        cell = chunk.GetCell(
            address.Local.X, address.Local.Y, address.Local.Z);
        return true;
    }

    public bool TryGetChunk(ChunkCoord coord, out Chunk chunk) =>
        _chunks.TryGetValue(coord, out chunk!);

    public Chunk GetChunk(ChunkCoord coord) =>
        _chunks.TryGetValue(coord, out var chunk)
            ? chunk
            : throw new KeyNotFoundException($"Chunk is not resident: {coord}");

    public bool TryGetContentRevision(
        ChunkCoord coord,
        out ChunkContentRevision revision)
    {
        if (!_chunks.TryGetValue(
                coord,
                out var chunk) ||
            !_residencyEpochs.TryGetValue(
                coord,
                out var epoch))
        {
            revision = default;
            return false;
        }

        revision =
            new ChunkContentRevision(
                epoch,
                chunk.Revision);
        return true;
    }

    /// <summary>
    /// Meshing alone observes the entire materialized spatial map. Archived
    /// chunks are immutable while archived and are not brought into active
    /// residency by this query.
    /// </summary>
    internal bool TryGetMaterializedRevision(ChunkCoord coord, out ulong revision)
    {
        if (_chunks.TryGetValue(coord, out var resident) ||
            _archive.TryPeek(coord, out resident!))
        {
            revision = resident.Revision;
            return true;
        }

        revision = 0;
        return false;
    }

    private ChunkContentStamp CaptureMeshingContentStamp(
        IEnumerable<ChunkCoord> coordinates)
    {
        var revisions = new Dictionary<ChunkCoord, ChunkContentRevision>();
        foreach (var coord in coordinates.Distinct()
                     .OrderBy(coord => coord.Y)
                     .ThenBy(coord => coord.Z)
                     .ThenBy(coord => coord.X))
        {
            if (TryGetMaterializedRevision(coord, out var revision))
                revisions.Add(coord, new ChunkContentRevision(
                    GetResidencyEpoch(coord), revision));
        }

        return new ChunkContentStamp(revisions);
    }

    public ChunkContentRevision GetContentRevision(
        ChunkCoord coord) =>
        TryGetContentRevision(
            coord,
            out var revision)
            ? revision
            : throw new KeyNotFoundException(
                $"Chunk content revision is unavailable: {coord}");

    public ChunkContentStamp CaptureContentStamp(
        IEnumerable<ChunkCoord> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        var revisions =
            new Dictionary<ChunkCoord, ChunkContentRevision>();

        foreach (var coord in coordinates
                     .Distinct()
                     .OrderBy(coord => coord.Y)
                     .ThenBy(coord => coord.Z)
                     .ThenBy(coord => coord.X))
        {
            if (TryGetContentRevision(
                    coord,
                    out var revision))
            {
                revisions.Add(
                    coord,
                    revision);
            }
        }

        return new ChunkContentStamp(revisions);
    }

    public bool IsContentStampCurrent(
        ChunkContentStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);

        foreach (var (coord, expected) in stamp.Entries)
        {
            if (!TryGetContentRevision(coord, out var current) || current != expected)
            {
                return false;
            }
        }

        return true;
    }

    public ulong GetResidencyEpoch(
        ChunkCoord coord) =>
        _residencyEpochs.TryGetValue(
            coord,
            out var epoch)
            ? epoch
            : 0;

    public ChunkResidencyStamp CaptureResidencyStamp(
        IEnumerable<ChunkCoord> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        var epochs =
            new Dictionary<ChunkCoord, ulong>();

        foreach (var coord in coordinates
                     .Distinct()
                     .OrderBy(coord => coord.Y)
                     .ThenBy(coord => coord.Z)
                     .ThenBy(coord => coord.X))
        {
            epochs.Add(
                coord,
                GetResidencyEpoch(coord));
        }

        return new ChunkResidencyStamp(epochs);
    }

    public bool IsResidencyStampCurrent(
        ChunkResidencyStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);

        foreach (var (coord, expected) in
                 stamp.Entries)
        {
            if (GetResidencyEpoch(coord) != expected)
            {
                return false;
            }
        }

        return true;
    }

    public ulong GetColumnResidencyRevision(ChunkColumnCoord column) =>
        _columnResidencyRevisions.TryGetValue(column, out var revision)
            ? revision
            : 0;

    public ChunkColumnResidencyStamp CaptureColumnResidencyStamp(
        IEnumerable<ChunkColumnCoord> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var revisions = new Dictionary<ChunkColumnCoord, ulong>();

        foreach (var column in columns
                     .Distinct()
                     .OrderBy(column => column.Z)
                     .ThenBy(column => column.X))
        {
            revisions.Add(column, GetColumnResidencyRevision(column));
        }

        return new ChunkColumnResidencyStamp(revisions);
    }

    public bool IsColumnResidencyStampCurrent(ChunkColumnResidencyStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);

        foreach (var (column, expected) in stamp.Entries)
        {
            if (GetColumnResidencyRevision(column) != expected)
            {
                return false;
            }
        }

        return true;
    }

    public bool IsLoadedAt(WorldVoxelCoord position) =>
        ContainsChunk(VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z).Chunk);

    public bool TryGetCell(WorldVoxelCoord position, out VoxelCell cell)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        if (!_chunks.TryGetValue(address.Chunk, out var chunk))
        {
            cell = VoxelCell.Empty;
            return false;
        }

        cell = chunk.GetCell(
            address.Local.X,
            address.Local.Y,
            address.Local.Z);
        return true;
    }

    public bool TryGetCellAndFluid(
        WorldVoxelCoord position,
        out VoxelCell cell,
        out FluidCell fluid)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        if (!_chunks.TryGetValue(address.Chunk, out var chunk))
        {
            cell = VoxelCell.Empty;
            fluid = FluidCell.Empty;
            return false;
        }

        cell = chunk.GetCell(
            address.Local.X,
            address.Local.Y,
            address.Local.Z);
        fluid = chunk.GetFluid(
            address.Local.X,
            address.Local.Y,
            address.Local.Z);
        return true;
    }

    public VoxelCell GetCellOrEmpty(WorldVoxelCoord position) =>
        TryGetCell(position, out var cell)
            ? cell
            : VoxelCell.Empty;

    public bool TryGetObjects(WorldVoxelCoord support, out IReadOnlyList<ObjectCell> objects)
    {
        objects = Array.Empty<ObjectCell>();
        if (support.Y < 0) return false;
        var address = VoxelCoordinates.FromWorld(support.X, support.Y, support.Z);
        if (!_chunks.TryGetValue(address.Chunk, out var chunk)) return false;
        objects = chunk.ObjectsAt(address.Local.X, address.Local.Y, address.Local.Z);
        return true;
    }

    /// <summary>Objects attach to a real supporting block, never replace
    /// that block or the independent fluid in its voxel.</summary>
    public bool AddObjectAt(WorldVoxelCoord support, ObjectCell obj)
    {
        if (support.Y < 0) return false;
        var address = VoxelCoordinates.FromWorld(support.X, support.Y, support.Z);
        if (!_chunks.TryGetValue(address.Chunk, out var chunk) ||
            !chunk.AddObject(address.Local.X, address.Local.Y, address.Local.Z, obj))
            return false;
        _archive.MarkDirty(address.Chunk);
        Revision++;
        _residentObjectChunks.Add(address.Chunk);
        ObjectChunkChanged?.Invoke(address.Chunk);
        return true;
    }

    public bool RemoveObjectAt(WorldVoxelCoord support, ObjectCell obj)
    {
        if (support.Y < 0) return false;
        var address = VoxelCoordinates.FromWorld(support.X, support.Y, support.Z);
        if (!_chunks.TryGetValue(address.Chunk, out var chunk) ||
            !chunk.RemoveObject(address.Local.X, address.Local.Y, address.Local.Z, obj))
            return false;
        _archive.MarkDirty(address.Chunk);
        Revision++;
        if (chunk.ObjectCount == 0) _residentObjectChunks.Remove(address.Chunk);
        ObjectChunkChanged?.Invoke(address.Chunk);
        return true;
    }

    public bool TryGetFluid(
        WorldVoxelCoord position,
        out FluidCell fluid)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        if (!_chunks.TryGetValue(
                address.Chunk,
                out var chunk))
        {
            fluid = FluidCell.Empty;
            return false;
        }

        fluid = chunk.GetFluid(
            address.Local.X,
            address.Local.Y,
            address.Local.Z);
        return true;
    }

    public FluidCell GetFluidOrEmpty(
        WorldVoxelCoord position) =>
        TryGetFluid(position, out var fluid)
            ? fluid
            : FluidCell.Empty;

    public VoxelLight GetLightOrDark(WorldVoxelCoord position)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        return _chunks.TryGetValue(address.Chunk, out var chunk)
            ? chunk.GetLight(
                address.Local.X,
                address.Local.Y,
                address.Local.Z)
            : default;
    }

    public bool TrySetLight(WorldVoxelCoord position, VoxelLight light)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        if (!_chunks.TryGetValue(address.Chunk, out var chunk))
        {
            return false;
        }

        chunk.SetLight(
            address.Local.X,
            address.Local.Y,
            address.Local.Z,
            light);
        return true;
    }

    public BlockSurfaceState GetBlockSurfaceStateOrEmpty(WorldVoxelCoord position)
    {
        var address = VoxelCoordinates.FromWorld(position.X, position.Y, position.Z);
        return _chunks.TryGetValue(address.Chunk, out var chunk)
            ? chunk.GetSurfaceState(address.Local.X, address.Local.Y, address.Local.Z)
            : BlockSurfaceState.Empty;
    }

    public bool SetBlockSurfaceStateAt(
        WorldVoxelCoord position,
        BlockSurfaceState state,
        out VoxelWorldEdit edit)
    {
        ArgumentNullException.ThrowIfNull(state);
        var address = VoxelCoordinates.FromWorld(position.X, position.Y, position.Z);
        if (!_chunks.TryGetValue(address.Chunk, out var chunk))
        {
            edit = default;
            return false;
        }

        var cell = chunk.GetCell(address.Local.X, address.Local.Y, address.Local.Z);
        if (cell.IsEmpty ||
            !chunk.SetSurfaceState(address.Local.X, address.Local.Y, address.Local.Z, state))
        {
            edit = default;
            return false;
        }

        _archive.MarkDirty(address.Chunk);
        Revision++;
        edit = new VoxelWorldEdit(
            position, address.Chunk, address.Local, cell, cell, Revision);
        return true;
    }

    public MicroblockMask GetMicroblockMaskOrEmpty(WorldVoxelCoord position)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        if (!_chunks.TryGetValue(address.Chunk, out var chunk))
        {
            return MicroblockMask.Empty;
        }

        return chunk.GetMicroblockMask(
            address.Local.X,
            address.Local.Y,
            address.Local.Z);
    }

    public bool SetCellAt(
        WorldVoxelCoord position,
        VoxelCell cell,
        out VoxelWorldEdit edit)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        if (!_chunks.TryGetValue(address.Chunk, out var chunk))
        {
            edit = default;
            return false;
        }

        var previous = chunk.GetCell(
            address.Local.X,
            address.Local.Y,
            address.Local.Z);

        var objectRevision = chunk.ObjectRevision;
        if (previous == cell ||
            !chunk.SetCell(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                cell))
        {
            edit = default;
            return false;
        }

        if (!CanContainFluid(cell))
        {
            chunk.SetFluid(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                FluidCell.Empty);
        }

        _archive.MarkDirty(address.Chunk);
        Revision++;
        if (objectRevision != chunk.ObjectRevision)
        {
            if (chunk.ObjectCount == 0) _residentObjectChunks.Remove(address.Chunk);
            ObjectChunkChanged?.Invoke(address.Chunk);
        }
        edit = new VoxelWorldEdit(
            position,
            address.Chunk,
            address.Local,
            previous,
            cell,
            Revision);
        return true;
    }

    public bool SetBlockAt(
        WorldVoxelCoord position,
        BlockRuntimeId block,
        out VoxelWorldEdit edit) =>
        SetCellAt(position, new VoxelCell(block), out edit);

    public bool SetBlockStateAt(
        WorldVoxelCoord position,
        BlockStateSnapshot state,
        out VoxelWorldEdit edit)
    {
        ArgumentNullException.ThrowIfNull(state);

        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        if (!_chunks.TryGetValue(
                address.Chunk,
                out var chunk))
        {
            edit = default;
            return false;
        }

        var previous = chunk.GetCell(
            address.Local.X,
            address.Local.Y,
            address.Local.Z);

        var previousSnapshot =
            previous.IsEmpty
                ? (BlockStateSnapshot?)null
                : new BlockStateSnapshot(
                    previous,
                    previous.HasMicroblockGeometry
                        ? chunk.GetMicroblockMask(
                            address.Local.X,
                            address.Local.Y,
                            address.Local.Z)
                        : MicroblockMask.Empty,
                    chunk.GetSurfaceState(
                        address.Local.X,
                        address.Local.Y,
                        address.Local.Z));

        if (previousSnapshot is { } existing &&
            existing == state)
        {
            edit = default;
            return false;
        }

        var priorObjectRevision = chunk.ObjectRevision;
        chunk.SetCell(
            address.Local.X,
            address.Local.Y,
            address.Local.Z,
            state.Cell);

        if (state.HasMicroblockGeometry)
        {
            chunk.SetMicroblockMask(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                state.MicroblockMask);
        }

        chunk.SetSurfaceState(
            address.Local.X,
            address.Local.Y,
            address.Local.Z,
            state.SurfaceState);

        if (!CanContainFluid(state.Cell))
        {
            chunk.SetFluid(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                FluidCell.Empty);
        }

        _archive.MarkDirty(address.Chunk);
        Revision++;
        if (priorObjectRevision != chunk.ObjectRevision)
        {
            if (chunk.ObjectCount == 0) _residentObjectChunks.Remove(address.Chunk);
            ObjectChunkChanged?.Invoke(address.Chunk);
        }

        var current = chunk.GetCell(
            address.Local.X,
            address.Local.Y,
            address.Local.Z);

        edit = new VoxelWorldEdit(
            position,
            address.Chunk,
            address.Local,
            previous,
            current,
            Revision);
        return true;
    }

    public bool SetFluidAt(
        WorldVoxelCoord position,
        FluidCell fluid,
        out FluidWorldEdit edit)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        if (!_chunks.TryGetValue(
                address.Chunk,
                out var chunk))
        {
            edit = default;
            return false;
        }

        var previous = chunk.GetFluid(
            address.Local.X,
            address.Local.Y,
            address.Local.Z);

        if (!fluid.IsEmpty &&
            !CanContainFluid(chunk.GetCell(
                address.Local.X,
                address.Local.Y,
                address.Local.Z)))
        {
            edit = default;
            return false;
        }

        var previousObjectRevision = chunk.ObjectRevision;
        if (previous == fluid ||
            !chunk.SetFluid(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                fluid))
        {
            edit = default;
            return false;
        }

        _archive.MarkDirty(address.Chunk);
        Revision++;
        if (chunk.ObjectRevision != previousObjectRevision)
        {
            if (chunk.ObjectCount == 0) _residentObjectChunks.Remove(address.Chunk);
            ObjectChunkChanged?.Invoke(address.Chunk);
        }

        edit = new FluidWorldEdit(
            position,
            address.Chunk,
            address.Local,
            previous,
            fluid,
            Revision);

        return true;
    }

    public VoxelWorld CloneForWorker() =>
        CloneForWorker(_chunks.Keys);

    public VoxelWorld CloneForWorker(
        IEnumerable<ChunkCoord> coordinates) =>
        CloneForWorkerCore(coordinates, lightingOnly: false);

    internal VoxelWorld CloneForLightingWorker(
        IEnumerable<ChunkCoord> coordinates) =>
        CloneForWorkerCore(coordinates, lightingOnly: true);

    private VoxelWorld CloneForWorkerCore(
        IEnumerable<ChunkCoord> coordinates,
        bool lightingOnly)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        var clone = new VoxelWorld();
        clone._fluidOccupancyBlocks = _fluidOccupancyBlocks;

        foreach (var coord in coordinates.Distinct())
        {
            if (_chunks.TryGetValue(coord, out var chunk))
            {
                clone._chunks.Add(
                    coord,
                    lightingOnly
                        ? chunk.CloneForLightingWorker()
                        : chunk.CloneForWorker());
            }
        }

        foreach (var (coord, epoch) in
                 _residencyEpochs)
        {
            if (clone._chunks.ContainsKey(coord))
            {
                clone._residencyEpochs.Add(
                    coord,
                    epoch);
            }
        }

        foreach (var coord in clone._chunks.Keys)
        {
            var column = ChunkColumnCoord.FromChunk(coord);
            clone._residentColumnChunkCounts[column] =
                clone._residentColumnChunkCounts.TryGetValue(column, out var count)
                    ? count + 1
                    : 1;
        }

        foreach (var column in clone._residentColumnChunkCounts.Keys)
        {
            if (_columnResidencyRevisions.TryGetValue(column, out var revision))
            {
                clone._columnResidencyRevisions.Add(column, revision);
            }
        }

        clone._nextResidencyEpoch = _nextResidencyEpoch;
        clone._nextColumnResidencyRevision = _nextColumnResidencyRevision;
        clone.Revision = Revision;
        return clone;
    }

    /// <summary>
    /// Rendering sees already-materialized archived neighbors, unlike
    /// simulation snapshots that may only include actively resident chunks.
    /// Never mutate or reactivate archived chunks to capture their geometry.
    /// </summary>
    private VoxelWorld CloneForMeshing(IEnumerable<ChunkCoord> coordinates)
    {
        var selected = coordinates.Distinct().ToArray();
        var snapshot = CloneForWorker(selected);
        foreach (var coord in selected)
        {
            if (snapshot._chunks.ContainsKey(coord) ||
                !_archive.TryPeek(coord, out var archived))
                continue;

            snapshot._chunks.Add(coord, archived.CloneForWorker());
            var column = ChunkColumnCoord.FromChunk(coord);
            snapshot._residentColumnChunkCounts[column] =
                snapshot._residentColumnChunkCounts.TryGetValue(
                    column, out var count) ? count + 1 : 1;
        }

        return snapshot;
    }

    public VoxelWorld CloneFluidNeighborhood(
        IEnumerable<WorldVoxelCoord> seeds,
        int horizontalVoxelRadius)
    {
        var neighborhood =
            FluidSimulationSnapshot.DescribeNeighborhood(
                seeds,
                horizontalVoxelRadius);

        return neighborhood.Chunks.Count == 0
            ? new VoxelWorld()
            : CloneForWorker(
                neighborhood.Chunks);
    }

    public MeshWorkerSnapshot CaptureMeshWorkerSnapshot(
        IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask>
            dirtyMeshlets)
    {
        ArgumentNullException.ThrowIfNull(dirtyMeshlets);

        var required = new HashSet<ChunkCoord>();
        var footprints =
            new Dictionary<ChunkMeshletKey, HashSet<ChunkCoord>>();

        foreach (var (coord, mask) in dirtyMeshlets)
        {
            if (mask.IsEmpty)
                continue;

            required.Add(coord);
            var selected = mask.Indices().ToArray();
            foreach (var index in selected)
                footprints.Add(new ChunkMeshletKey(coord, index), [coord]);

            for (var y = -1; y <= 1; y++)
            for (var z = -1; z <= 1; z++)
            for (var x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0 && z == 0)
                    continue;

                var overlap = mask.Intersect(
                    ChunkMeshletMask.ForDependencyOffset(x, y, z));
                if (overlap.IsEmpty)
                    continue;

                var neighbor = new ChunkCoord(
                    coord.X + x, coord.Y + y, coord.Z + z);
                required.Add(neighbor);
                foreach (var index in selected)
                {
                    if (overlap.ContainsIndex(index))
                        footprints[new ChunkMeshletKey(coord, index)]
                            .Add(neighbor);
                }
            }
        }

        // The geometry snapshot includes archived neighbors because their
        // exact voxel contents remain part of the authoritative world.
        // Residency epochs are still captured separately to reject in-flight
        // worker results when active residency changes.
        var dependencies = new MeshDependencyStamp(
            CaptureMeshingContentStamp(required),
            CaptureResidencyStamp(required));
        var meshletDependencies =
            new Dictionary<ChunkMeshletKey, MeshDependencyStamp>(
                footprints.Count);
        foreach (var (key, envelope) in footprints)
        {
            meshletDependencies.Add(key, new MeshDependencyStamp(
                CaptureMeshingContentStamp(envelope),
                CaptureResidencyStamp(envelope)));
        }

        return new MeshWorkerSnapshot(
            CloneForMeshing(required),
            dependencies,
            meshletDependencies);
    }

    /// <summary>
    /// Publishes worker lighting only for chunks that reported a light change.
    /// Unmodified snapshot chunks must not cause full light-array copies on
    /// the main thread. Input positions are taken in their supplied order.
    /// </summary>
    public void CopyChangedLightFrom(
        VoxelWorld source,
        IReadOnlyList<WorldVoxelCoord> changedPositions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(changedPositions);

        var copied = new HashSet<ChunkCoord>();
        foreach (var position in changedPositions)
        {
            var coord = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z).Chunk;
            if (!copied.Add(coord) ||
                !_chunks.TryGetValue(coord, out var target) ||
                !source._chunks.TryGetValue(coord, out var sourceChunk))
            {
                continue;
            }

            if (target.CopyLightFrom(sourceChunk))
                MarkLightingChanged(coord);
        }
    }

    /// <summary>
    /// Copies a bounded slice of the lighting worker result. Unlike
    /// CopyChangedLightFrom, this updates individual voxels rather than
    /// cloning each chunk's light array, so large results can be published
    /// incrementally without repeating whole-chunk copies.
    /// </summary>
    public void CopyLightPositionsFrom(
        VoxelWorld source,
        IReadOnlyList<WorldVoxelCoord> positions,
        int offset,
        int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(positions);
        if (offset < 0 || count < 0 || offset > positions.Count - count)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var changedChunks = new HashSet<ChunkCoord>();
        var changedChunkOrder = new List<ChunkCoord>();
        for (var i = offset; i < offset + count; i++)
        {
            var position = positions[i];
            var address = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            if (!_chunks.TryGetValue(address.Chunk, out var target) ||
                !source._chunks.TryGetValue(address.Chunk, out var from))
                continue;

            var local = address.Local;
            var next = from.GetLight(local.X, local.Y, local.Z);
            if (target.GetLight(local.X, local.Y, local.Z) == next)
                continue;

            target.SetLight(local.X, local.Y, local.Z, next);
            if (changedChunks.Add(address.Chunk))
                changedChunkOrder.Add(address.Chunk);
        }

        foreach (var coord in changedChunkOrder)
            MarkLightingChanged(coord);
    }

    /// <summary>
    /// Commit one worker-computed light field. The lighting runtime validates
    /// chunk content and neighborhood residency before each publication.
    /// </summary>
    public void CopyLightChunkFrom(VoxelWorld source, ChunkCoord coord)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_chunks.TryGetValue(coord, out var target) &&
            source._chunks.TryGetValue(coord, out var from) &&
            target.CopyLightFrom(from))
            MarkLightingChanged(coord);
    }

    private void AssignResidencyEpoch(ChunkCoord coord)
    {
        _nextResidencyEpoch =
            _nextResidencyEpoch == ulong.MaxValue ? 1 : _nextResidencyEpoch + 1;
        _residencyEpochs[coord] = _nextResidencyEpoch;
    }

    private void MarkColumnChunkAdded(ChunkCoord coord)
    {
        var column = ChunkColumnCoord.FromChunk(coord);
        _residentColumnChunkCounts[column] =
            _residentColumnChunkCounts.TryGetValue(column, out var count)
                ? checked(count + 1)
                : 1;
        MarkColumnResidencyChanged(column);
    }

    private void MarkColumnChunkRemoved(ChunkCoord coord)
    {
        var column = ChunkColumnCoord.FromChunk(coord);
        var count = _residentColumnChunkCounts[column];
        if (count == 1)
        {
            _residentColumnChunkCounts.Remove(column);
            _columnResidencyRevisions.Remove(column);
            return;
        }

        _residentColumnChunkCounts[column] = count - 1;
        MarkColumnResidencyChanged(column);
    }

    private void MarkColumnResidencyChanged(ChunkColumnCoord column)
    {
        _nextColumnResidencyRevision =
            _nextColumnResidencyRevision == ulong.MaxValue
                ? 1
                : _nextColumnResidencyRevision + 1;
        _columnResidencyRevisions[column] = _nextColumnResidencyRevision;
    }
}
