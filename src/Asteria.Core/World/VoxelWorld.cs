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

public sealed class VoxelWorld
{
    private readonly Dictionary<ChunkCoord, Chunk> _chunks = [];
    private readonly Dictionary<ChunkCoord, ulong>
        _residencyEpochs = [];
    private readonly Dictionary<ChunkColumnCoord, ulong>
        _columnResidencyRevisions = [];
    private readonly SessionChunkArchiveStore _archive = new();
    private ulong _nextResidencyEpoch;
    private ulong _nextColumnResidencyRevision;

    public ulong Revision { get; private set; }

    public int ChunkCount => _chunks.Count;

    public int ArchivedChunkCount => _archive.ArchivedCount;

    public int DirtyChunkCount => _archive.DirtyCount;

    public IEnumerable<ChunkCoord> LoadedChunkCoords => _chunks.Keys;

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
        AssignResidencyEpoch(coord);
        MarkColumnResidencyChanged(coord);
        Revision++;
    }

    public bool ContainsChunk(ChunkCoord coord) => _chunks.ContainsKey(coord);

    public bool HasArchivedChunk(ChunkCoord coord) =>
        _archive.HasArchived(coord);

    public ChunkArchiveResult ArchiveChunk(ChunkCoord coord)
    {
        if (!_chunks.Remove(coord, out var chunk))
        {
            return ChunkArchiveResult.NotResident;
        }

        _residencyEpochs.Remove(coord);
        MarkColumnResidencyChanged(coord);
        var result = _archive.Archive(coord, chunk);
        Revision++;
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
        AssignResidencyEpoch(coord);
        MarkColumnResidencyChanged(coord);
        Revision++;
        return ChunkRestoreResult.Restored;
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

    public VoxelCell GetCellOrEmpty(WorldVoxelCoord position) =>
        TryGetCell(position, out var cell)
            ? cell
            : VoxelCell.Empty;

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

        if (!cell.IsEmpty)
        {
            chunk.SetFluid(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                FluidCell.Empty);
        }

        _archive.MarkDirty(address.Chunk);
        Revision++;
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

        chunk.SetFluid(
            address.Local.X,
            address.Local.Y,
            address.Local.Z,
            FluidCell.Empty);

        _archive.MarkDirty(address.Chunk);
        Revision++;

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
            !chunk.GetCell(
                address.Local.X,
                address.Local.Y,
                address.Local.Z).IsEmpty)
        {
            edit = default;
            return false;
        }

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
        IEnumerable<ChunkCoord> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        var clone = new VoxelWorld();

        foreach (var coord in coordinates.Distinct())
        {
            if (_chunks.TryGetValue(coord, out var chunk))
            {
                clone._chunks.Add(
                    coord,
                    chunk.CloneForWorker());
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

        foreach (var column in clone._chunks.Keys
                     .Select(ChunkColumnCoord.FromChunk)
                     .Distinct())
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

        var required =
            new HashSet<ChunkCoord>();

        foreach (var (coord, mask) in
                 dirtyMeshlets)
        {
            if (mask.IsEmpty)
            {
                continue;
            }

            required.Add(coord);

            for (var y = -1; y <= 1; y++)
            {
                for (var z = -1; z <= 1; z++)
                {
                    for (var x = -1; x <= 1; x++)
                    {
                        if (x == 0 &&
                            y == 0 &&
                            z == 0)
                        {
                            continue;
                        }

                        if (!mask.Overlaps(
                                ChunkMeshletMask
                                    .ForDependencyOffset(
                                        x,
                                        y,
                                        z)))
                        {
                            continue;
                        }

                        required.Add(
                            new ChunkCoord(
                                coord.X + x,
                                coord.Y + y,
                                coord.Z + z));
                    }
                }
            }
        }

        var dependencies =
            new MeshDependencyStamp(
                CaptureContentStamp(required),
                CaptureResidencyStamp(required));

        return new MeshWorkerSnapshot(
            CloneForWorker(required),
            dependencies);
    }

    public void CopyLightFrom(VoxelWorld source)
    {
        ArgumentNullException.ThrowIfNull(source);

        foreach (var (coord, chunk) in _chunks)
        {
            if (source._chunks.TryGetValue(coord, out var sourceChunk))
            {
                chunk.CopyLightFrom(sourceChunk);
            }
        }
    }

    private void AssignResidencyEpoch(ChunkCoord coord)
    {
        _nextResidencyEpoch =
            _nextResidencyEpoch == ulong.MaxValue ? 1 : _nextResidencyEpoch + 1;
        _residencyEpochs[coord] = _nextResidencyEpoch;
    }

    private void MarkColumnResidencyChanged(ChunkCoord coord)
    {
        var column = ChunkColumnCoord.FromChunk(coord);

        if (!_chunks.Keys.Any(candidate =>
                candidate.X == coord.X && candidate.Z == coord.Z))
        {
            _columnResidencyRevisions.Remove(column);
            return;
        }

        _nextColumnResidencyRevision =
            _nextColumnResidencyRevision == ulong.MaxValue
                ? 1
                : _nextColumnResidencyRevision + 1;
        _columnResidencyRevisions[column] = _nextColumnResidencyRevision;
    }
}
