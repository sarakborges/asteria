namespace Asteria.Core.World;

/// <summary>
/// Small deterministic entry platform for Void worlds. No terrain, fluids,
/// decorations or procedurally placed structures exist outside authored shells
/// and this platform. The immutable generator owns its position and block.
/// </summary>
internal sealed class VoidSpawnPlatform
{
    private const int Radius = 2;
    private readonly int _x;
    private readonly int _z;
    private readonly int _y;
    private readonly BlockRuntimeId _block;

    public VoidSpawnPlatform(
        DimensionDefinition dimension,
        BlockRegistry blocks,
        BiomeDefinition biome)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(biome);
        var layer = biome.SurfaceLayers.FirstOrDefault() ??
            throw new ArgumentException("Void spawn requires a biome surface block.",
                nameof(biome));
        _block = blocks.GetId(layer.Block);
        _x = dimension.Spawn.X;
        _z = dimension.Spawn.Z;
        var minimum = checked((dimension.Shell?.FloorY ?? 0) + 2);
        var maximum = dimension.Shell?.RoofY is { } roof
            ? Math.Max(minimum, roof - 2)
            : int.MaxValue;
        _y = Math.Clamp(dimension.SeaLevel, minimum, maximum);
    }

    public GeneratedSurfaceDestination Spawn =>
        new(_x, checked(_y + 1), _z);

    public bool Covers(int x, int z) =>
        Math.Abs((long)x - _x) <= Radius &&
        Math.Abs((long)z - _z) <= Radius;

    public bool IsNear(int x, int z, int radius) =>
        radius >= 0 &&
        Math.Abs((long)x - _x) <= (long)radius + Radius &&
        Math.Abs((long)z - _z) <= (long)radius + Radius;

    public bool IntersectsChunk(int chunkX, int chunkZ)
    {
        var minX = (long)chunkX * Chunk.Size;
        var minZ = (long)chunkZ * Chunk.Size;
        return minX <= (long)_x + Radius &&
            minX + Chunk.Size - 1 >= (long)_x - Radius &&
            minZ <= (long)_z + Radius &&
            minZ + Chunk.Size - 1 >= (long)_z - Radius;
    }

    public int SurfaceHeight(int x, int z) => Covers(x, z) ? _y : 0;

    public bool IsSolidAt(int x, int y, int z) =>
        y == _y && Covers(x, z);

    public ChunkSurfaceRange SurfaceRange(int chunkX, int chunkZ) =>
        new(0, IntersectsChunk(chunkX, chunkZ) ? _y : 0);

    public void Apply(Chunk chunk, ChunkCoord coord)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var originY = (long)coord.Y * Chunk.Size;
        if (_y < originY || _y >= originY + Chunk.Size ||
            !IntersectsChunk(coord.X, coord.Z))
            return;

        var originX = (long)coord.X * Chunk.Size;
        var originZ = (long)coord.Z * Chunk.Size;
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            if (Covers(checked((int)(originX + x)),
                       checked((int)(originZ + z))))
                chunk.SetBlock(x, (int)(_y - originY), z, _block);
        }
    }
}
