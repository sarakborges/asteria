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
        var densityVolume =
            _terrain.SampleDensityVolume(
                column,
                originX,
                originY,
                originZ);
        var topExclusive = checked(originY + Chunk.Size);

        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var surfaceY = column.HeightAt(localX, localZ);
                var baseY = column.BaseHeightAt(localX, localZ);
                var surfaceFluidCutDepth =
                    column.SurfaceFluidCutDepthAt(
                        localX,
                        localZ);
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
                BiomeSample? volumeSample = null;
                BiomeSurfaceMaterialColumn? volumeMaterials = null;

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

                        var localY =
                            worldY -
                            originY;
                        if (densityVolume.DensityAt(
                                localX,
                                localY,
                                localZ) < 0d)
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
                            volumeSample ??=
                                densityVolume.VolumeBiomeAt(
                                    localX,
                                    worldY,
                                    localZ) ??
                                throw new InvalidOperationException(
                                    "Additive solid voxel has no volume biome owner.");
                            volumeMaterials ??=
                                _materials.SampleColumn(
                                    volumeSample,
                                    worldX,
                                    worldZ);
                            var depth =
                                AdditiveDepthAt(
                                    densityVolume,
                                    sample,
                                    baseY,
                                    localX,
                                    localY,
                                    localZ,
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
                if (_generatedFluids.TryGetColumnBounds(
                        sample,
                        baseY,
                        surfaceFluidCutDepth,
                        worldX,
                        worldZ,
                        out var fluidMinimumY,
                        out var fluidMaximumY) &&
                    decorationY >=
                        fluidMinimumY &&
                    decorationY <=
                        fluidMaximumY)
                {
                    continue;
                }
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
                        ? volumeSample ??
                          densityVolume.VolumeBiomeAt(
                              localX,
                              surfaceY,
                              localZ) ??
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

        MaterializeGeneratedFluids(
            chunk,
            column,
            densityVolume,
            originX,
            originY,
            originZ,
            topExclusive);

        MaterializeStructures(
            chunk,
            coord,
            originY,
            topExclusive);

        return chunk;
    }

    private uint AdditiveDepthAt(
        TerrainDensityVolume densityVolume,
        BiomeSample surfaceBiome,
        int baseY,
        int localX,
        int localY,
        int localZ,
        int worldX,
        int worldY,
        int worldZ,
        uint finiteDepth)
    {
        var volumeBiome =
            densityVolume.VolumePlacementAt(
                localX,
                localZ);
        uint depth = 0;

        while (depth < finiteDepth)
        {
            var above =
                (long)worldY +
                depth +
                1L;
            if (above > int.MaxValue)
            {
                break;
            }

            var localAbove =
                (long)localY +
                depth +
                1L;
            var density =
                localAbove < densityVolume.Height
                    ? densityVolume.DensityAt(
                        localX,
                        (int)localAbove,
                        localZ)
                    : _terrain.DensityAt(
                        surfaceBiome,
                        baseY,
                        worldX,
                        (int)above,
                        worldZ,
                        volumeBiome);

            if (density < 0d)
            {
                break;
            }

            depth++;
        }

        return depth;
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
        var placements =
            _structures.PlacementsForChunk(
                coord.X,
                coord.Z);

        // Placement bounds include blocks, fluids and explicit clears.
        // Surface structures must not scan underground occupancy or
        // rasterize every payload in unrelated vertical chunk bands.
        bool IntersectsThisChunk(SurfaceStructurePlacement placement) =>
            placement.MinimumY < topExclusive &&
            placement.MaximumY >= originY;

        if (!placements.Any(IntersectsThisChunk))
        {
            return;
        }

        var needsBaseOccupancy =
            placements.Any(placement =>
                IntersectsThisChunk(placement) &&
                placement.Generation.ReplacePolicy ==
                StructureReplacePolicy.AirOnly);
        var baseOccupied =
            needsBaseOccupancy
                ? new HashSet<int>()
                : null;

        if (baseOccupied is not null)
        {
            chunk.VisitBlockCells(
                (x, y, z, _) =>
                    baseOccupied.Add(
                        StructureCellKey(
                            x,
                            y,
                            z)));
            chunk.VisitFluidCells(
                (x, y, z, _) =>
                    baseOccupied.Add(
                        StructureCellKey(
                            x,
                            y,
                            z)));
        }

        var claimed =
            new HashSet<int>();

        foreach (var placement in
                 placements)
        {
            if (!IntersectsThisChunk(placement))
            {
                continue;
            }

            foreach (var voxel in
                     placement.ClearVoxels)
            {
                if (!TryStructureLocal(
                        voxel.X,
                        voxel.Y,
                        voxel.Z,
                        origin,
                        originY,
                        topExclusive,
                        out var localX,
                        out var localY,
                        out var localZ))
                {
                    continue;
                }

                var key =
                    StructureCellKey(
                        localX,
                        localY,
                        localZ);
                if (!CanReplaceStructureCell(
                        placement.Generation.ReplacePolicy,
                        key,
                        claimed,
                        baseOccupied))
                {
                    continue;
                }

                chunk.SetCell(
                    localX,
                    localY,
                    localZ,
                    VoxelCell.Empty);
                chunk.SetFluid(
                    localX,
                    localY,
                    localZ,
                    FluidCell.Empty);
                claimed.Add(
                    key);
            }

            foreach (var voxel in
                     placement.Voxels)
            {
                if (!TryStructureLocal(
                        voxel.X,
                        voxel.Y,
                        voxel.Z,
                        origin,
                        originY,
                        topExclusive,
                        out var localX,
                        out var localY,
                        out var localZ))
                {
                    continue;
                }

                var key =
                    StructureCellKey(
                        localX,
                        localY,
                        localZ);
                if (!CanReplaceStructureCell(
                        placement.Generation.ReplacePolicy,
                        key,
                        claimed,
                        baseOccupied))
                {
                    continue;
                }

                chunk.SetFluid(
                    localX,
                    localY,
                    localZ,
                    FluidCell.Empty);
                chunk.SetCell(
                    localX,
                    localY,
                    localZ,
                    voxel.Cell);
                claimed.Add(
                    key);
            }

            foreach (var voxel in
                     placement.FluidVoxels)
            {
                if (!TryStructureLocal(
                        voxel.X,
                        voxel.Y,
                        voxel.Z,
                        origin,
                        originY,
                        topExclusive,
                        out var localX,
                        out var localY,
                        out var localZ))
                {
                    continue;
                }

                var key =
                    StructureCellKey(
                        localX,
                        localY,
                        localZ);
                if (!CanReplaceStructureCell(
                        placement.Generation.ReplacePolicy,
                        key,
                        claimed,
                        baseOccupied))
                {
                    continue;
                }

                chunk.SetCell(
                    localX,
                    localY,
                    localZ,
                    VoxelCell.Empty);
                chunk.SetFluid(
                    localX,
                    localY,
                    localZ,
                    voxel.Fluid);
                claimed.Add(
                    key);
            }
        }
    }

    private bool TryStructureLocal(
        int worldX,
        int worldY,
        int worldZ,
        (int X, int Y, int Z) origin,
        int originY,
        int topExclusive,
        out int localX,
        out int localY,
        out int localZ)
    {
        localX =
            localY =
            localZ =
            0;

        if (worldY < originY ||
            worldY >= topExclusive ||
            worldX < origin.X ||
            worldX >=
                origin.X +
                Chunk.Size ||
            worldZ < origin.Z ||
            worldZ >=
                origin.Z +
                Chunk.Size ||
            (_floorY is { } floorY &&
             worldY == floorY) ||
            (_roofY is { } roofY &&
             worldY == roofY))
        {
            return false;
        }

        localX =
            worldX -
            origin.X;
        localY =
            worldY -
            originY;
        localZ =
            worldZ -
            origin.Z;
        return true;
    }

    private static bool CanReplaceStructureCell(
        StructureReplacePolicy policy,
        int key,
        IReadOnlySet<int> claimed,
        IReadOnlySet<int>? baseOccupied) =>
        policy switch
        {
            StructureReplacePolicy.Any =>
                true,
            StructureReplacePolicy.AirOnly =>
                !claimed.Contains(
                    key) &&
                !(baseOccupied?.Contains(
                      key) ??
                  false),
            StructureReplacePolicy.Terrain =>
                !claimed.Contains(
                    key),
            _ =>
                throw new InvalidOperationException(
                    $"Unknown structure replace policy {policy}."),
        };

    private static int StructureCellKey(
        int x,
        int y,
        int z) =>
        x |
        y << 8 |
        z << 16;

    private void MaterializeGeneratedFluids(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
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
                var surfaceCutDepth =
                    column.SurfaceFluidCutDepthAt(
                        localX,
                        localZ);

                var worldX =
                    checked(
                        originX +
                        localX);
                var worldZ =
                    checked(
                        originZ +
                        localZ);

                if (!_generatedFluids.TryGetColumnBounds(
                        sample,
                        baseY,
                        surfaceCutDepth,
                        worldX,
                        worldZ,
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
                        surfaceCutDepth,
                        worldX,
                        worldY,
                        worldZ,
                        densityVolume.DensityAt(
                            localX,
                            localY,
                            localZ));

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
