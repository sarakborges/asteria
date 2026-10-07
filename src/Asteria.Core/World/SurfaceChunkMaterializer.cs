namespace Asteria.Core.World;

/// <summary>
/// Only the materialization stage writes chunk voxels. It consumes immutable
/// terrain/biome snapshots and material/decorator capabilities.
/// </summary>
public sealed class SurfaceChunkMaterializer
{
    private readonly SurfaceTerrainColumnCache _columns;
    private readonly SurfaceTerrainField _terrain;
    private readonly BiomeSurfaceMaterialField _materials;
    private readonly SurfaceDecorationField _decorations;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly BlockRuntimeId _shellBlock;
    private readonly int? _floorY;
    private readonly int? _roofY;

    public SurfaceChunkMaterializer(
        SurfaceTerrainColumnCache columns,
        SurfaceTerrainField terrain,
        BiomeSurfaceMaterialField materials,
        SurfaceDecorationField decorations,
        GeneratedFluidField generatedFluids,
        DimensionDefinition dimension,
        BlockRegistry blocks)
    {
        _columns = columns ??
            throw new ArgumentNullException(nameof(columns));
        _terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        _materials = materials ??
            throw new ArgumentNullException(nameof(materials));
        _decorations = decorations ??
            throw new ArgumentNullException(nameof(decorations));
        _generatedFluids = generatedFluids ??
            throw new ArgumentNullException(nameof(generatedFluids));
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
                var baseY = column.BaseHeightAt(localX, localZ);
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

                        if (_terrain.DensityAt(
                                sample, baseY, worldX, worldY, worldZ) < 0d)
                        {
                            continue;
                        }

                        var depth = worldY <= baseY
                            ? checked((uint)(baseY - worldY))
                            : _terrain.AdditiveDepthAt(
                                sample,
                                baseY,
                                worldX,
                                worldY,
                                worldZ,
                                materials.FiniteDepth);
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

        MaterializeGeneratedFluids(
            chunk,
            column,
            originX,
            originY,
            originZ,
            topExclusive);

        return chunk;
    }

    private void MaterializeGeneratedFluids(
        Chunk chunk,
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ,
        int topExclusive)
    {
        if (!_generatedFluids.HasRules)
        {
            return;
        }

        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var sample = column.BiomeAt(localX, localZ);
                var baseY = column.BaseHeightAt(localX, localZ);

                if (!_generatedFluids.TryGetColumnBounds(
                        sample,
                        baseY,
                        out var minimumY,
                        out var maximumY))
                {
                    continue;
                }

                var firstY = Math.Max(originY, minimumY);
                var lastY = Math.Min(topExclusive - 1, maximumY);
                if (firstY > lastY)
                {
                    continue;
                }

                var worldX = checked(originX + localX);
                var worldZ = checked(originZ + localZ);

                for (var worldY = firstY; worldY <= lastY; worldY++)
                {
                    var localY = worldY - originY;

                    if (!chunk.GetCell(localX, localY, localZ).IsEmpty ||
                        !chunk.GetFluid(localX, localY, localZ).IsEmpty)
                    {
                        continue;
                    }

                    var fluid = _generatedFluids.FluidAt(
                        sample,
                        baseY,
                        worldY,
                        _terrain.DensityAt(
                            sample,
                            baseY,
                            worldX,
                            worldY,
                            worldZ));

                    if (!fluid.IsEmpty)
                    {
                        chunk.SetFluid(
                            localX,
                            localY,
                            localZ,
                            fluid);
                    }
                }
            }
        }
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
