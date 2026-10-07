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
    private readonly SurfaceStructureField _structures;
    private readonly BlockRuntimeId _shellBlock;
    private readonly int? _floorY;
    private readonly int? _roofY;

    public SurfaceChunkMaterializer(
        SurfaceTerrainColumnCache columns,
        SurfaceTerrainField terrain,
        BiomeSurfaceMaterialField materials,
        SurfaceDecorationField decorations,
        GeneratedFluidField generatedFluids,
        SurfaceStructureField structures,
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
        _structures = structures ??
            throw new ArgumentNullException(nameof(structures));
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

                BiomeSurfaceMaterialColumn? surfaceMaterials = null;
                if (firstSolidY <= lastSolidY)
                {
                    surfaceMaterials = _materials.SampleColumn(
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

                        BlockRuntimeId block;

                        if (worldY <= baseY)
                        {
                            surfaceMaterials ??=
                                _materials.SampleColumn(
                                    sample,
                                    worldX,
                                    worldZ);
                            var depth =
                                checked(
                                    (uint)(
                                        baseY -
                                        worldY));
                            block =
                                surfaceMaterials.BlockAt(
                                    depth);
                        }
                        else
                        {
                            var volumeSample =
                                _terrain.VolumeBiomeAt(
                                    worldX,
                                    worldY,
                                    worldZ) ??
                                throw new InvalidOperationException(
                                    "Additive solid voxel has no volume biome owner.");
                            var volumeMaterials =
                                _materials.SampleColumn(
                                    volumeSample,
                                    worldX,
                                    worldZ);
                            var depth =
                                _terrain.AdditiveDepthAt(
                                    sample,
                                    baseY,
                                    worldX,
                                    worldY,
                                    worldZ,
                                    volumeMaterials.FiniteDepth);
                            block =
                                volumeMaterials.BlockAt(
                                    depth);
                        }

                        chunk.SetBlock(
                            localX,
                            worldY - originY,
                            localZ,
                            block);
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

                var topSample =
                    surfaceY > baseY
                        ? _terrain.VolumeBiomeAt(
                            worldX,
                            surfaceY,
                            worldZ) ??
                          throw new InvalidOperationException(
                              "Additive surface has no volume biome owner.")
                        : sample;
                var topMaterials =
                    _materials.SampleColumn(
                        topSample,
                        worldX,
                        worldZ);
                var decoration =
                    _decorations.BlockAt(
                        topSample,
                        topMaterials.BlockAt(
                            0),
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

        MaterializeStructures(
            chunk,
            coord,
            originY,
            topExclusive);

        MaterializeGeneratedFluids(
            chunk,
            column,
            originX,
            originY,
            originZ,
            topExclusive);

        return chunk;
    }

    private void MaterializeStructures(
        Chunk chunk,
        ChunkCoord coord,
        int originY,
        int topExclusive)
    {
        if (!_structures.HasRules)
        {
            return;
        }

        var origin =
            VoxelCoordinates.ChunkOrigin(
                coord);

        foreach (var placement in
                 _structures.PlacementsForChunk(
                     coord.X,
                     coord.Z))
        {
            foreach (var voxel in
                     placement.Voxels)
            {
                if (voxel.Y < originY ||
                    voxel.Y >= topExclusive ||
                    voxel.X < origin.X ||
                    voxel.X >=
                        origin.X +
                        Chunk.Size ||
                    voxel.Z < origin.Z ||
                    voxel.Z >=
                        origin.Z +
                        Chunk.Size)
                {
                    continue;
                }

                if ((_floorY is
                         { } floorY &&
                     voxel.Y ==
                         floorY) ||
                    (_roofY is
                         { } roofY &&
                     voxel.Y ==
                         roofY))
                {
                    continue;
                }

                chunk.SetCell(
                    voxel.X -
                    origin.X,
                    voxel.Y -
                    originY,
                    voxel.Z -
                    origin.Z,
                    voxel.Cell);
            }
        }
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
