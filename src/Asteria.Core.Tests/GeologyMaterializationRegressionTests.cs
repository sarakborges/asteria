using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GeologyMaterializationRegressionTests
{
    private const ulong Seed = 0xA57E_3D10_2026UL;

    [Fact]
    public void MaterializedChunksMatchWorldSpaceGeologyAcrossChunkSeamsAndGenerationOrder()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome();
        var dimension = CreateDimension(biome.Id);
        var biomes = new BiomeRegistry([biome]);
        var firstGenerator = new BiomeWorldGenerator(Seed, dimension, blocks, biomes);
        var secondGenerator = new BiomeWorldGenerator(Seed, dimension, blocks, biomes);
        var materials = new BiomeSurfaceMaterialField(Seed, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);

        // Materialize in opposite orders so cache and generation order cannot
        // influence voxels at either the horizontal or vertical chunk seam.
        var firstUpperRight = firstGenerator.Materialize(new ChunkCoord(1, 2, 0));
        var firstUpperLeft = firstGenerator.Materialize(new ChunkCoord(0, 2, 0));
        var firstLowerRight = firstGenerator.Materialize(new ChunkCoord(1, 1, 0));
        var firstLowerLeft = firstGenerator.Materialize(new ChunkCoord(0, 1, 0));
        var secondLowerLeft = secondGenerator.Materialize(new ChunkCoord(0, 1, 0));
        var secondLowerRight = secondGenerator.Materialize(new ChunkCoord(1, 1, 0));
        var secondUpperLeft = secondGenerator.Materialize(new ChunkCoord(0, 2, 0));
        var secondUpperRight = secondGenerator.Materialize(new ChunkCoord(1, 2, 0));

        var grass = blocks.GetId("asteria:grass");
        var dirt = blocks.GetId("asteria:dirt");
        var cover = Assert.IsType<BiomeGeologyDefinition>(biome.Geology)
            .SurfaceCoverDepth;

        for (var x = 0; x < Chunk.Size * 2; x++)
        for (var z = 0; z < Chunk.Size; z++)
        for (var worldY = 16; worldY <= 40; worldY++)
        {
            var coord = new ChunkCoord(x / Chunk.Size, worldY / Chunk.Size, 0);
            var localX = x % Chunk.Size;
            var localY = worldY % Chunk.Size;
            var actualFirst = (x / Chunk.Size, coord.Y) switch
            {
                (0, 1) => firstLowerLeft.GetBlock(localX, localY, z),
                (1, 1) => firstLowerRight.GetBlock(localX, localY, z),
                (0, 2) => firstUpperLeft.GetBlock(localX, localY, z),
                (1, 2) => firstUpperRight.GetBlock(localX, localY, z),
                _ => throw new InvalidOperationException("Unexpected test chunk coordinate."),
            };
            var actualSecond = (x / Chunk.Size, coord.Y) switch
            {
                (0, 1) => secondLowerLeft.GetBlock(localX, localY, z),
                (1, 1) => secondLowerRight.GetBlock(localX, localY, z),
                (0, 2) => secondUpperLeft.GetBlock(localX, localY, z),
                (1, 2) => secondUpperRight.GetBlock(localX, localY, z),
                _ => throw new InvalidOperationException("Unexpected test chunk coordinate."),
            };

            Assert.Equal(actualFirst, actualSecond);

            var depth = checked((uint)(40 - worldY));
            if (depth == 0)
            {
                Assert.Equal(grass, actualFirst);
            }
            else if (depth < cover)
            {
                Assert.Equal(dirt, actualFirst);
            }
            else
            {
                Assert.True(materials.TryGeologyBlockAt(
                    sample, x, worldY, z, out var expected));
                Assert.Equal(expected, actualFirst);
            }
        }

        // Explicitly exercise each voxel along both shared faces.
        for (var worldY = 16; worldY <= 40; worldY++)
        for (var z = 0; z < Chunk.Size; z++)
        {
            var chunkY = worldY / Chunk.Size;
            var leftFirst = chunkY == 1
                ? firstLowerLeft.GetBlock(Chunk.Size - 1, worldY % Chunk.Size, z)
                : firstUpperLeft.GetBlock(Chunk.Size - 1, worldY % Chunk.Size, z);
            var rightFirst = chunkY == 1
                ? firstLowerRight.GetBlock(0, worldY % Chunk.Size, z)
                : firstUpperRight.GetBlock(0, worldY % Chunk.Size, z);
            var leftSecond = chunkY == 1
                ? secondLowerLeft.GetBlock(Chunk.Size - 1, worldY % Chunk.Size, z)
                : secondUpperLeft.GetBlock(Chunk.Size - 1, worldY % Chunk.Size, z);
            var rightSecond = chunkY == 1
                ? secondLowerRight.GetBlock(0, worldY % Chunk.Size, z)
                : secondUpperRight.GetBlock(0, worldY % Chunk.Size, z);
            Assert.Equal(leftFirst, leftSecond);
            Assert.Equal(rightFirst, rightSecond);
            Assert.True(materials.TryGeologyBlockAt(
                sample, Chunk.Size - 1, worldY, z, out var expectedLeft));
            Assert.True(materials.TryGeologyBlockAt(
                sample, Chunk.Size, worldY, z, out var expectedRight));
            if (40 - worldY >= cover)
            {
                Assert.Equal(expectedLeft, leftFirst);
                Assert.Equal(expectedRight, rightFirst);
            }
        }
    }

    [Fact]
    public void FacePaletteAirMaterialsDisableSupportOccupancyFastPath()
    {
        var blocks = CreateBlocks();
        var biome = BiomeDefinitionJson.Parse(
            """
            {
              "id": "asteria:test/air_palette",
              "surfaceLayout": {
                "regionSize": { "min": 128, "max": 128 }
              },
              "surfaceTerrain": {
                "baseHeightOffset": 40,
                "macroAmplitude": 0,
                "macroScale": 64,
                "detailAmplitude": 0,
                "detailScale": 32
              },
              "palette": {
                "default": [
                  { "block": "asteria:air" }
                ]
              }
            }
            """);

        var materials = new BiomeSurfaceMaterialField(
            Seed, [biome], blocks);

        Assert.False(materials.FacePalettesPreserveOccupancy);
    }

    [Theory]
    [InlineData(true, 40, 20, false, true)]
    [InlineData(true, 40, 40, false, true)]
    [InlineData(true, 40, 41, false, false)]
    [InlineData(true, 40, 0, false, false)]
    [InlineData(true, 40, 20, true, false)]
    [InlineData(false, 40, 20, false, false)]
    public void LocalDensityMaskRequiresCaveBiomeOwnership(
        bool hasCaveBiome,
        int baseY,
        int worldY,
        bool isSphereShellBoundaryOrLining,
        bool expected)
    {
        Assert.Equal(
            expected,
            SurfaceChunkMaterializer.IsLocalDensityCaveVoid(
                hasCaveBiome, baseY, worldY,
                isSphereShellBoundaryOrLining));
    }

    [Fact]
    public void BiomeRegionOwnershipUsesVolumeIdentityBeforeSurfaceFallback()
    {
        static BiomeSample Sample(string id) =>
            new(id, [new BiomeInfluence(id, 1f)]);

        var surface = Sample("asteria:test/surface");
        var carvedVolume = Sample("asteria:test/carved_volume");
        var additiveVolume = Sample("asteria:test/additive_volume");

        var carvedRegion = SurfaceTerrainField.ResolveRegionOwner(
            surface, null, carvedVolume,
            baseY: 40, surfaceFluidCutDepth: 0, worldY: 20,
            isEmpty: true, isSphereShellBoundaryOrLining: false);
        Assert.Equal(BiomeRegionOwnerKind.Volume, carvedRegion.Kind);
        Assert.Equal(carvedVolume.Primary, carvedRegion.Biome.Primary);

        var additiveSolid = SurfaceTerrainField.ResolveRegionOwner(
            surface, additiveVolume, null,
            baseY: 40, surfaceFluidCutDepth: 0, worldY: 220,
            isEmpty: false, isSphereShellBoundaryOrLining: false);
        Assert.Equal(BiomeRegionOwnerKind.Volume, additiveSolid.Kind);
        Assert.Equal(additiveVolume.Primary, additiveSolid.Biome.Primary);

        // An additive formation does not own an empty voxel merely because
        // the voxel's Y lies inside its authored vertical interval.
        var additiveEmpty = SurfaceTerrainField.ResolveRegionOwner(
            surface, additiveVolume, null,
            baseY: 40, surfaceFluidCutDepth: 0, worldY: 220,
            isEmpty: true, isSphereShellBoundaryOrLining: false);
        Assert.Equal(BiomeRegionOwnerKind.Surface, additiveEmpty.Kind);
        Assert.Equal(surface.Primary, additiveEmpty.Biome.Primary);

        // Empty cells removed by a surface-fluid cut are not owned by a
        // carved-volume biome, even if one is selected for that column.
        var surfaceCut = SurfaceTerrainField.ResolveRegionOwner(
            surface, null, carvedVolume,
            baseY: 40, surfaceFluidCutDepth: 4, worldY: 38,
            isEmpty: true, isSphereShellBoundaryOrLining: false);
        Assert.Equal(BiomeRegionOwnerKind.Surface, surfaceCut.Kind);
        Assert.Equal(surface.Primary, surfaceCut.Biome.Primary);

        var shellLining = SurfaceTerrainField.ResolveRegionOwner(
            surface, null, carvedVolume,
            baseY: 40, surfaceFluidCutDepth: 0, worldY: 20,
            isEmpty: true, isSphereShellBoundaryOrLining: true);
        Assert.Equal(BiomeRegionOwnerKind.Surface, shellLining.Kind);

        var solidCell = SurfaceTerrainField.ResolveRegionOwner(
            surface, null, carvedVolume,
            baseY: 40, surfaceFluidCutDepth: 0, worldY: 20,
            isEmpty: false, isSphereShellBoundaryOrLining: false);
        Assert.Equal(BiomeRegionOwnerKind.Surface, solidCell.Kind);
    }

    [Fact]
    public void DirectionalPaletteDispatchUsesBiomeMaterialCapabilities()
    {
        var blocks = CreateBlocks();
        var surface = BiomeDefinitionJson.Parse(
            """
            {
              "id": "asteria:test/surface_owner",
              "surfaceLayout": {
                "regionSize": { "min": 128, "max": 128 }
              },
              "surfaceTerrain": {
                "baseHeightOffset": 40,
                "macroAmplitude": 0,
                "macroScale": 64,
                "detailAmplitude": 0,
                "detailScale": 32
              },
              "palette": {
                "default": [{ "block": "asteria:stone" }],
                "walls": [{ "block": "asteria:clay" }]
              }
            }
            """);
        var emptyRegionVolume = BiomeDefinitionJson.Parse(
            """
            {
              "id": "asteria:test/empty_region_owner",
              "volumeLayout": { "placement": "carvedVoid" },
              "palette": {
                "default": [{ "block": "asteria:stone" }]
              }
            }
            """);
        var additiveVolume = BiomeDefinitionJson.Parse(
            """
            {
              "id": "asteria:test/additive_geology_owner",
              "volumeLayout": { "weight": 0.25 },
              "terrain3d": {
                "additive": [{
                  "minY": 200,
                  "maxY": 280,
                  "horizontalScale": 112,
                  "detailScale": 40,
                  "coverage": 0.55,
                  "roughness": 0.18,
                  "densityScale": 28
                }]
              },
              "geology": {
                "baseBlock": "asteria:stone",
                "blocks": ["asteria:dirt", "asteria:gravel"],
                "scale": 24,
                "verticalScale": 6,
                "coverage": 0.78
              },
              "palette": {
                "default": [{ "block": "asteria:stone" }],
                "walls": [{ "block": "asteria:clay" }]
              }
            }
            """);
        var materials = new BiomeSurfaceMaterialField(
            Seed, [surface, emptyRegionVolume, additiveVolume], blocks);
        var surfaceOwner = new BiomeRegionOwner(
            new BiomeSample(
                surface.Id, [new BiomeInfluence(surface.Id, 1f)]),
            BiomeRegionOwnerKind.Surface);
        var emptyRegionOwner = new BiomeRegionOwner(
            new BiomeSample(
                emptyRegionVolume.Id,
                [new BiomeInfluence(emptyRegionVolume.Id, 1f)]),
            BiomeRegionOwnerKind.Volume);
        var additiveOwner = new BiomeRegionOwner(
            new BiomeSample(
                additiveVolume.Id, [new BiomeInfluence(additiveVolume.Id, 1f)]),
            BiomeRegionOwnerKind.Volume);

        Assert.True(materials.ShouldApplyDirectionalFacePalette(
            surfaceOwner, BiomePaletteFace.Walls));
        Assert.False(materials.ShouldApplyDirectionalFacePalette(
            surfaceOwner, BiomePaletteFace.Ceiling));
        Assert.False(materials.ShouldApplyDirectionalFacePalette(
            surfaceOwner, BiomePaletteFace.Floor));

        // A volume biome without geology uses its default material palette
        // for every exposed orientation.
        Assert.True(materials.ShouldApplyDirectionalFacePalette(
            emptyRegionOwner, BiomePaletteFace.Floor));
        Assert.True(materials.ShouldApplyDirectionalFacePalette(
            emptyRegionOwner, BiomePaletteFace.Walls));
        Assert.True(materials.ShouldApplyDirectionalFacePalette(
            emptyRegionOwner, BiomePaletteFace.Ceiling));

        // A volume biome with geology keeps that 3D field unless it explicitly
        // authors a directional override.
        Assert.False(materials.ShouldApplyDirectionalFacePalette(
            additiveOwner, BiomePaletteFace.Floor));
        Assert.True(materials.ShouldApplyDirectionalFacePalette(
            additiveOwner, BiomePaletteFace.Walls));
        Assert.False(materials.ShouldApplyDirectionalFacePalette(
            additiveOwner, BiomePaletteFace.Ceiling));

        var surfaceMask = materials.DirectionalPaletteFor(surfaceOwner);
        Assert.Equal(BiomePaletteFaceMask.Walls, surfaceMask.Faces);
        var emptyRegionMask = materials.DirectionalPaletteFor(emptyRegionOwner);
        Assert.Equal(
            BiomePaletteFaceMask.Floor |
            BiomePaletteFaceMask.Walls |
            BiomePaletteFaceMask.Ceiling,
            emptyRegionMask.Faces);
        var additiveMask = materials.DirectionalPaletteFor(additiveOwner);
        Assert.Equal(BiomePaletteFaceMask.Walls, additiveMask.Faces);
        Assert.True(additiveMask.MaxDepth > 0);
    }

    [Fact]
    public void DirectionalPaletteDiagnosticsAreReproducibleForFixedSeedAndChunk()
    {
        var biome = BiomeDefinitionJson.Parse(
            """
            {
              "id": "asteria:test/directional_palette_diagnostics",
              "surfaceLayout": {
                "regionSize": { "min": 128, "max": 128 }
              },
              "surfaceTerrain": {
                "baseHeightOffset": 40,
                "macroAmplitude": 0,
                "macroScale": 64,
                "detailAmplitude": 0,
                "detailScale": 32
              },
              "palette": {
                "default": [{ "block": "asteria:stone" }],
                "walls": [{ "block": "asteria:clay" }]
              }
            }
            """);
        var blocks = CreateBlocks();
        var dimension = CreateDimension(biome.Id);
        var biomes = new BiomeRegistry([biome]);
        var firstGenerator = new BiomeWorldGenerator(
            Seed, dimension, blocks, biomes);
        var secondGenerator = new BiomeWorldGenerator(
            Seed, dimension, blocks, biomes);

        static DirectionalFacePaletteMetrics? Capture(
            BiomeWorldGenerator generator)
        {
            using var job = generator.BeginMaterialization(
                new ChunkCoord(0, 2, 0));
            while (!job.IsComplete)
                job.Advance();
            return job.DirectionalFacePaletteMetrics;
        }

        var first = Capture(firstGenerator);
        var second = Capture(secondGenerator);
        Assert.True(first.HasValue);
        Assert.True(second.HasValue);
        var firstMetrics = first.Value;
        var secondMetrics = second.Value;

#if DEBUG
        Assert.True(firstMetrics.HasTimingBreakdown);
#else
        Assert.False(firstMetrics.HasTimingBreakdown);
#endif
        Assert.Equal(Chunk.Area, firstMetrics.SurfaceColumns);
        Assert.True(firstMetrics.SolidVoxels > 0);
        Assert.True(firstMetrics.FaceProbes > 0);
        Assert.True(firstMetrics.DensityProbes > 0);
        Assert.True(firstMetrics.EmptyRegionEligibilityRejectedProbes > 0);
        Assert.True(firstMetrics.DensityProbes <= firstMetrics.FaceProbes);
        Assert.InRange(firstMetrics.PaletteWrites, 0, firstMetrics.FaceProbes);

        Assert.Equal(firstMetrics.SurfaceColumns, secondMetrics.SurfaceColumns);
        Assert.Equal(firstMetrics.SolidVoxels, secondMetrics.SolidVoxels);
        Assert.Equal(firstMetrics.FaceProbes, secondMetrics.FaceProbes);
        Assert.Equal(firstMetrics.NeighborColumnLookups, secondMetrics.NeighborColumnLookups);
        Assert.Equal(firstMetrics.SolidPaletteLookups, secondMetrics.SolidPaletteLookups);
        Assert.Equal(firstMetrics.EmptyRegionPaletteLookups, secondMetrics.EmptyRegionPaletteLookups);
        Assert.Equal(firstMetrics.DensityProbes, secondMetrics.DensityProbes);
        Assert.Equal(firstMetrics.BoundaryDensityProbes, secondMetrics.BoundaryDensityProbes);
        Assert.Equal(firstMetrics.EmptyRegionOwnerResolutions, secondMetrics.EmptyRegionOwnerResolutions);
        Assert.Equal(firstMetrics.MaskRejectedProbes, secondMetrics.MaskRejectedProbes);
        Assert.Equal(
            firstMetrics.EmptyRegionEligibilityRejectedProbes,
            secondMetrics.EmptyRegionEligibilityRejectedProbes);
        Assert.Equal(firstMetrics.PaletteRejectedProbes, secondMetrics.PaletteRejectedProbes);
        Assert.Equal(firstMetrics.DepthRejectedProbes, secondMetrics.DepthRejectedProbes);
        Assert.Equal(firstMetrics.PaletteWrites, secondMetrics.PaletteWrites);
    }

    [Fact]
    public void MaterializedGravityGeologyNeverLeavesUnsupportedCellsAcrossVerticalSeams()
    {
        var blocks = CreateBlocks(gravityBlocks: true);
        var biome = CreateBiome();
        var caves = new DimensionCaveDefinition(
        [
            new DimensionCaveLayerDefinition(
                minDepth: 8,
                maxDepth: 80,
                channels:
                [
                    new DimensionCaveNoiseChannelDefinition(56, 36),
                    new DimensionCaveNoiseChannelDefinition(56, 36),
                ],
                noiseHalfWidth: 1f,
                densityScale: 24f,
                boundaryFade: 6),
        ]);
        var dimension = CreateDimension(biome.Id, caves);
        var biomes = new BiomeRegistry([biome]);
        var firstGenerator = new BiomeWorldGenerator(
            Seed, dimension, blocks, biomes);
        var secondGenerator = new BiomeWorldGenerator(
            Seed, dimension, blocks, biomes);
        var materials = new BiomeSurfaceMaterialField(Seed, [biome], blocks);
        Assert.True(materials.FacePalettesPreserveOccupancy);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var gravityBlocks = new HashSet<BlockRuntimeId>
        {
            blocks.GetId("asteria:sand"),
            blocks.GetId("asteria:gravel"),
        };

        WorldVoxelCoord? unsupportedCandidate = null;
        WorldVoxelCoord? supportedCandidate = null;
        var seamHeights = new[] { Chunk.Size, Chunk.Size * 2 };

        // The failing case must be a genuine cave roof exactly at a vertical
        // chunk boundary: solid geology above a carved void below.
        for (var z = -128; z <= 128 && !unsupportedCandidate.HasValue; z += 2)
        for (var x = -128; x <= 128 && !unsupportedCandidate.HasValue; x += 2)
        foreach (var worldY in seamHeights)
        {
            if (firstGenerator.DensityAt(x, worldY, z) < 0d ||
                firstGenerator.DensityAt(x, worldY - 1, z) >= 0d ||
                !materials.TryGeologyBlockAt(
                    sample, x, worldY, z, out var material) ||
                !gravityBlocks.Contains(material))
                continue;

            unsupportedCandidate = new WorldVoxelCoord(x, worldY, z);
            break;
        }

        // The control case need only be a supported underground deposit; it
        // should not depend on a rare coincidence at the cave/chunk seam.
        for (var z = -128; z <= 128 && !supportedCandidate.HasValue; z += 2)
        for (var x = -128; x <= 128 && !supportedCandidate.HasValue; x += 2)
        for (var worldY = 4; worldY < 38; worldY++)
        {
            if (firstGenerator.DensityAt(x, worldY, z) < 0d ||
                firstGenerator.DensityAt(x, worldY - 1, z) < 0d ||
                !materials.TryGeologyBlockAt(
                    sample, x, worldY, z, out var material) ||
                !gravityBlocks.Contains(material))
                continue;

            supportedCandidate = new WorldVoxelCoord(x, worldY, z);
            break;
        }

        Assert.True(unsupportedCandidate.HasValue,
            "The cave fixture did not produce an unsupported gravity deposit on a vertical chunk seam.");
        Assert.True(supportedCandidate.HasValue,
            "The geology fixture did not produce a supported underground gravity deposit.");

        var candidates = new[] { unsupportedCandidate.Value, supportedCandidate.Value };
        var horizontalColumns = candidates
            .Select(position => VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z).Chunk)
            .Select(coord => (coord.X, coord.Z))
            .Distinct()
            .ToArray();
        var coords = horizontalColumns
            .SelectMany(column => Enumerable.Range(0, 3)
                .Select(y => new ChunkCoord(column.X, y, column.Z)))
            .ToArray();

        var firstChunks = new Dictionary<ChunkCoord, Chunk>();
        foreach (var coord in coords.OrderByDescending(coord => coord.Y)
                     .ThenBy(coord => coord.Z).ThenBy(coord => coord.X))
            firstChunks.Add(coord, firstGenerator.Materialize(coord));

        var secondChunks = new Dictionary<ChunkCoord, Chunk>();
        foreach (var coord in coords.OrderBy(coord => coord.Y)
                     .ThenBy(coord => coord.Z).ThenBy(coord => coord.X))
            secondChunks.Add(coord, secondGenerator.Materialize(coord));

        foreach (var coord in coords)
            AssertChunksEqual(firstChunks[coord], secondChunks[coord]);

        var unsupported = unsupportedCandidate.Value;
        var (unsupportedCoord, unsupportedLocal) = VoxelCoordinates.FromWorld(
            unsupported.X, unsupported.Y, unsupported.Z);
        Assert.False(gravityBlocks.Contains(firstChunks[unsupportedCoord].GetBlock(
            unsupportedLocal.X, unsupportedLocal.Y, unsupportedLocal.Z)),
            "Unsupported generated sand/gravel should be replaced by stable host rock.");

        var supported = supportedCandidate.Value;
        var (supportedCoord, supportedLocal) = VoxelCoordinates.FromWorld(
            supported.X, supported.Y, supported.Z);
        Assert.True(gravityBlocks.Contains(firstChunks[supportedCoord].GetBlock(
            supportedLocal.X, supportedLocal.Y, supportedLocal.Z)),
            "A generated sand/gravel deposit with solid support must retain its material.");

        foreach (var (coord, chunk) in firstChunks)
        {
            var lower = coord.Y > 0
                ? firstChunks[new ChunkCoord(coord.X, coord.Y - 1, coord.Z)]
                : null;
            for (var z = 0; z < Chunk.Size; z++)
            for (var y = 0; y < Chunk.Size; y++)
            for (var x = 0; x < Chunk.Size; x++)
            {
                var cell = chunk.GetCell(x, y, z);
                if (cell.IsEmpty || !gravityBlocks.Contains(cell.Block))
                    continue;

                var worldY = coord.Y * Chunk.Size + y;
                if (worldY == 0)
                    continue;

                var supportedBelow = y > 0
                    ? !chunk.GetCell(x, y - 1, z).IsEmpty
                    : lower is not null &&
                      !lower.GetCell(x, Chunk.Size - 1, z).IsEmpty;
                Assert.True(supportedBelow,
                    $"Generated unsupported gravity block remains at " +
                    $"{coord.X * Chunk.Size + x},{worldY},{coord.Z * Chunk.Size + z}.");
            }
        }

        // Generated chunks enter physics through the same resident-chunk
        // activation path as restored chunks. A valid generated world must
        // produce no gravity wakeups when these chunks become resident.
        var world = new VoxelWorld();
        foreach (var (coord, chunk) in firstChunks)
            world.InsertChunk(coord, chunk);

        var physicsUpdates = new BlockPhysicsUpdateQueue();
        var mutations = new VoxelMutationRuntime(
            world,
            new WorldUpdateQueue(),
            new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(),
            physicsUpdates,
            new MeshletContentRevisions(),
            new MeshletContentRevisions());
        var droppedBlocks = new DroppedBlockRuntime(world, blocks);
        using var physics = new BlockPhysicsRuntime(
            world, blocks, mutations, physicsUpdates, droppedBlocks);

        var queuedWakeups = coords.Sum(physics.EnqueueResidentChunk);
        Assert.Equal(0, queuedWakeups);
        Assert.Equal(0, physicsUpdates.Count);
        Assert.Equal(0, physics.ProcessWakeups().FallingStarted);
    }

    private static void AssertChunksEqual(Chunk expected, Chunk actual)
    {
        for (var z = 0; z < Chunk.Size; z++)
        for (var y = 0; y < Chunk.Size; y++)
        for (var x = 0; x < Chunk.Size; x++)
            Assert.Equal(
                expected.GetCell(x, y, z),
                actual.GetCell(x, y, z));
    }

    [Fact]
    public void GeologyVariesAtOneAltitudeAndThroughVerticalMaterializedColumns()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome();
        var materials = new BiomeSurfaceMaterialField(Seed, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var sameAltitude = new HashSet<BlockRuntimeId>();
        var verticalColumnsWithVariation = 0;

        for (var z = -96; z <= 96; z += 3)
        for (var x = -96; x <= 96; x += 3)
        {
            Assert.True(materials.TryGeologyBlockAt(
                sample, x, 24, z, out var sameHeightBlock));
            sameAltitude.Add(sameHeightBlock);

            var vertical = new HashSet<BlockRuntimeId>();
            for (var y = 3; y <= 40; y++)
            {
                Assert.True(materials.TryGeologyBlockAt(
                    sample, x, y, z, out var block));
                vertical.Add(block);
            }

            if (vertical.Count > 1)
                verticalColumnsWithVariation++;
        }

        Assert.True(sameAltitude.Count >= 3,
            $"Expected host rock and deposits at the same Y; found {sameAltitude.Count} material(s).");
        Assert.True(verticalColumnsWithVariation > 0,
            "No sampled world-space column changed geological material with Y.");
    }

    [Fact]
    public void MaterializedVerticalWallBreaksLongMaterialColumns()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome();
        var dimension = CreateDimension(biome.Id);
        var generator = new BiomeWorldGenerator(
            Seed, dimension, blocks, new BiomeRegistry([biome]));
        var chunks = new Dictionary<ChunkCoord, Chunk>();
        var materials = new HashSet<BlockRuntimeId>();
        var columnsWithVariation = 0;
        var horizontalTransitions = 0;

        // Read the actual materialized voxel wall, not direct noise-field
        // queries. The terrain is flat at Y=40, so every sampled voxel below
        // it is part of one continuous exposed solid section.
        for (var x = -32; x < 32; x++)
        {
            var previous = BlockRuntimeId.Air;
            var column = new HashSet<BlockRuntimeId>();
            for (var y = 8; y < 40; y++)
            {
                var (coord, local) = VoxelCoordinates.FromWorld(x, y, 0);
                if (!chunks.TryGetValue(coord, out var chunk))
                {
                    chunk = generator.Materialize(coord);
                    chunks.Add(coord, chunk);
                }

                var block = chunk.GetBlock(local.X, local.Y, local.Z);
                materials.Add(block);
                column.Add(block);
                if (!previous.IsAir && previous != block)
                    horizontalTransitions++;
                previous = block;
            }

            if (column.Count > 1)
                columnsWithVariation++;
        }

        Assert.True(materials.Count >= 4,
            $"Expected host rock and multiple materials on a materialized wall; found {materials.Count}.");
        Assert.True(columnsWithVariation >= 48,
            $"Long single-material columns remain in the materialized wall: {columnsWithVariation}/64 varied.");
        Assert.True(horizontalTransitions >= 100,
            $"Material boundaries are still too continuous on the wall: {horizontalTransitions} transitions.");
    }

    [Fact]
    public void FlatExposedSurfaceRetainsCoverAndShowsGeologicalVariation()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome(surfaceCoverage: 0.4f);
        var dimension = CreateDimension(biome.Id);
        var biomes = new BiomeRegistry([biome]);
        var generator = new BiomeWorldGenerator(Seed, dimension, blocks, biomes);
        var surfaceMaterials = new HashSet<BlockRuntimeId>();

        // Sample a 64x64 flat surface so low authored exposure coverage remains
        // meaningful while checking actual materialized top voxels.
        var chunksWithSurfaceVariation = 0;
        for (var chunkZ = -2; chunkZ < 2; chunkZ++)
        for (var chunkX = -2; chunkX < 2; chunkX++)
        {
            var chunk = generator.Materialize(new ChunkCoord(chunkX, 2, chunkZ));
            var localSurface = new HashSet<BlockRuntimeId>();
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
            {
                var block = chunk.GetBlock(x, 40 % Chunk.Size, z);
                surfaceMaterials.Add(block);
                localSurface.Add(block);
            }

            if (localSurface.Count > 1)
                chunksWithSurfaceVariation++;
        }

        Assert.True(chunksWithSurfaceVariation >= 12,
            $"Flat exposed surface remains uniform within most chunks: {chunksWithSurfaceVariation}/16 varied.");
        Assert.Contains(blocks.GetId("asteria:grass"), surfaceMaterials);
        Assert.Contains(blocks.GetId("asteria:dirt"), surfaceMaterials);
        Assert.Contains(blocks.GetId("asteria:gravel"), surfaceMaterials);
        Assert.True(surfaceMaterials.Count >= 4,
            "A flat surface should retain its grass cover while exposing varied geological materials.");
    }

    private static BlockRegistry CreateBlocks(bool gravityBlocks = false) => new(
    [
        new BlockDefinition("asteria:grass"),
        new BlockDefinition("asteria:dirt"),
        new BlockDefinition("asteria:stone"),
        new BlockDefinition("asteria:sand", tags: gravityBlocks
            ? [BlockPhysicsCapabilities.Gravity] : null),
        new BlockDefinition("asteria:gravel", tags: gravityBlocks
            ? [BlockPhysicsCapabilities.Gravity] : null),
        new BlockDefinition("asteria:clay"),
    ]);

    private static BiomeDefinition CreateBiome(float surfaceCoverage = 0f) => new(
        "asteria:test/geology",
        new BiomeSurfaceLayoutDefinition(regionMin: 128, regionMax: 128),
        new BiomeTerrainDefinition(
            baseHeightOffset: 40f,
            macroAmplitude: 0f,
            macroScale: 128,
            detailAmplitude: 0f,
            detailScale: 32),
        [
            new BiomeSurfaceLayerDefinition("asteria:grass", depth: 1),
            new BiomeSurfaceLayerDefinition("asteria:dirt", depth: 2),
            new BiomeSurfaceLayerDefinition("asteria:stone"),
        ],
        geology: new BiomeGeologyDefinition(
            "asteria:stone",
            ["asteria:sand", "asteria:gravel", "asteria:clay", "asteria:dirt"],
            scale: 24,
            verticalScale: 6,
            detailScale: 5,
            selectionScale: 12,
            coverage: 0.78f,
            roughness: 0.32f,
            surfaceCoverDepth: 3,
            surfaceCoverage: surfaceCoverage));

    private static DimensionDefinition CreateDimension(
        string biomeId,
        DimensionCaveDefinition? caves = null) => new(
        new DimensionId("asteria:test"),
        [biomeId],
        seaLevel: 0,
        gravityStrength: 18f,
        new DimensionSpawnDefinition(0, 0),
        new DimensionEnvironmentDefinition(
            new DimensionColor(0, 0, 0),
            new DimensionColor(255, 255, 255),
            1f,
            new DimensionColor(0, 0, 0),
            0f),
        caves: caves);
}
