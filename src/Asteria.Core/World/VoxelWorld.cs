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

public sealed class VoxelWorld
{
    private readonly Dictionary<ChunkCoord, Chunk> _chunks = [];

    public ulong Revision { get; private set; }

    public int ChunkCount => _chunks.Count;

    public IEnumerable<ChunkCoord> LoadedChunkCoords => _chunks.Keys;

    public void InsertChunk(ChunkCoord coord, Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (!_chunks.TryAdd(coord, chunk))
        {
            throw new InvalidOperationException($"Chunk {coord} is already resident.");
        }

        Revision++;
    }

    public bool ContainsChunk(ChunkCoord coord) => _chunks.ContainsKey(coord);

    public bool TryGetChunk(ChunkCoord coord, out Chunk chunk) =>
        _chunks.TryGetValue(coord, out chunk!);

    public Chunk GetChunk(ChunkCoord coord) =>
        _chunks.TryGetValue(coord, out var chunk)
            ? chunk
            : throw new KeyNotFoundException($"Chunk is not resident: {coord}");

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

    public VoxelWorld CloneForWorker()
    {
        var clone = new VoxelWorld();

        foreach (var (coord, chunk) in _chunks)
        {
            clone._chunks.Add(coord, chunk.CloneForWorker());
        }

        clone.Revision = Revision;
        return clone;
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
}
