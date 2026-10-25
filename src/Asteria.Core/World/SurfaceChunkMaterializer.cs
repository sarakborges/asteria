namespace Asteria.Core.World;

/// <summary>
/// Only the materialization stage writes chunk voxels. It consumes immutable
/// terrain/biome snapshots and material/decorator capabilities.
/// </summary>
public sealed class SurfaceChunkMaterializer
{
    private readonly record struct ColumnMaterializationCache(
        BiomeSurfaceMaterialColumn? SurfaceMaterials,
        BiomeSample? VolumeSample,
        BiomeSurfaceMaterialColumn? VolumeMaterials);

    private readonly record struct PendingGeneratedObject(
        int X, int Y, int Z, string Id, BlockFace Face,
        bool Submerged = false, FluidRuntimeId SupportFluid = default)
    {
        public (int X, int Y, int Z) Target => Face switch
        {
            BlockFace.Top => (X, Y + 1, Z),
            BlockFace.Bottom => (X, Y - 1, Z),
            BlockFace.Right => (X + 1, Y, Z),
            BlockFace.Left => (X - 1, Y, Z),
            BlockFace.Front => (X, Y, Z + 1),
            BlockFace.Back => (X, Y, Z - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(Face))
        };
    }

    private readonly SurfaceTerrainColumnCache _columns;
    private readonly SurfaceTerrainField _terrain;
    private readonly BiomeSurfaceMaterialField _materials;
    private readonly SurfaceDecorationField _decorations;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly BlockRegistry _blocks;
    private readonly SurfaceStructureField _structures;
    private readonly Func<FluidRuntimeId, int, int, int, int, bool>
        _nearbyFluidQuery;
    private readonly VolumeBiomeField? _volumeBiomes;
    private readonly VoidSpawnPlatform? _voidSpawnPlatform;
    private readonly CaveSpikeField? _caveSpikes;
    private readonly VolumeStructureField? _volumeStructures;
    private readonly LayerVeinField? _veins;
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
        VolumeStructureField? volumeStructures = null,
        LayerVeinField? veins = null)
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
        _veins = veins;
        _nearbyFluidQuery = HasNearbyFluid;
        ArgumentNullException.ThrowIfNull(dimension);
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));

        // Flat and Normal respect authored Sphere Shells; Void does not.
        _shellBlock = voidSpawnPlatform is null && dimension.Shell is { } shell
            ? blocks.GetId(shell.Block)
            : BlockRuntimeId.Air;
        _floorY = voidSpawnPlatform is null ? dimension.Shell?.FloorY : null;
        _roofY = voidSpawnPlatform is null ? dimension.Shell?.RoofY : null;
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
        if (_voidSpawnPlatform is not null)
        {
            // Void has only its small spawn platform: no authored shell,
            // lining, terrain, caves, fluids or decorations.
            _voidSpawnPlatform.Apply(chunk, coord);
            return chunk;
        }

        var column = _columns.Get(coord.X, coord.Z);
        var densityVolume =
            _terrain.SampleDensityVolume(
                column,
                originX,
                originY,
                originZ);
        var topExclusive = checked(originY + Chunk.Size);
        List<PendingGeneratedObject>? pendingObjects = null;
        Span<BlockRuntimeId> terrainBlocks =
            stackalloc BlockRuntimeId[Chunk.Volume];
        terrainBlocks.Clear();
        var columnMaterializationCaches =
            new ColumnMaterializationCache[Chunk.Area];

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
                uint? volumeCoverDepth = null;

                if (firstSolidY <= lastSolidY)
                {
                    var surfaceCore = _materials.CoreLayer(sample);
                    var surfaceCoverDepth =
                        _materials.GeologySurfaceCoverDepth(sample);

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
                        if (!densityVolume.IsSolidAtUnchecked(
                                localX,
                                localY,
                                localZ))
                        {
                            continue;
                        }

                        // Resolve an exposed surface deposit before volumetric
                        // geology. When the surface rule applies (notably ocean
                        // seabeds), the volumetric material for this same voxel
                        // would be immediately overwritten and need not be sampled.
                        var isExposedTerrainTop = worldY == surfaceY;
                        if (!isExposedTerrainTop &&
                            worldY < surfaceY &&
                            (long)surfaceY - worldY <= 2L)
                        {
                            isExposedTerrainTop = true;
                            for (var aboveY = worldY + 1;
                                 aboveY <= surfaceY;
                                 aboveY++)
                            {
                                var aboveLocalY = aboveY - originY;
                                if (aboveLocalY < 0 || aboveLocalY >= Chunk.Size ||
                                    densityVolume.IsSolidAtUnchecked(
                                        localX, aboveLocalY, localZ))
                                {
                                    isExposedTerrainTop = false;
                                    break;
                                }
                            }
                        }

                        BlockRuntimeId exposedBlock = BlockRuntimeId.Air;
                        var hasExposedGeology = false;
                        if (isExposedTerrainTop)
                        {
                            var surfaceOwner = worldY > baseY
                                ? densityVolume.VolumeBiomeAt(localX, worldY, localZ) ??
                                  throw new InvalidOperationException(
                                      "Additive surface has no volume biome owner.")
                                : sample;
                            if (!_materials.SurfaceGeologyRequiresSubmerged(surfaceOwner) ||
                                HasGeneratedFluidAboveSurface(
                                    surfaceOwner, sample, baseY, surfaceFluidCutDepth,
                                    worldX, worldY, worldZ,
                                    densityVolume.VolumePlacementAt(localX, localZ)))
                            {
                                hasExposedGeology = _materials.TrySurfaceGeologyBlockAt(
                                    surfaceOwner, worldX, worldY, worldZ,
                                    out exposedBlock);
                            }
                        }

                        BlockRuntimeId block;

                        if (hasExposedGeology)
                        {
                            block = exposedBlock;
                        }
                        else if (worldY <= baseY)
                        {
                            var depth = checked((uint)(baseY - worldY));
                            surfaceMaterials ??= _materials.SampleColumn(
                                sample, worldX, worldZ);
                            var useSurfaceCover = surfaceCoverDepth is { } authoredCoverDepth
                                ? depth < authoredCoverDepth
                                : depth == 0 || depth < surfaceMaterials.FiniteDepth;
                            if (useSurfaceCover)
                            {
                                block = surfaceMaterials.BlockAt(depth);
                            }
                            else if (!_materials.TryGeologyBlockAt(
                                sample, worldX, worldY, worldZ, out block))
                            {
                                // Biomes without authored geology preserve the
                                // existing depth-based core-material contract.
                                block = depth >= surfaceCore.StartDepth
                                    ? surfaceCore.Block
                                    : surfaceMaterials.BlockAt(depth);
                            }
                        }
                        else
                        {
                            if (volumeSample is null)
                            {
                                volumeSample =
                                    densityVolume.VolumeBiomeAt(
                                        localX,
                                        worldY,
                                        localZ) ??
                                    throw new InvalidOperationException(
                                        "Additive solid voxel has no volume biome owner.");
                                volumeCoverDepth =
                                    _materials.GeologySurfaceCoverDepth(volumeSample);
                            }
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
                            var useSurfaceCover = volumeCoverDepth is { } authoredCoverDepth
                                ? depth < authoredCoverDepth
                                : depth == 0 || depth < volumeMaterials.FiniteDepth;
                            if (useSurfaceCover)
                            {
                                block = volumeMaterials.BlockAt(depth);
                            }
                            else if (!_materials.TryGeologyBlockAt(
                                volumeSample, worldX, worldY, worldZ, out block))
                            {
                                block = volumeMaterials.BlockAt(depth);
                            }
                        }

                        terrainBlocks[
                            localX + Chunk.Size *
                            (localZ + Chunk.Size * localY)] = block;
                    }
                }

                var columnIndex = localX + Chunk.Size * localZ;
                columnMaterializationCaches[columnIndex] =
                    new ColumnMaterializationCache(
                        surfaceMaterials, volumeSample, volumeMaterials);
            }
        }

        // Apply the prepared terrain in storage order: X is contiguous, then
        // Z, then Y. Geology sampling stays column-oriented, but palette and
        // occupancy writes now touch adjacent voxel indices.
        for (var localY = 0; localY < Chunk.Size; localY++)
        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        for (var localX = 0; localX < Chunk.Size; localX++)
        {
            var block = terrainBlocks[
                localX + Chunk.Size * (localZ + Chunk.Size * localY)];
            if (!block.IsAir)
                chunk.SetBlock(localX, localY, localZ, block);
        }

        // Decorations are evaluated after terrain is committed, so every
        // support/occupancy query observes the complete terrain layer.
        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var surfaceY = column.HeightAt(localX, localZ);
                var baseY = column.BaseHeightAt(localX, localZ);
                var surfaceFluidCutDepth =
                    column.SurfaceFluidCutDepthAt(localX, localZ);
                var worldX = checked(originX + localX);
                var worldZ = checked(originZ + localZ);
                var sample = column.BiomeAt(localX, localZ);
                var columnIndex = localX + Chunk.Size * localZ;
                var columnCache = columnMaterializationCaches[columnIndex];
                var surfaceMaterials = columnCache.SurfaceMaterials;
                var volumeSample = columnCache.VolumeSample;
                var volumeMaterials = columnCache.VolumeMaterials;

                var decorationY = (long)surfaceY + 1;
                var normalInChunk = CanPlaceDecorationAt(
                    decorationY, originY, topExclusive);
                var fluidTopInChunk = CanPlaceDecorationAt(
                    decorationY + 1, originY, topExclusive);
                var fluidObjectSupportInChunk = CanPlaceDecorationAt(
                    decorationY, originY, topExclusive);
                var objectSupportInChunk = CanPlaceDecorationAt(
                    surfaceY, originY, topExclusive);
                if (!normalInChunk && !fluidTopInChunk && !objectSupportInChunk &&
                    !fluidObjectSupportInChunk)
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

                var considerObjects = objectSupportInChunk &&
                    _decorations.HasObjectDecorations(topSample);
                if (normalInChunk || considerObjects)
                {
                    // Generated-fluid occupancy, not only the column range,
                    // decides whether this is a submerged decorator cell.
                    var decorationTerrainSolid = normalInChunk
                        ? densityVolume.IsSolidAtUnchecked(
                            localX, (int)decorationY - originY, localZ)
                        : _terrain.DensityAt(
                            sample, baseY, worldX, (int)decorationY, worldZ,
                            densityVolume.VolumePlacementAt(localX, localZ)) >= 0d;
                    var submerged =
                        _generatedFluids.TryGetColumnBounds(
                            sample, baseY, surfaceFluidCutDepth,
                            worldX, worldZ, out var minimumY,
                            out var maximumY) &&
                        decorationY >= minimumY && decorationY <= maximumY &&
                        !_generatedFluids.FluidAt(
                            sample, baseY, surfaceFluidCutDepth,
                            worldX, (int)decorationY, worldZ,
                            decorationTerrainSolid).IsEmpty;

                    if (normalInChunk)
                    {
                        var decoration = _decorations.BlockAt(
                            topSample, topBlock, worldX, worldZ,
                            submerged: submerged, supportY: surfaceY,
                            nearbyFluid: _nearbyFluidQuery);
                        if (!decoration.IsAir)
                            chunk.SetBlock(localX, (int)decorationY - originY,
                                localZ, decoration);
                    }
                    if (considerObjects)
                    {
                        var objectId = _decorations.ObjectAt(
                            topSample, topBlock, worldX, worldZ,
                            submerged: submerged, supportY: surfaceY,
                            nearbyFluid: _nearbyFluidQuery);
                        if (objectId is not null)
                        {
                            pendingObjects ??= [];
                            pendingObjects.Add(new PendingGeneratedObject(
                                localX, surfaceY - originY, localZ,
                                objectId, BlockFace.Top, submerged));
                        }
                    }
                }

                // Water-top decorations occupy the *air above* a source,
                // not its water voxel. This also spans vertical chunk edges.
                if ((fluidTopInChunk || fluidObjectSupportInChunk) &&
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
                        if (fluidTopInChunk && !decoration.IsAir)
                            chunk.SetBlock(
                                localX, (int)decorationY + 1 - originY,
                                localZ, decoration);
                        if (fluidObjectSupportInChunk &&
                            _decorations.HasObjectDecorations(topSample))
                        {
                            var objectId = _decorations.ObjectAt(
                                topSample, topBlock, worldX, worldZ,
                                supportY: (int)decorationY,
                                onFluidSurface: true,
                                fluidBelow: supportFluid.Fluid,
                                nearbyFluid: _nearbyFluidQuery);
                            if (objectId is not null)
                            {
                                pendingObjects ??= [];
                                pendingObjects.Add(new PendingGeneratedObject(
                                    localX, (int)decorationY - originY, localZ,
                                    objectId, BlockFace.Top,
                                    SupportFluid: supportFluid.Fluid));
                            }
                        }
                    }
                }
            }
        }

        MaterializeVolumeCavePalette(
            chunk, column, densityVolume, originX, originY, originZ);
        MaterializeExteriorFacePalette(
            chunk, column, densityVolume, originX, originY, originZ);

        MaterializeVolumeStructures(chunk, coord);

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
        // Give whole cave formations priority over soft vegetation.
        // Plants fill only the void cells left by spikes, including when
        // a formation starts in the adjacent vertical chunk.
        MaterializeCaveSpikes(
            chunk, column, densityVolume, originX, originY, originZ);
        MaterializeVerticalDecorations(
            chunk, column, densityVolume,
            originX, originY, originZ, ref pendingObjects);
        // Veins are placed before natural face coatings, so moss stays on top.
        _veins?.Apply(chunk, coord);
        MaterializeWallLayers(
            chunk, column, densityVolume, originX, originY, originZ,
            ref pendingObjects);
        MaterializeWallObjects(
            chunk, column, densityVolume, originX, originY, originZ);
        // The Sphere Shell and its opaque terrain lining are the final
        // worldgen boundary. Structures, fluids, spikes and layers must not
        // uncover or replace them.
        _voidSpawnPlatform?.Apply(chunk, coord);
        ConcealSphereShell(chunk, column, originY, topExclusive);
        MaterializeStructureObjects(chunk, coord);

        // The supporting block owns an Object, never its adjacent fluid cell.
        // Publish only after structures, caves, and the shell are final.
        if (pendingObjects is not null)
        {
            foreach (var obj in pendingObjects)
            {
                if ((_floorY is { } floor && originY + obj.Y <= floor) ||
                    (_roofY is { } roof && originY + obj.Y >= roof))
                    continue;
                var support = chunk.GetCell(obj.X, obj.Y, obj.Z);
                if (!obj.SupportFluid.IsNone)
                {
                    var fluid = chunk.GetFluid(obj.X, obj.Y, obj.Z);
                    if (!support.IsEmpty || fluid.Fluid != obj.SupportFluid ||
                        fluid.Level != FluidCell.MaxLevel || !fluid.IsSource)
                        continue;
                }
                else if (support.IsEmpty ||
                         !_blocks.GetDefinition(support.Block).IsCollidable)
                    continue;
                var target = obj.Target;
                if (Chunk.Contains(target.X, target.Y, target.Z))
                {
                    if (!chunk.GetCell(target.X, target.Y, target.Z).IsEmpty ||
                        (obj.Submerged &&
                         chunk.GetFluid(target.X, target.Y, target.Z).IsEmpty))
                        continue;
                }
                chunk.AddObject(obj.X, obj.Y, obj.Z,
                    new ObjectCell(obj.Id, obj.Face));
            }
        }

        return chunk;
    }

    /// <summary>
    /// Finalize authored Structure Objects on their actual supporting voxels.
    /// This pass observes final structures/caves/shell, and never replaces blocks
    /// or fluid occupying the adjacent Object cell.
    /// </summary>
    private void MaterializeStructureObjects(Chunk chunk, ChunkCoord coord)
    {
        if (!_structures.HasRules) return;
        var origin = VoxelCoordinates.ChunkOrigin(coord);
        foreach (var placement in _structures.PlacementsForChunk(coord.X, coord.Z))
        foreach (var obj in placement.Objects)
        {
            var support = obj.SupportPosition;
            var x = (long)support.X - origin.X;
            var y = (long)support.Y - origin.Y;
            var z = (long)support.Z - origin.Z;
            if (x < 0 || x >= Chunk.Size ||
                y < 0 || y >= Chunk.Size ||
                z < 0 || z >= Chunk.Size)
                continue;

            var sx = (int)x;
            var sy = (int)y;
            var sz = (int)z;
            var cell = chunk.GetCell(sx, sy, sz);
            if (cell.IsEmpty || !_blocks.GetDefinition(cell.Block).IsCollidable)
                continue;

            var targetX = (long)obj.X - origin.X;
            var targetY = (long)obj.Y - origin.Y;
            var targetZ = (long)obj.Z - origin.Z;
            if (targetX >= 0 && targetX < Chunk.Size &&
                targetY >= 0 && targetY < Chunk.Size &&
                targetZ >= 0 && targetZ < Chunk.Size &&
                !chunk.GetCell((int)targetX, (int)targetY, (int)targetZ).IsEmpty)
                continue;

            chunk.AddObject(sx, sy, sz, obj.Cell);
        }
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
                _terrain.DensityAt(
                    biome,
                    baseY,
                    (int)px,
                    supportY,
                    (int)pz,
                    neighbor.VolumeBiomes[local.X, local.Z]) < 0d)
                return true;
        }
        return false;
    }

    private double DensityAtWorld(
        int worldX,
        int worldY,
        int worldZ)
    {
        var (coord, local) =
            VoxelCoordinates.FromWorld(worldX, 0, worldZ);
        var column = _columns.Get(coord.X, coord.Z);
        var biome = column.BiomeAt(local.X, local.Z);
        return _terrain.DensityAt(
            biome,
            column.BaseHeightAt(local.X, local.Z),
            worldX,
            worldY,
            worldZ,
            column.VolumeBiomes[local.X, local.Z]);
    }

    private bool IsCaveVoidAtWorld(
        int worldX,
        int worldY,
        int worldZ)
    {
        if (worldY < 0)
            throw new ArgumentOutOfRangeException(nameof(worldY));

        var (coord, local) =
            VoxelCoordinates.FromWorld(worldX, 0, worldZ);
        var column = _columns.Get(coord.X, coord.Z);
        return _terrain.IsCaveVoidAt(
            column.BaseHeightAt(local.X, local.Z),
            new WorldVoxelCoord(worldX, worldY, worldZ));
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
            var isSolid =
                localAbove < densityVolume.Height
                    ? densityVolume.IsSolidAt(
                        localX,
                        (int)localAbove,
                        localZ)
                    : _terrain.DensityAt(
                        surfaceBiome,
                        baseY,
                        worldX,
                        (int)above,
                        worldZ,
                        volumeBiome) >= 0d;

            if (!isSolid)
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
                    !densityVolume.IsSolidAtUnchecked(x, y, z))
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
                        return !densityVolume.IsSolidAtUnchecked(nx, ny, nz) &&
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
                        biome = column.CaveBiomeAt(x, z);
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
                    !densityVolume.IsSolidAtUnchecked(x, y, z))
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
                        ? !densityVolume.IsSolidAtUnchecked(nx, ny, nz)
                        : DensityAtWorld(
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
                        : IsCaveVoidAtWorld(
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
        int originZ,
        ref List<PendingGeneratedObject>? pendingObjects)
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

            // Include adjacent vertical seam targets: an Object is stored on
            // its host chunk, even when the visible sprite crosses a seam.
            for (var localY = -1; localY <= Chunk.Size; localY++)
            {
                var worldY = originY + localY;
                if (worldY <= 0 ||
                    (_floorY is { } floorY && worldY <= floorY) ||
                    (_roofY is { } roofY && worldY >= roofY))
                    continue;

                var volumePlacement = densityVolume.VolumePlacementAt(
                    localX, localZ);
                var targetInChunk = (uint)localY < Chunk.Size;
                var targetSolid = targetInChunk
                    ? densityVolume.IsSolidAtUnchecked(localX, localY, localZ)
                    : _terrain.DensityAt(surfaceBiome, baseY,
                        worldX, worldY, worldZ, volumePlacement) >= 0d;
                if (targetSolid ||
                    (targetInChunk &&
                     !chunk.GetCell(localX, localY, localZ).IsEmpty))
                    continue;
                var belowSolid = localY > 0 && localY <= Chunk.Size
                    ? densityVolume.IsSolidAtUnchecked(localX, localY - 1, localZ)
                    : _terrain.DensityAt(
                        surfaceBiome, baseY,
                        worldX, worldY - 1, worldZ, volumePlacement) >= 0d;
                var isFloor = belowSolid;
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
                    var aboveSolid = localY + 1 >= 0 && localY + 1 < Chunk.Size
                        ? densityVolume.IsSolidAtUnchecked(localX, localY + 1, localZ)
                        : _terrain.DensityAt(
                            surfaceBiome, baseY,
                            worldX, worldY + 1, worldZ, volumePlacement) >= 0d;
                    isCeiling = aboveSolid;
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
                    underground ??= column.CaveBiomeAt(localX, localZ);
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
                    targetSolid).IsEmpty;
                var placement = new SurfacePlacementContext(worldY, 0);

                if (isFloor)
                {
                    var support = localY > 0 && localY <= Chunk.Size
                        ? chunk.GetBlock(localX, localY - 1, localZ)
                        : worldY > baseY
                            ? (volumeMaterials ??= _materials.SampleColumn(
                                decoratorBiome, worldX, worldZ)).BlockAt(0)
                            : _materials.VolumeBlockAt(
                                decoratorBiome, worldX, worldY - 1, worldZ);
                    if (!support.IsAir)
                    {
                        if (targetInChunk)
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
                        if ((uint)(localY - 1) < Chunk.Size)
                        {
                            var objectId = _decorations.ObjectAt(
                                decoratorBiome, support, worldX, worldZ,
                                placement, verticalY: worldY,
                                submerged: submerged, supportY: worldY - 1);
                            if (objectId is not null)
                            {
                                pendingObjects ??= [];
                                pendingObjects.Add(new PendingGeneratedObject(
                                    localX, localY - 1, localZ,
                                    objectId, BlockFace.Top, submerged));
                            }
                        }
                    }
                }

                if (!isCeiling ||
                    !_decorations.HasCeilingDecorationsFor(
                        decoratorBiome.Primary))
                    continue;

                BlockRuntimeId ceilingSupport;
                if (localY + 1 >= 0 && localY + 1 < Chunk.Size)
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

                if (targetInChunk)
                {
                    var hanger = _decorations.BlockAt(
                        decoratorBiome, ceilingSupport, worldX, worldZ,
                        placement, verticalY: worldY, submerged: submerged,
                        supportSurface: DecorationSupportSurface.Ceiling);
                    if (!hanger.IsAir)
                        chunk.SetBlock(localX, localY, localZ, hanger);
                }
                if ((uint)(localY + 1) < Chunk.Size)
                {
                    var objectId = _decorations.ObjectAt(
                        decoratorBiome, ceilingSupport, worldX, worldZ,
                        placement, verticalY: worldY,
                        submerged: submerged, supportY: worldY + 1,
                        supportSurface: DecorationSupportSurface.Ceiling);
                    if (objectId is not null)
                    {
                        pendingObjects ??= [];
                        pendingObjects.Add(new PendingGeneratedObject(
                            localX, localY + 1, localZ,
                            objectId, BlockFace.Bottom, submerged));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies wall-attached objects only to real density-exposed side faces.
    /// Face selection is stable in world space, and adjacent chunk columns
    /// are sampled from the same immutable terrain/material owners.
    /// This never constructs a second world or fluid representation.
    /// </summary>

    /// <summary>
    /// Procedural wall overlays belong to their solid host face. Neighbor
    /// density is immutable even across chunk borders; never write into a
    /// neighbor chunk or create a second wall decoration block.
    /// </summary>
    private void MaterializeWallLayers(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX,
        int originY,
        int originZ,
        ref List<PendingGeneratedObject>? pendingObjects)
    {
        if (!_decorations.HasWallLayers && !_decorations.HasWallObjects)
            return;

        ReadOnlySpan<(int Dx, int Dz, BlockFace Face)> faces =
        [
            (1, 0, BlockFace.Right),
            (-1, 0, BlockFace.Left),
            (0, 1, BlockFace.Front),
            (0, -1, BlockFace.Back),
        ];

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var worldX = originX + x;
            var worldZ = originZ + z;
            var surface = column.BiomeAt(x, z);
            var baseY = column.BaseHeightAt(x, z);
            var surfaceSupportsLayers =
                _decorations.HasWallLayersFor(surface.Primary) ||
                _decorations.HasWallObjectsFor(surface.Primary);
            if (!surfaceSupportsLayers &&
                _volumeBiomes is not { HasBiomes: true })
                continue;

            // No exterior or cave face below this column can occur in
            // sky-only vertical chunks.
            if (originY > baseY)
                continue;

            var upper = Math.Min(Chunk.Size, baseY - originY + 1);
            for (var y = 0; y < upper; y++)
            {
                var worldY = originY + y;
                if (worldY <= 0 ||
                    (_floorY is { } floor && worldY <= floor) ||
                    (_roofY is { } roof && worldY >= roof))
                    continue;

                var host = chunk.GetCell(x, y, z);
                if (host.IsEmpty ||
                    !densityVolume.IsSolidAtUnchecked(x, y, z))
                    continue;

                foreach (var side in faces)
                {
                    var nx = x + side.Dx;
                    var nz = z + side.Dz;
                    var px = worldX + side.Dx;
                    var pz = worldZ + side.Dz;
                    var local = Chunk.Contains(nx, y, nz);

                    if (local)
                    {
                        if (!chunk.GetCell(nx, y, nz).IsEmpty ||
                            densityVolume.IsSolidAtUnchecked(nx, y, nz))
                            continue;
                    }
                    else if (DensityAtWorld(px, worldY, pz) >= 0d)
                        continue;

                    // Resolve the actual empty-side biome without changing
                    // its chunk. For cave walls, the carved void owns the
                    // attached layer; for exterior cliffs, the surface does.
                    var neighborColumn = local ? column :
                        _columns.Get(
                            (int)Math.Floor((double)px / Chunk.Size),
                            (int)Math.Floor((double)pz / Chunk.Size));
                    var sx = ((nx % Chunk.Size) + Chunk.Size) % Chunk.Size;
                    var sz = ((nz % Chunk.Size) + Chunk.Size) % Chunk.Size;
                    var neighborBaseY =
                        neighborColumn.BaseHeightAt(sx, sz);
                    BiomeSample? owner;
                    if (worldY <= neighborBaseY &&
                        _terrain.IsCaveVoidAt(neighborBaseY,
                            new WorldVoxelCoord(px, worldY, pz)))
                    {
                        owner = neighborColumn.CaveBiomeAt(sx, sz);
                    }
                    else if (surfaceSupportsLayers &&
                             worldY > neighborBaseY &&
                             neighborColumn.BiomeAt(sx, sz).Primary ==
                                 surface.Primary)
                    {
                        owner = surface;
                    }
                    else
                    {
                        continue;
                    }

                    if (owner is null ||
                        (!_decorations.HasWallLayersFor(owner.Primary) &&
                         !_decorations.HasWallObjectsFor(owner.Primary)))
                        continue;

                    if (_decorations.HasWallLayersFor(owner.Primary))
                    {
                        var layerId = _decorations.WallLayerAt(
                            owner, host.Block, worldX, worldY, worldZ);
                        if (layerId is not null)
                        {
                            var previous = chunk.GetSurfaceState(x, y, z);
                            if (previous.TryAttach(
                                new AttachedBlockLayer(side.Face, layerId),
                                out var updated))
                                chunk.SetSurfaceState(x, y, z, updated);
                        }
                    }
                    if (_decorations.HasWallObjectsFor(owner.Primary))
                    {
                        var objectId = _decorations.WallObjectAt(
                            owner, host.Block, worldX, worldY, worldZ);
                        if (objectId is not null)
                        {
                            pendingObjects ??= [];
                            pendingObjects.Add(new PendingGeneratedObject(
                                x, y, z, objectId, side.Face));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Renders physical wall plants as actual objects in the adjacent air
    /// voxel. Flat coatings are owned separately by MaterializeWallLayers on
    /// the solid host's BlockSurfaceState, never as projected block objects.
    /// Neighboring chunks are queried without mutating their cells.
    /// </summary>
    private void MaterializeWallObjects(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX,
        int originY,
        int originZ)
    {
        if (!_decorations.HasWallBlocks)
            return;

        // Outward normals point away from solid backing support.
        ReadOnlySpan<(int Dx, int Dz, HorizontalFacing Facing)> supports =
        [
            (0, -1, HorizontalFacing.South),
            (1, 0, HorizontalFacing.West),
            (0, 1, HorizontalFacing.North),
            (-1, 0, HorizontalFacing.East),
        ];

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var worldX = originX + x;
            var worldZ = originZ + z;
            var surface = column.BiomeAt(x, z);
            var baseY = column.BaseHeightAt(x, z);
            var surfaceSupportsObjects = _decorations.HasWallBlocksFor(surface.Primary);
            if (!surfaceSupportsObjects &&
                _volumeBiomes is not { HasBiomes: true })
                continue;
            if (originY > baseY && !surfaceSupportsObjects)
                continue;

            for (var y = 0; y < Chunk.Size; y++)
            {
                var worldY = originY + y;
                if (worldY <= 0 ||
                    (_floorY is { } floor && worldY <= floor) ||
                    (_roofY is { } roof && worldY >= roof) ||
                    !chunk.GetCell(x, y, z).IsEmpty ||
                    !chunk.GetFluid(x, y, z).IsEmpty ||
                    densityVolume.IsSolidAtUnchecked(x, y, z))
                    continue;

                var exterior = worldY > baseY && surfaceSupportsObjects;
                var cave = worldY <= baseY &&
                    _volumeBiomes is { HasBiomes: true } &&
                    _terrain.IsCaveVoidAt(baseY,
                        new WorldVoxelCoord(worldX, worldY, worldZ));
                if (!exterior && !cave)
                    continue;
                var owner = exterior
                    ? surface
                    : column.CaveBiomeAt(x, z);
                if (owner is null ||
                    !_decorations.HasWallBlocksFor(owner.Primary))
                    continue;

                foreach (var side in supports)
                {
                    var nx = x + side.Dx;
                    var nz = z + side.Dz;
                    var neighborX = worldX + side.Dx;
                    var neighborZ = worldZ + side.Dz;
                    var local = Chunk.Contains(nx, y, nz);
                    var solid = local
                        ? densityVolume.IsSolidAtUnchecked(nx, y, nz)
                        : DensityAtWorld(neighborX, worldY, neighborZ) >= 0d;
                    if (!solid)
                        continue;

                    BlockRuntimeId backingBlock;
                    if (exterior)
                    {
                        var (neighborCoord, neighborLocal) =
                            VoxelCoordinates.FromWorld(neighborX, 0, neighborZ);
                        var supportingColumn = local
                            ? column
                            : _columns.Get(neighborCoord.X, neighborCoord.Z);
                        var neighborBiome = supportingColumn.BiomeAt(
                            neighborLocal.X, neighborLocal.Z);
                        var neighborBaseY = supportingColumn.BaseHeightAt(
                            neighborLocal.X, neighborLocal.Z);
                        if (neighborBiome.Primary != owner.Primary ||
                            worldY > neighborBaseY)
                            continue;
                        backingBlock = local
                            ? chunk.GetBlock(nx, y, nz)
                            : _materials.BlockAt(
                                neighborBiome, neighborX, neighborZ,
                                (uint)(neighborBaseY - worldY));
                    }
                    else
                    {
                        backingBlock = local
                            ? chunk.GetBlock(nx, y, nz)
                            : _materials.VolumeBlockAt(
                                owner, neighborX, worldY, neighborZ,
                                BiomePaletteFace.Walls);
                    }
                    if (backingBlock.IsAir)
                        continue;

                    var block = _decorations.WallBlockAt(
                        owner, backingBlock, worldX, worldY, worldZ);
                    if (block.IsAir)
                        continue;

                    chunk.SetCell(x, y, z,
                        new VoxelCell(block, facing: side.Facing));
                    break;
                }
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
                    !densityVolume.IsSolidAtUnchecked(x, localY, z))
                {
                    containsVoid = true;
                    break;
                }
            }
            if (!containsVoid)
                continue;

            var worldX = originX + x;
            var worldZ = originZ + z;
            var biome = column.CaveBiomeAt(x, z);
            if (biome is null || !spikes.HasRulesFor(biome.Primary))
                continue;

            var surfaceBiome = column.BiomeAt(x, z);
            var volumeBiome = densityVolume.VolumePlacementAt(x, z);
            for (var anchorY = first; anchorY <= last; anchorY++)
            {
                // TrySample rejects a non-void anchor before producing any
                // placement: IsVoid(anchorY) is its first density check.
                // When the anchor is inside this chunk, reuse the already
                // sampled density and skip both direction probes immediately.
                var localAnchorY = anchorY - originY;
                if ((uint)localAnchorY < Chunk.Size &&
                    densityVolume.IsSolidAtUnchecked(x, localAnchorY, z))
                    continue;

                for (var direction = 0; direction < 2; direction++)
                {
                    var down = direction == 1;
                    if (!spikes.TrySample(
                            biome.Primary, surfaceBiome, volumeBiome, baseY,
                            worldX, anchorY, worldZ, down,
                            densityVolume, originY, x, z,
                            out var placement))
                        continue;

                    // A blocked root or intermediate tier invalidates
                    // this formation in the current chunk. Never emit the
                    // remaining tiers as a floating, disconnected spike.
                    var blocked = false;
                    for (var segment = 0; segment < placement.Height; segment++)
                    {
                        var worldY = anchorY + (down ? -segment : segment);
                        var localY = worldY - originY;
                        if ((uint)localY >= Chunk.Size)
                            continue;
                        if (!chunk.GetCell(x, localY, z).IsEmpty ||
                            !chunk.GetFluid(x, localY, z).IsEmpty)
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked)
                        continue;

                    for (var segment = 0; segment < placement.Height; segment++)
                    {
                        var worldY = anchorY + (down ? -segment : segment);
                        var localY = worldY - originY;
                        if ((uint)localY >= Chunk.Size)
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

    private bool HasGeneratedFluidAboveSurface(
        BiomeSample fluidBiome,
        BiomeSample surfaceBiome,
        int baseY,
        int surfaceFluidCutDepth,
        int worldX,
        int surfaceY,
        int worldZ,
        BiomeSample? volumePlacement)
    {
        var aboveY = (long)surfaceY + 1L;
        if (aboveY < 0 || aboveY > int.MaxValue)
            return false;

        var y = (int)aboveY;
        // The column and density volume already carry the authoritative
        // surface biome, base height and volume placement for this X/Z.
        // Reuse them instead of constructing new scalar-sample caches.
        var density = _terrain.DensityAt(
            surfaceBiome, baseY, worldX, y, worldZ, volumePlacement);
        var fluid = _generatedFluids.FluidAt(
            fluidBiome, baseY, surfaceFluidCutDepth,
            worldX, y, worldZ, density);
        return !fluid.IsEmpty;
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
            return;

        // Resolve each column's fluid interval once. The subsequent pass walks
        // the chunk in storage order (X, then Z, then Y), preserving bounded
        // vertical work while making palette writes contiguous.
        Span<int> firstFluidYByColumn = stackalloc int[Chunk.Area];
        Span<int> lastFluidYByColumn = stackalloc int[Chunk.Area];
        firstFluidYByColumn.Fill(int.MaxValue);
        lastFluidYByColumn.Fill(int.MinValue);

        var firstWorldY = int.MaxValue;
        var lastWorldY = int.MinValue;
        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var sample = column.BiomeAt(localX, localZ);
                var baseY = column.BaseHeightAt(localX, localZ);
                var surfaceCutDepth =
                    column.SurfaceFluidCutDepthAt(localX, localZ);
                var worldX = checked(originX + localX);
                var worldZ = checked(originZ + localZ);

                if (!_generatedFluids.TryGetColumnBounds(
                        sample,
                        baseY,
                        surfaceCutDepth,
                        worldX,
                        worldZ,
                        out var minimumY,
                        out var maximumY))
                    continue;

                var firstY = Math.Max(originY, minimumY);
                var lastY = Math.Min(topExclusive - 1, maximumY);
                if (firstY > lastY)
                    continue;

                var columnIndex = localX + Chunk.Size * localZ;
                firstFluidYByColumn[columnIndex] = firstY;
                lastFluidYByColumn[columnIndex] = lastY;
                firstWorldY = Math.Min(firstWorldY, firstY);
                lastWorldY = Math.Max(lastWorldY, lastY);
            }
        }

        if (firstWorldY > lastWorldY)
            return;

        for (var worldY = firstWorldY; worldY <= lastWorldY; worldY++)
        {
            var localY = worldY - originY;
            for (var localZ = 0; localZ < Chunk.Size; localZ++)
            {
                for (var localX = 0; localX < Chunk.Size; localX++)
                {
                    var columnIndex = localX + Chunk.Size * localZ;
                    if (worldY < firstFluidYByColumn[columnIndex] ||
                        worldY > lastFluidYByColumn[columnIndex])
                        continue;

                    var block = chunk.GetBlock(localX, localY, localZ);
                    if ((!block.IsAir &&
                         !FluidBlockOccupancy.AllowsFluid(
                             _blocks.GetDefinition(block))) ||
                        !chunk.GetFluid(localX, localY, localZ).IsEmpty)
                        continue;

                    var sample = column.BiomeAt(localX, localZ);
                    var baseY = column.BaseHeightAt(localX, localZ);
                    var surfaceCutDepth =
                        column.SurfaceFluidCutDepthAt(localX, localZ);
                    var worldX = checked(originX + localX);
                    var worldZ = checked(originZ + localZ);
                    var fluid = _generatedFluids.FluidAt(
                        sample,
                        baseY,
                        surfaceCutDepth,
                        worldX,
                        worldY,
                        worldZ,
                        densityVolume.IsSolidAtUnchecked(
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

    private void ConcealSphereShell(
        Chunk chunk,
        SurfaceTerrainColumn column,
        int originY,
        int topExclusive)
    {
        if (_shellBlock.IsAir)
            return;

        var floorLiningY = _floorY is { } floor
            ? (long)floor + 1L : -1L;
        var roofLiningY = _roofY is { } roof
            ? (long)roof - 1L : -1L;
        bool IsInChunk(long y) => y >= originY && y < topExclusive;
        if (!IsInChunk(_floorY ?? -1) &&
            !IsInChunk(_roofY ?? -1) &&
            !IsInChunk(floorLiningY) &&
            !IsInChunk(roofLiningY))
            return;

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var coreBlock = _materials.CoreLayer(column.BiomeAt(x, z)).Block;

            // Seal the playable face with its biome's deep terrain material.
            SealShellLining(chunk, x, z, originY, topExclusive,
                floorLiningY, coreBlock);
            SealShellLining(chunk, x, z, originY, topExclusive,
                roofLiningY, coreBlock);

            // Apply the unbreakable boundary last, after every worldgen pass.
            if (_floorY is { } floorY)
                SetBoundaryBlock(chunk, x, z, originY, topExclusive,
                    floorY, _shellBlock);
            if (_roofY is { } roofY)
                SetBoundaryBlock(chunk, x, z, originY, topExclusive,
                    roofY, _shellBlock);
        }
    }

    private void SealShellLining(
        Chunk chunk,
        int x,
        int z,
        int originY,
        int topExclusive,
        long worldY,
        BlockRuntimeId coreBlock)
    {
        if (worldY < originY || worldY >= topExclusive)
            return;

        var localY = (int)(worldY - originY);
        var current = chunk.GetBlock(x, localY, z);
        if (!current.IsAir)
        {
            var definition = _blocks.GetDefinition(current);
            if (definition.IsOpaque &&
                definition.Shape.Kind == BlockShapeKind.Cube &&
                !chunk.GetCell(x, localY, z).HasMicroblockGeometry)
            {
                // Preserve the biome's legitimate surface if it already
                // conceals the shell; only repair cleared/partial cells.
                chunk.SetFluid(x, localY, z, FluidCell.Empty);
                return;
            }
        }

        SetBoundaryBlock(chunk, x, z, originY, topExclusive,
            worldY, coreBlock);
    }

    private static void SetBoundaryBlock(
        Chunk chunk,
        int x,
        int z,
        int originY,
        int topExclusive,
        long worldY,
        BlockRuntimeId block)
    {
        if (worldY < originY || worldY >= topExclusive)
            return;

        var localY = (int)(worldY - originY);
        chunk.SetBlock(x, localY, z, block);
        chunk.SetFluid(x, localY, z, FluidCell.Empty);
        chunk.SetSurfaceState(x, localY, z, BlockSurfaceState.Empty);
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
