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
    private readonly Func<FluidRuntimeId, int, int, int, int, bool>
        _nearbyFluidQuery;
    private readonly VolumeBiomeField? _volumeBiomes;
    private readonly VoidSpawnPlatform? _voidSpawnPlatform;
    private readonly CaveSpikeField? _caveSpikes;
    private readonly VolumeStructureField? _volumeStructures;
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
        BlockRegistry blocks,
        VolumeBiomeField? volumeBiomes = null)
        : this(columns, terrain, materials, decorations, generatedFluids,
            structures, dimension, blocks, volumeBiomes, null)
    {
    }

    internal SurfaceChunkMaterializer(
        SurfaceTerrainColumnCache columns,
        SurfaceTerrainField terrain,
        BiomeSurfaceMaterialField materials,
        SurfaceDecorationField decorations,
        GeneratedFluidField generatedFluids,
        SurfaceStructureField structures,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        VolumeBiomeField? volumeBiomes,
        VoidSpawnPlatform? voidSpawnPlatform,
        CaveSpikeField? caveSpikes = null,
        VolumeStructureField? volumeStructures = null)
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
        _volumeBiomes = volumeBiomes;
        _voidSpawnPlatform = voidSpawnPlatform;
        _caveSpikes = caveSpikes;
        _volumeStructures = volumeStructures;
        _nearbyFluidQuery = HasNearbyFluid;
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
                    var surfaceCore = _materials.CoreLayer(sample);

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
                            var depth = checked((uint)(baseY - worldY));
                            block = depth >= surfaceCore.StartDepth
                                ? surfaceCore.Block
                                : (surfaceMaterials ??= _materials.SampleColumn(
                                    sample, worldX, worldZ)).BlockAt(depth);
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
                var normalInChunk = CanPlaceDecorationAt(
                    decorationY, originY, topExclusive);
                var fluidTopInChunk = CanPlaceDecorationAt(
                    decorationY + 1, originY, topExclusive);
                if (!normalInChunk && !fluidTopInChunk)
                    continue;

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
                // Disconnected additive surfaces are owned by the vertical
                // decoration pass, including when they cross chunk boundaries.
                if ((surfaceY > baseY &&
                     _decorations.HasVerticalDecorationsFor(topSample.Primary)) ||
                    !_decorations.HasDecorations(topSample))
                    continue;

                var topMaterials = surfaceY > baseY
                    ? volumeMaterials ??= _materials.SampleColumn(
                        topSample, worldX, worldZ)
                    : surfaceMaterials ??= _materials.SampleColumn(
                        sample, worldX, worldZ);
                var topBlock = topMaterials.BlockAt(0);

                if (normalInChunk)
                {
                    // Generated-fluid occupancy, not only the column range,
                    // decides whether this is a submerged decorator cell.
                    var submerged =
                        _generatedFluids.TryGetColumnBounds(
                            sample, baseY, surfaceFluidCutDepth,
                            worldX, worldZ, out var minimumY,
                            out var maximumY) &&
                        decorationY >= minimumY && decorationY <= maximumY &&
                        !_generatedFluids.FluidAt(
                            sample, baseY, surfaceFluidCutDepth,
                            worldX, (int)decorationY, worldZ,
                            densityVolume.DensityAt(
                                localX, (int)decorationY - originY, localZ)).IsEmpty;

                    var decoration = _decorations.BlockAt(
                        topSample, topBlock, worldX, worldZ,
                        submerged: submerged, supportY: surfaceY,
                        nearbyFluid: _nearbyFluidQuery);
                    if (!decoration.IsAir)
                        chunk.SetBlock(localX, (int)decorationY - originY,
                            localZ, decoration);
                }

                // Water-top decorations occupy the *air above* a source,
                // not its water voxel. This also spans vertical chunk edges.
                if (fluidTopInChunk &&
                    _decorations.HasFluidSurfaceDecorations(topSample))
                {
                    var supportFluid = _generatedFluids.FluidAtEmptyVoxel(
                        sample, baseY, surfaceFluidCutDepth,
                        worldX, (int)decorationY, worldZ);
                    var aboveFluid = _generatedFluids.FluidAtEmptyVoxel(
                        sample, baseY, surfaceFluidCutDepth,
                        worldX, (int)decorationY + 1, worldZ);
                    if (!supportFluid.IsEmpty && aboveFluid.IsEmpty)
                    {
                        var decoration = _decorations.BlockAt(
                            topSample, topBlock, worldX, worldZ,
                            supportY: (int)decorationY,
                            onFluidSurface: true,
                            fluidBelow: supportFluid.Fluid,
                            nearbyFluid: _nearbyFluidQuery);
                        if (!decoration.IsAir)
                            chunk.SetBlock(
                                localX, (int)decorationY + 1 - originY,
                                localZ, decoration);
                    }
                }
            }
        }

        MaterializeVolumeCavePalette(
            chunk, column, densityVolume, originX, originY, originZ);
        MaterializeExteriorFacePalette(
            chunk, column, densityVolume, originX, originY, originZ);

        MaterializeVolumeStructures(chunk, coord);

        MaterializeVerticalDecorations(
            chunk, column, densityVolume,
            originX, originY, originZ);

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
        MaterializeCaveSpikes(
            chunk, column, densityVolume, originX, originY, originZ);
        // SurfaceChunkMaterializer remains the sole procedural voxel writer.
        _voidSpawnPlatform?.Apply(chunk, coord);

        return chunk;
    }

    private bool CanPlaceDecorationAt(long y, int originY, int topExclusive) =>
        y >= originY && y < topExclusive &&
        (_floorY is not { } floor || y > floor) &&
        (_roofY is not { } roof || y < roof);

    private bool HasNearbyFluid(
        FluidRuntimeId fluid, int radius, int worldX, int supportY, int worldZ)
    {
        var distanceSquared = radius * radius;
        for (var dz = -radius; dz <= radius; dz++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var square = dx * dx + dz * dz;
            if (square == 0 || square > distanceSquared)
                continue;
            var px = (long)worldX + dx;
            var pz = (long)worldZ + dz;
            if (px < int.MinValue || px > int.MaxValue ||
                pz < int.MinValue || pz > int.MaxValue)
                continue;
            var (coord, local) = VoxelCoordinates.FromWorld(
                (int)px, 0, (int)pz);
            var neighbor = _columns.Get(coord.X, coord.Z);
            var biome = neighbor.BiomeAt(local.X, local.Z);
            var baseY = neighbor.BaseHeightAt(local.X, local.Z);
            if (supportY <= baseY)
                continue;
            var candidate = _generatedFluids.FluidAtEmptyVoxel(
                biome, baseY,
                neighbor.SurfaceFluidCutDepthAt(local.X, local.Z),
                (int)px, supportY, (int)pz);
            if (candidate.Fluid == fluid &&
                _terrain.DensityAt((int)px, supportY, (int)pz) < 0d)
                return true;
        }
        return false;
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
                if (!voxel.Mask.IsEmpty)
                    chunk.SetMicroblockMask(localX, localY, localZ, voxel.Mask);
                if (!voxel.Surface.IsEmpty)
                    chunk.SetSurfaceState(localX, localY, localZ, voxel.Surface);
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

    /// <summary>
    /// Paints the nearest carved-void face with its authored palette profile.
    /// The nearest void is resolved in terrain-density space (including across
    /// chunk boundaries), so thickness always extends inward from the face.
    /// Equal-distance corners prefer floor, then walls, then ceiling.
    /// </summary>
    private void MaterializeVolumeCavePalette(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX, int originY, int originZ)
    {
        if (_volumeBiomes is not { HasBiomes: true } volumes ||
            _materials.MaximumCavePaintDepth == 0)
            return;

        // A cave face can cross a chunk edge. Neighbor base heights are
        // immutable per X/Z column but used by many vertical face probes.
        // Resolve each halo column once, with query-local bounded storage.
        BiomeField.Sampler? haloSampler = null;
        var haloBaseHeights = new Dictionary<(int X, int Z), int>();

        bool OutsideCarvedVoid(int x, int y, int z)
        {
            var key = (X: x, Z: z);
            if (!haloBaseHeights.TryGetValue(key, out var baseHeight))
            {
                haloSampler ??= _terrain.CreateBiomeSampler();
                baseHeight = _terrain.SampleBaseHeight(haloSampler, x, z);
                haloBaseHeights.Add(key, baseHeight);
            }

            return _terrain.IsCaveVoidAt(
                baseHeight, new WorldVoxelCoord(x, y, z));
        }

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var baseY = column.BaseHeightAt(x, z);
            // Chunks entirely above the carved cavern band cannot contain
            // its rock faces. Avoid the expensive 2D biome placement query.
            if (originY > baseY - 4)
                continue;

            var worldX = originX + x;
            var worldZ = originZ + z;
            BiomeSample? biome = null;
            var ownerResolved = false;
            var reach = (int)_materials.MaximumCavePaintDepth;
            for (var y = 0; y < Chunk.Size; y++)
            {
                var worldY = originY + y;
                if (worldY <= 0 || worldY > baseY - 4 ||
                    (_floorY is { } floorY && worldY <= floorY) ||
                    (_roofY is { } roofY && worldY >= roofY) ||
                    chunk.GetBlock(x, y, z).IsAir ||
                    densityVolume.DensityAt(x, y, z) < 0d)
                    continue;

                bool CarvedVoid(int dx, int dy, int dz)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    var nz = z + dz;
                    var neighborY = worldY + dy;
                    if (neighborY <= 0 || neighborY > baseY - 3)
                        return false;
                    if ((uint)nx < Chunk.Size &&
                        (uint)ny < Chunk.Size &&
                        (uint)nz < Chunk.Size)
                        return densityVolume.DensityAt(nx, ny, nz) < 0d &&
                            neighborY <= column.BaseHeightAt(nx, nz) - 4;

                    return OutsideCarvedVoid(
                        worldX + dx, neighborY, worldZ + dz);
                }

                for (var depth = 0; depth < reach; depth++)
                {
                    var distance = depth + 1;
                    BiomePaletteFace? face = null;
                    if (CarvedVoid(0, distance, 0))
                        face = BiomePaletteFace.Floor;
                    else if (CarvedVoid(-distance, 0, 0) ||
                             CarvedVoid(distance, 0, 0) ||
                             CarvedVoid(0, 0, -distance) ||
                             CarvedVoid(0, 0, distance))
                        face = BiomePaletteFace.Walls;
                    else if (CarvedVoid(0, -distance, 0))
                        face = BiomePaletteFace.Ceiling;

                    if (face is not { } exposed)
                        continue;

                    // Expensive 2D volume-identity sampling is necessary
                    // only for a genuinely exposed carved face. Cache at
                    // most one result per column, even for many Y voxels.
                    if (!ownerResolved)
                    {
                        biome = volumes.SampleCave(worldX, worldZ);
                        ownerResolved = true;
                    }
                    if (biome is not null &&
                        depth < _materials.MaxVolumePaintDepth(biome))
                        chunk.SetBlock(x, y, z,
                            _materials.VolumeBlockAt(
                                biome, worldX, worldY, worldZ,
                                exposed, (uint)depth));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Directional face overrides apply to exposed exterior terrain and
    /// additive volume solids too. Without an authored override this is a
    /// no-op; the existing top-facing layer materialization is preserved.
    /// </summary>
    private void MaterializeExteriorFacePalette(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX, int originY, int originZ)
    {
        if (!_materials.HasExteriorOverrides)
            return;

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var worldX = originX + x;
            var worldZ = originZ + z;
            var surfaceBiome = column.BiomeAt(x, z);
            var baseY = column.BaseHeightAt(x, z);
            var volumePlacement = densityVolume.VolumePlacementAt(x, z);
            // The exterior-face pass is unnecessary when neither the
            // surface nor the possible additive owner overrides walls or
            // ceilings. Avoid scanning all solid voxels in these columns.
            bool HasExteriorOverride(BiomeSample sample) =>
                _materials.HasDirectionalOverride(sample, BiomePaletteFace.Walls) ||
                _materials.HasDirectionalOverride(sample, BiomePaletteFace.Ceiling);
            if (!HasExteriorOverride(surfaceBiome) &&
                (volumePlacement is null ||
                 !HasExteriorOverride(volumePlacement)))
                continue;

            for (var y = 0; y < Chunk.Size; y++)
            {
                var worldY = originY + y;
                if (worldY <= 0 ||
                    (_floorY is { } floorY && worldY <= floorY) ||
                    (_roofY is { } roofY && worldY >= roofY) ||
                    chunk.GetBlock(x, y, z).IsAir ||
                    densityVolume.DensityAt(x, y, z) < 0d)
                    continue;

                var biome = worldY > baseY
                    ? densityVolume.VolumeBiomeAt(x, worldY, z)
                    : surfaceBiome;
                if (biome is null)
                    continue;
                var wall = _materials.HasDirectionalOverride(
                    biome, BiomePaletteFace.Walls);
                var ceiling = _materials.HasDirectionalOverride(
                    biome, BiomePaletteFace.Ceiling);
                if (!wall && !ceiling)
                    continue;

                bool ExteriorAir(int dx, int dy, int dz)
                {
                    var neighborY = worldY + dy;
                    if (neighborY < 0)
                        return false;
                    var nx = x + dx;
                    var ny = y + dy;
                    var nz = z + dz;
                    var voidDensity = (uint)nx < Chunk.Size &&
                        (uint)ny < Chunk.Size &&
                        (uint)nz < Chunk.Size
                        ? densityVolume.DensityAt(nx, ny, nz) < 0d
                        : _terrain.DensityAt(
                            worldX + dx, neighborY, worldZ + dz) < 0d;
                    if (!voidDensity)
                        return false;

                    var neighborPosition = new WorldVoxelCoord(
                        worldX + dx, neighborY, worldZ + dz);
                    // The X/Z column already contains the base heights
                    // needed to classify carved voids inside this tile,
                    // including probes across its vertical chunk faces.
                    var carvedVoid = (uint)nx < Chunk.Size &&
                                     (uint)nz < Chunk.Size
                        ? _terrain.IsCaveVoidAt(
                            column.BaseHeightAt(nx, nz), neighborPosition)
                        : _terrain.IsCaveVoidAt(
                            neighborPosition.X, neighborPosition.Y,
                            neighborPosition.Z);
                    return !carvedVoid;
                }

                var reach = (int)_materials.MaxVolumePaintDepth(biome);
                for (var depth = 0; depth < reach; depth++)
                {
                    var distance = depth + 1;
                    BiomePaletteFace? face = null;
                    if (wall &&
                        (ExteriorAir(-distance, 0, 0) ||
                         ExteriorAir(distance, 0, 0) ||
                         ExteriorAir(0, 0, -distance) ||
                         ExteriorAir(0, 0, distance)))
                        face = BiomePaletteFace.Walls;
                    else if (ceiling && ExteriorAir(0, -distance, 0))
                        face = BiomePaletteFace.Ceiling;

                    if (face is not { } exposed)
                        continue;

                    chunk.SetBlock(x, y, z, _materials.VolumeBlockAt(
                        biome, worldX, worldY, worldZ, exposed, (uint)depth));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Decorates exposed additive tops and carved cave floors or ceilings.
    /// All candidate support checks use immutable terrain density, including
    /// faces crossing vertical chunk boundaries.
    /// </summary>
    /// <summary>
    /// Applies approved, density-safe authored volume templates. They are
    /// queried by world-space anchor; this stage alone writes chunk cells.
    /// Materialization before sprite decorators and spikes prevents those
    /// smaller decorations from truncating larger formations.
    /// </summary>
    private void MaterializeVolumeStructures(Chunk chunk, ChunkCoord coord)
    {
        if (_volumeStructures is not { HasRules: true } formations)
            return;

        var origin = VoxelCoordinates.ChunkOrigin(coord);
        foreach (var voxel in formations.VoxelsForChunk(coord))
        {
            var x = voxel.X - origin.X;
            var y = voxel.Y - origin.Y;
            var z = voxel.Z - origin.Z;
            if ((uint)x >= Chunk.Size ||
                (uint)y >= Chunk.Size ||
                (uint)z >= Chunk.Size ||
                !chunk.GetCell(x, y, z).IsEmpty ||
                !chunk.GetFluid(x, y, z).IsEmpty)
                continue;

            chunk.SetBlock(x, y, z, voxel.Block);
        }
    }

    private void MaterializeVerticalDecorations(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX,
        int originY,
        int originZ)
    {
        if (!_decorations.HasVerticalDecorations)
            return;

        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        for (var localX = 0; localX < Chunk.Size; localX++)
        {
            var baseY = column.BaseHeightAt(localX, localZ);
            var surfaceBiome = column.BiomeAt(localX, localZ);
            var worldX = originX + localX;
            var worldZ = originZ + localZ;
            BiomeSample? underground = null;
            BiomeSurfaceMaterialColumn? volumeMaterials = null;

            for (var localY = 0; localY < Chunk.Size; localY++)
            {
                var worldY = originY + localY;
                if (worldY <= 0 ||
                    (_floorY is { } floorY && worldY <= floorY) ||
                    (_roofY is { } roofY && worldY >= roofY) ||
                    !chunk.GetCell(localX, localY, localZ).IsEmpty ||
                    densityVolume.DensityAt(localX, localY, localZ) >= 0d)
                    continue;

                var volumePlacement = densityVolume.VolumePlacementAt(
                    localX, localZ);
                var belowDensity = localY > 0
                    ? densityVolume.DensityAt(localX, localY - 1, localZ)
                    : _terrain.DensityAt(
                        surfaceBiome, baseY,
                        worldX, worldY - 1, worldZ, volumePlacement);
                var isFloor = belowDensity >= 0d;
                // Additive islands may expose their undersides above the
                // base surface. Resolve the author at the solid voxel above.
                // Do not sample unsupported sky or other volume ownership.
                BiomeSample? additiveCeilingBiome = null;
                if (_decorations.HasCeilingDecorations &&
                    worldY > baseY && volumePlacement is not null)
                {
                    additiveCeilingBiome = densityVolume.VolumeBiomeAt(
                        localX, worldY + 1, localZ);
                }

                var isCeiling = false;
                if (_decorations.HasCeilingDecorations &&
                    (worldY < baseY ||
                     (additiveCeilingBiome is not null &&
                      _decorations.HasCeilingDecorationsFor(
                          additiveCeilingBiome.Primary))))
                {
                    var aboveDensity = localY + 1 < Chunk.Size
                        ? densityVolume.DensityAt(localX, localY + 1, localZ)
                        : _terrain.DensityAt(
                            surfaceBiome, baseY,
                            worldX, worldY + 1, worldZ, volumePlacement);
                    isCeiling = aboveDensity >= 0d;
                }
                if (!isFloor && !isCeiling)
                    continue;

                BiomeSample? decoratorBiome;
                if (worldY > baseY)
                {
                    // The supporting additive voxel, not the empty sky,
                    // owns both top-facing and hanging decorators.
                    if (isFloor && worldY - 1 > baseY)
                    {
                        decoratorBiome = densityVolume.VolumeBiomeAt(
                            localX, worldY - 1, localZ);
                    }
                    else if (isCeiling)
                    {
                        decoratorBiome = additiveCeilingBiome;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    // A solid face outside a carved void does not authorize
                    // a volume decorator on the ordinary terrain surface.
                    if (_volumeBiomes is not { HasBiomes: true } ||
                        !_terrain.IsCaveVoidAt(
                            baseY, new WorldVoxelCoord(worldX, worldY, worldZ)))
                        continue;
                    underground ??= _volumeBiomes.SampleCave(worldX, worldZ);
                    decoratorBiome = underground;
                }

                if (decoratorBiome is null ||
                    !_decorations.HasVerticalDecorationsFor(
                        decoratorBiome.Primary))
                    continue;

                var submerged = !_generatedFluids.FluidAt(
                    surfaceBiome,
                    baseY,
                    column.SurfaceFluidCutDepthAt(localX, localZ),
                    worldX, worldY, worldZ,
                    densityVolume.DensityAt(localX, localY, localZ)).IsEmpty;
                var placement = new SurfacePlacementContext(worldY, 0);

                if (isFloor)
                {
                    var support = localY > 0
                        ? chunk.GetBlock(localX, localY - 1, localZ)
                        : worldY > baseY
                            ? (volumeMaterials ??= _materials.SampleColumn(
                                decoratorBiome, worldX, worldZ)).BlockAt(0)
                            : _materials.VolumeBlockAt(
                                decoratorBiome, worldX, worldY - 1, worldZ);
                    if (!support.IsAir)
                    {
                        var decoration = _decorations.BlockAt(
                            decoratorBiome, support, worldX, worldZ,
                            placement, verticalY: worldY,
                            submerged: submerged);
                        if (!decoration.IsAir)
                        {
                            chunk.SetBlock(localX, localY, localZ, decoration);
                            continue;
                        }
                    }
                }

                if (!isCeiling ||
                    !_decorations.HasCeilingDecorationsFor(
                        decoratorBiome.Primary))
                    continue;

                BlockRuntimeId ceilingSupport;
                if (localY + 1 < Chunk.Size)
                {
                    ceilingSupport =
                        chunk.GetBlock(localX, localY + 1, localZ);
                }
                else if (worldY > baseY)
                {
                    // Resolve the *same* additive palette/depth used by the
                    // solid-material pass, even across a vertical chunk seam.
                    volumeMaterials ??= _materials.SampleColumn(
                        decoratorBiome, worldX, worldZ);
                    var depth = AdditiveDepthAt(
                        densityVolume, surfaceBiome, baseY,
                        localX, localY + 1, localZ,
                        worldX, worldY + 1, worldZ,
                        volumeMaterials.FiniteDepth);
                    ceilingSupport = volumeMaterials.BlockAt(depth);
                }
                else
                {
                    ceilingSupport = _materials.VolumeBlockAt(
                        decoratorBiome, worldX, worldY + 1, worldZ,
                        BiomePaletteFace.Ceiling);
                }
                if (ceilingSupport.IsAir)
                    continue;

                var hanger = _decorations.BlockAt(
                    decoratorBiome, ceilingSupport, worldX, worldZ,
                    placement, verticalY: worldY, submerged: submerged,
                    supportSurface: DecorationSupportSurface.Ceiling);
                if (!hanger.IsAir)
                    chunk.SetBlock(localX, localY, localZ, hanger);
            }
        }
    }

    private void MaterializeCaveSpikes(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX,
        int originY,
        int originZ)
    {
        if (_caveSpikes is not { HasRules: true } spikes ||
            _volumeBiomes is not { HasBiomes: true } volumeBiomes)
            return;

        var maximumHeight = spikes.MaximumHeight;
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var baseY = column.BaseHeightAt(x, z);
            var first = Math.Max(1, originY - maximumHeight + 1);
            var last = Math.Min(baseY - 1,
                originY + Chunk.Size + maximumHeight - 2);
            // When no cavern anchor could possibly contribute a segment
            // to this chunk, skip biome identity/terrain sampling entirely.
            if (last < first)
                continue;

            // A generated spike must occupy empty density inside this
            // chunk. A fully solid column has no contributing formation
            // even if an anchor in a neighboring vertical chunk qualifies.
            var containsVoid = false;
            for (var localY = 0; localY < Chunk.Size; localY++)
            {
                // Sky above the surface and gaps in additive masses are
                // not cave space. Only a carved void below the base surface
                // could contain a generated cave spike.
                if (originY + localY < baseY &&
                    densityVolume.DensityAt(x, localY, z) < 0d)
                {
                    containsVoid = true;
                    break;
                }
            }
            if (!containsVoid)
                continue;

            var worldX = originX + x;
            var worldZ = originZ + z;
            var biome = volumeBiomes.SampleCave(worldX, worldZ);
            if (biome is null || !spikes.HasRulesFor(biome.Primary))
                continue;

            var surfaceBiome = column.BiomeAt(x, z);
            var volumeBiome = densityVolume.VolumePlacementAt(x, z);
            for (var anchorY = first; anchorY <= last; anchorY++)
            {
                for (var direction = 0; direction < 2; direction++)
                {
                    var down = direction == 1;
                    if (!spikes.TrySample(
                            biome.Primary, surfaceBiome, volumeBiome, baseY,
                            worldX, anchorY, worldZ, down, out var placement))
                        continue;

                    for (var segment = 0; segment < placement.Height; segment++)
                    {
                        var y = anchorY + (down ? -segment : segment);
                        var localY = y - originY;
                        if ((uint)localY >= Chunk.Size ||
                            !chunk.GetCell(x, localY, z).IsEmpty ||
                            !chunk.GetFluid(x, localY, z).IsEmpty)
                            continue;

                        chunk.SetCell(x, localY, z,
                            new VoxelCell(placement.Block,
                                state: SpikeSegmentState.Encode(
                                    segment, placement.Height, down)));
                    }
                }
            }
        }
    }

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
