namespace Asteria.Core.World;

/// <summary>
/// Only the materialization stage writes chunk voxels. It consumes immutable
/// terrain/biome snapshots and material/decorator capabilities.
/// </summary>
public sealed class SurfaceChunkMaterializer
{
    private readonly SurfaceTerrainColumnCache _columns;
    private readonly BiomeSurfaceMaterialField _materials;
    private readonly SurfaceDecorationField _decorations;
    private readonly BlockRuntimeId _shellBlock;
    private readonly int? _floorY;
    private readonly int? _roofY;

    public SurfaceChunkMaterializer(
        SurfaceTerrainColumnCache columns,
        BiomeSurfaceMaterialField materials,
        SurfaceDecorationField decorations,
        DimensionDefinition dimension,
        BlockRegistry blocks)
    {
        _columns = columns ??
            throw new ArgumentNullException(nameof(columns));
        _materials = materials ??
            throw new ArgumentNullException(nameof(materials));
        _decorations = decorations ??
            throw new ArgumentNullException(nameof(decorations));
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(blocks);

        _shellBlock = dimension.Shell is { } shell
            ? blocks.GetId(shell.Block)
            : BlockRuntimeId.Air;
        _floorY = dimension.Shell?.FloorY;
        _roofY = dimension.Shell?.RoofY;
    }

    public Chunk Materialize(ChunkCoord coord)
    {
        if (coord.Y < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coord),
                "Sphere chunk Y cannot be negative.");
        }

        var chunk = new Chunk();
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);
        var column = _columns.Get(coord.X, coord.Z);
        var topExclusive = checked(originY + Chunk.Size);

        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var surfaceY = column.HeightAt(localX, localZ);
                var worldX = checked(originX + localX);
                var worldZ = checked(originZ + localZ);
                var sample = column.BiomeAt(localX, localZ);

                // Sphere Shell planes remain authoritative even above terrain.
                if (!_shellBlock.IsAir)
                {
                    SetShellIfInChunk(
                        chunk, localX, localZ, originY, topExclusive, _floorY);
                    SetShellIfInChunk(
                        chunk, localX, localZ, originY, topExclusive, _roofY);
                }

                var firstSolidY = Math.Max(originY, _floorY ?? 0);
                var lastSolidY = Math.Min(
                    Math.Min(topExclusive - 1, surfaceY),
                    _roofY ?? int.MaxValue);

                BiomeSurfaceMaterialColumn? materials = null;
                if (firstSolidY <= lastSolidY)
                {
                    materials = _materials.SampleColumn(
                        sample,
                        worldX,
                        worldZ);

                    for (var worldY = firstSolidY;
                         worldY <= lastSolidY;
                         worldY++)
                    {
                        if (!_shellBlock.IsAir &&
                            (worldY == _floorY || worldY == _roofY))
                        {
                            continue;
                        }

                        var depth = checked((uint)(surfaceY - worldY));
                        chunk.SetBlock(
                            localX,
                            worldY - originY,
                            localZ,
                            materials.BlockAt(depth));
                    }
                }

                var decorationY = (long)surfaceY + 1;
                if (decorationY < originY ||
                    decorationY >= topExclusive ||
                    (_floorY is { } floorY && decorationY < floorY) ||
                    (_roofY is { } roofY && decorationY > roofY) ||
                    (_floorY is { } shellFloor && decorationY == shellFloor) ||
                    (_roofY is { } shellRoof && decorationY == shellRoof))
                {
                    continue;
                }

                materials ??= _materials.SampleColumn(
                    sample,
                    worldX,
                    worldZ);
                var decoration = _decorations.BlockAt(
                    sample,
                    materials.BlockAt(0),
                    worldX,
                    worldZ);

                if (!decoration.IsAir)
                {
                    chunk.SetBlock(
                        localX,
                        (int)decorationY - originY,
                        localZ,
                        decoration);
                }
            }
        }

        return chunk;
    }

    private void SetShellIfInChunk(
        Chunk chunk,
        int localX,
        int localZ,
        int originY,
        int topExclusive,
        int? shellY)
    {
        if (shellY is not { } y ||
            y < originY ||
            y >= topExclusive)
        {
            return;
        }

        chunk.SetBlock(
            localX,
            y - originY,
            localZ,
            _shellBlock);
    }
}
