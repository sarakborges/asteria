using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class TerrainDensityTests
{
    [Fact]
    public void ContiguousSolidDepthAboveMatchesScalarScanForEveryDensityStorageMode()
    {
        const int width = 3;
        const int height = 16;
        const int depth = 2;
        var masks = new ushort[]
        {
            ushort.MaxValue,
            0b1011_1100_0001_1111,
            0b0000_0000_0000_0000,
            0b1000_0000_0000_0001,
            0b1110_1111_1111_1111,
            0b0000_1111_0000_1111,
        };
        var densities = new double[width * height * depth];

        for (var z = 0; z < depth; z++)
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = z * width * height + y * width + x;
            densities[index] =
                (masks[z * width + x] & (1 << y)) != 0
                    ? 1d
                    : -1d;
        }

        double DensityAt(int x, int y, int z) =>
            densities[(z * height + y) * width + x];

        var volumes = new[]
        {
            new TerrainDensityVolume(
                width, height, depth, masks, DensityAt,
                volumeBiomes: null, volumeBiomeField: null),
            new TerrainDensityVolume(
                width, height, depth, densities),
            new TerrainDensityVolume(
                width, height, depth, DensityAt,
                volumeBiomes: null, volumeBiomeField: null),
        };

        uint[] maximumDepths = [0, 1, 2, 4, 15, 16, 32];
        foreach (var volume in volumes)
        for (var z = 0; z < depth; z++)
        for (var x = 0; x < width; x++)
        for (var y = -1; y < height; y++)
        foreach (var maximumDepth in maximumDepths)
        {
            var expected = 0u;
            while (expected < maximumDepth &&
                   y + (long)expected + 1L < height &&
                   volume.IsSolidAt(
                       x, y + (int)expected + 1, z))
            {
                expected++;
            }

            Assert.Equal(
                expected,
                volume.ContiguousSolidDepthAboveInVolume(
                    x, y, z, maximumDepth));
        }
    }

    [Fact]
    public void CaveSpikeClearanceBitCountsMatchScalarColumnScan()
    {
        const int height = 16;
        for (var mask = 0; mask <= ushort.MaxValue; mask += 257)
        for (var localY = 0; localY < height; localY++)
        foreach (var down in new[] { false, true })
        {
            var expected = 0;
            for (var offset = 0; offset < height; offset++)
            {
                var y = localY + (down ? -offset : offset);
                if ((uint)y >= height ||
                    (mask & (1 << y)) != 0)
                    break;
                expected++;
            }

            Assert.Equal(
                expected,
                CaveSpikeField.CountContiguousVoidDepthInMask(
                    (ushort)mask, height, localY, down));
        }
    }

    [Fact]
    public void CaveDensityIsSubtractiveAndCannotPierceTheSurface()
    {
        var generator = Generator(
            caves: WideCaves());

        var surface = generator.SurfaceHeight(4, -9);
        Assert.Equal(96, surface);
        Assert.True(generator.DensityAt(4, surface, -9) >= 0d);
        Assert.True(generator.DensityAt(4, surface + 1, -9) < 0d);
        Assert.True(generator.DensityAt(4, surface - 2, -9) >= 0d);
        Assert.True(generator.DensityAt(4, surface - 36, -9) < 0d);

        var caveY = surface - 36;
        var caveChunkY = caveY / Chunk.Size;
        var underground = generator.Materialize(
            new ChunkCoord(0, caveChunkY, -1));
        Assert.True(
            underground.GetBlock(
                4,
                caveY % Chunk.Size,
                Chunk.Size - 9).IsAir);
    }

    [Fact]
    public void ChambersExpandCavesWithoutPiercingSurfaceOrDepthBounds()
    {
        var tunnels = Generator(caves: ChamberCaves(withChambers: false));
        var chambers = Generator(caves: ChamberCaves(withChambers: true));
        var surface = chambers.SurfaceHeight(0, 0);
        var expanded = false;

        for (var z = -72; z <= 72; z += 12)
        for (var x = -72; x <= 72; x += 12)
        for (var depth = 16; depth <= 64; depth += 8)
        {
            var y = surface - depth;
            var original = tunnels.DensityAt(x, y, z);
            var widened = chambers.DensityAt(x, y, z);
            Assert.True(widened <= original + 1e-9,
                $"Chambers must not restore solid terrain at {x},{y},{z}.");
            expanded |= original >= 0d && widened < 0d;
        }

        Assert.True(expanded, "Chambers should widen some tunnel sections.");
        Assert.True(chambers.DensityAt(0, surface - 2, 0) >= 0d);
        Assert.True(chambers.DensityAt(0, surface - 88, 0) >= 0d);
    }

    [Fact]
    public void CavePaletteMaterializationMatchesVoidDensityAcrossChunkSeams()
    {
        // A carved-volume palette activates cross-chunk cave-face painting.
        // Cave voids must still remain empty and solid faces must stay solid
        // on both sides of the chunk seam.
        var generator = Generator(
            caves: WideCaves(),
            caveSpike: new BiomeCaveSpikeDefinition(
                "asteria:spike_stone", 0.00001f, 2, 3, 3,
                [CaveSpikeDirection.Up, CaveSpikeDirection.Down]));
        var materialized = new Dictionary<ChunkCoord, Chunk>();
        var spike = Block("asteria:spike_stone");

        // Cover the complete six-voxel halo on both sides of the X/Z
        // seams, including their overlap at the four-chunk corner.
        for (var x = 10; x <= 21; x++)
        for (var z = 10; z <= 21; z++)
        for (var y = 32; y < 96; y++)
        {
            var (coord, local) = VoxelCoordinates.FromWorld(x, y, z);
            if (!materialized.TryGetValue(coord, out var chunk))
            {
                chunk = generator.Materialize(coord);
                materialized.Add(coord, chunk);
            }

            var block = chunk.GetBlock(local.X, local.Y, local.Z);
            if (generator.IsCaveVoidAt(x, y, z))
                Assert.True(block.IsAir || block == spike);
            else
                Assert.False(block.IsAir);
        }
    }

    [Fact]
    public void ChamberDensityMatchesScalarAndVolumeAcrossChunkBoundaries()
    {
        var generator = Generator(caves: ChamberCaves(withChambers: true));
        var volume = generator.SampleDensityVolume(-17, 41, -17, 4, 4, 4);
        for (var z = 0; z < 4; z++)
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
        {
            var expectedDensity =
                generator.DensityAt(-17 + x, 41 + y, -17 + z);
            Assert.Equal(
                expectedDensity,
                volume.DensityAt(x, y, z));
            Assert.Equal(
                expectedDensity >= 0d,
                volume.IsSolidAtUnchecked(x, y, z));
        }
    }

    [Fact]
    public void ChamberConfigurationRejectsInvalidRanges()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new DimensionCaveChamberDefinition(
                1, 64, -0.2f, 0.3f, 0.6f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new DimensionCaveChamberDefinition(
                128, 64, 0.3f, 0.3f, 0.6f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new DimensionCaveLayerDefinition(
                10, 80,
                [new DimensionCaveNoiseChannelDefinition(56, 36)],
                0.4f, 24f, 6,
                chambers: new DimensionCaveChamberDefinition(
                    128, 64, -0.2f, 0.3f, 0.4f)));
    }

    [Fact]
    public void CaveSpikesGenerateOnFloorsAndCeilings()
    {
        var generator = Generator(
            caves: WideCaves(),
            caveSpike: new BiomeCaveSpikeDefinition(
                "asteria:spike_stone", 1f, 3, 5, 6,
                [CaveSpikeDirection.Up, CaveSpikeDirection.Down]));
        var floor = generator.Materialize(new ChunkCoord(0, 1, 0));
        var ceiling = generator.Materialize(new ChunkCoord(0, 5, 0));
        Assert.Contains(
            Enumerable.Range(0, Chunk.Size)
                .SelectMany(y => Enumerable.Range(0, Chunk.Size)
                    .Select(x => floor.GetCell(x, y, 0))),
            cell => cell.Block == Block("asteria:spike_stone") &&
                !SpikeSegmentState.IsDown(cell.State));
        Assert.Contains(
            Enumerable.Range(0, Chunk.Size)
                .SelectMany(y => Enumerable.Range(0, Chunk.Size)
                    .Select(x => ceiling.GetCell(x, y, 0))),
            cell => cell.Block == Block("asteria:spike_stone") &&
                SpikeSegmentState.IsDown(cell.State));
    }

    [Fact]
    public void CaveSpikesTakePrecedenceOverCaveFloorVegetation()
    {
        var spikeRule = new BiomeCaveSpikeDefinition(
            "asteria:spike_stone", 1f, 3, 5, 6,
            [CaveSpikeDirection.Up, CaveSpikeDirection.Down],
            minSpacing: 1);
        var bare = Generator(caves: WideCaves(), caveSpike: spikeRule);
        var planted = Generator(
            caves: WideCaves(),
            caveSpike: spikeRule,
            caveDecoration: new BiomeDecorationDefinition(
                "asteria:test_fern", 1f, ["asteria:stone"]));
        var spike = Block("asteria:spike_stone");
        var fern = Block("asteria:test_fern");
        var spikeCount = 0;
        var fernCount = 0;

        // Generate floor and ceiling chunks out of order. A plant must
        // never replace a spike base and leave its higher tiers floating.
        foreach (var coord in new[]
                 {
                     new ChunkCoord(0, 5, 0),
                     new ChunkCoord(0, 1, 0),
                     new ChunkCoord(0, 2, 0),
                 })
        {
            var reference = bare.Materialize(coord);
            var actual = planted.Materialize(coord);
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
            for (var y = 0; y < Chunk.Size; y++)
            {
                var expected = reference.GetCell(x, y, z);
                var observed = actual.GetCell(x, y, z);
                if (expected.Block == spike)
                {
                    Assert.Equal(spike, observed.Block);
                    Assert.Equal(expected.State, observed.State);
                    spikeCount++;
                }
                if (observed.Block == fern)
                {
                    Assert.NotEqual(spike, expected.Block);
                    fernCount++;
                }
            }
        }

        Assert.True(spikeCount > 0);
        Assert.True(fernCount > 0);
    }

    [Fact]
    public void CaveSpikeClusterCanSuppressGrowthWithoutChangingCaveDensity()
    {
        var allowed = Generator(
            caves: WideCaves(),
            caveSpike: new BiomeCaveSpikeDefinition(
                "asteria:spike_stone", 1f, 2, 8, 9,
                [CaveSpikeDirection.Up, CaveSpikeDirection.Down],
                minSpacing: 1));
        var suppressed = Generator(
            caves: WideCaves(),
            caveSpike: new BiomeCaveSpikeDefinition(
                "asteria:spike_stone", 1f, 2, 8, 9,
                [CaveSpikeDirection.Up, CaveSpikeDirection.Down],
                cluster: new CaveSpikeClusterDefinition(
                    20, 14, 1f, 0f),
                minSpacing: 1));

        var floor = allowed.Materialize(new ChunkCoord(0, 1, 0));
        var ceiling = allowed.Materialize(new ChunkCoord(0, 5, 0));
        var emptyFloor = suppressed.Materialize(new ChunkCoord(0, 1, 0));
        var emptyCeiling = suppressed.Materialize(new ChunkCoord(0, 5, 0));
        var floorCandidates = 0;
        var ceilingCandidates = 0;
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        for (var y = 0; y < Chunk.Size; y++)
        {
            floorCandidates += floor.GetCell(x, y, z).Block ==
                Block("asteria:spike_stone") ? 1 : 0;
            ceilingCandidates += ceiling.GetCell(x, y, z).Block ==
                Block("asteria:spike_stone") ? 1 : 0;
            Assert.NotEqual(Block("asteria:spike_stone"),
                emptyFloor.GetCell(x, y, z).Block);
            Assert.NotEqual(Block("asteria:spike_stone"),
                emptyCeiling.GetCell(x, y, z).Block);

        }

        Assert.True(floorCandidates > 0);
        Assert.True(ceilingCandidates > 0);

        // Spike decoration must not alter the authoritative terrain field.
        for (var z = 0; z < Chunk.Size; z += 8)
        for (var x = 0; x < Chunk.Size; x += 8)
        for (var y = 0; y < Chunk.Size; y += 8)
        {
            Assert.Equal(allowed.DensityAt(x, 16 + y, z),
                suppressed.DensityAt(x, 16 + y, z));
        }
    }

    [Fact]
    public void CaveSpikeSpacingIsStableAcrossDirectionsAndHorizontalChunks()
    {
        var generator = Generator(
            caves: WideCaves(),
            caveSpike: new BiomeCaveSpikeDefinition(
                "asteria:spike_stone", 1f, 2, 8, 9,
                [CaveSpikeDirection.Up, CaveSpikeDirection.Down],
                minSpacing: 1));
        var coordinates = new HashSet<(int X, int Z)>();
        for (var chunkX = 0; chunkX <= 1; chunkX++)
        for (var chunkY = 1; chunkY <= 5; chunkY += 4)
        {
            var chunk = generator.Materialize(
                new ChunkCoord(chunkX, chunkY, 0));
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
            for (var y = 0; y < Chunk.Size; y++)
            {
                if (chunk.GetCell(x, y, z).Block ==
                    Block("asteria:spike_stone"))
                    coordinates.Add((chunkX * Chunk.Size + x, z));
            }
        }

        Assert.NotEmpty(coordinates);
        foreach (var (x, z) in coordinates)
        for (var dz = -1; dz <= 1; dz++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0)
                continue;
            Assert.DoesNotContain((x + dx, z + dz), coordinates);
        }
    }

    [Fact]
    public void CaveSpikeClusteringRejectsInvalidScalesAndSpacing()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new CaveSpikeClusterDefinition(1, 14, -0.2f, 0.25f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new CaveSpikeClusterDefinition(24, 1, -0.2f, 0.25f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new CaveSpikeClusterDefinition(24, 16, float.NaN, 0.25f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new CaveSpikeClusterDefinition(24, 16, -0.2f, -0.1f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeCaveSpikeDefinition(
                "asteria:spike_stone", 0.1f, 2, 8, 9,
                [CaveSpikeDirection.Up], minSpacing: 5));
    }

    [Fact]
    public void SpikeShapeProfileTapersAndSupportsReversedGrowth()
    {
        var shape = BlockShapeDefinition.Spike();
        var low = SpikeSegmentState.Encode(0, 5, false);
        var high = SpikeSegmentState.Encode(4, 5, false);
        var downward = SpikeSegmentState.Encode(0, 5, true);
        Assert.True(SpikeSegmentState.RadiusAt(shape, low, 0f) >
                    SpikeSegmentState.RadiusAt(shape, high, 1f));
        Assert.Equal(SpikeSegmentState.RadiusAt(shape, low, 0f),
            SpikeSegmentState.RadiusAt(shape, downward, 1f));
        Assert.ThrowsAny<ArgumentException>(() =>
            SpikeSegmentState.Encode(5, 5, false));
    }

    [Fact]
    public void CachedDensityFastPathMatchesScalarInSolidSurfaceAndSkyBands()
    {
        var generator = Generator();
        foreach (var chunkY in new[] { 0, 4, 6, 7 })
        {
            var chunk = generator.Materialize(new ChunkCoord(0, chunkY, 0));
            var originY = chunkY * Chunk.Size;
            for (var z = 0; z < Chunk.Size; z += 5)
            for (var x = 0; x < Chunk.Size; x += 5)
            for (var y = 0; y < Chunk.Size; y += 2)
            {
                Assert.Equal(
                    generator.DensityAt(x, originY + y, z) >= 0d,
                    !chunk.GetBlock(x, y, z).IsAir);
            }
        }
    }

    [Fact]
    public void SampleDensityVolumeMatchesScalarAcrossNegativeCoordinates()
    {
        var generator = Generator(caves: WideCaves());
        var volume = generator.SampleDensityVolume(
            -32, 61, -33, 4, 4, 4);

        for (var z = 0; z < 4; z++)
        {
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    Assert.Equal(
                        generator.DensityAt(
                            -32 + x, 61 + y, -33 + z),
                        volume.DensityAt(x, y, z));
                }
            }
        }
    }

    [Fact]
    public void SampleDensityVolumeMatchesScalarForFloatingFormation()
    {
        var generator = Generator(
            floating: new BiomeAdditiveDensityDefinition(
                minY: 80,
                maxY: 112,
                horizontalScale: 64,
                detailScale: 24,
                coverage: 1f,
                roughness: 0.2f,
                densityScale: 30f));
        var point =
            FindVolumeSolid(
                generator,
                y: 100);
        var coord =
            VoxelCoordinates.FromWorld(
                    point.X,
                    96,
                    point.Z)
                .Chunk;
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(
                coord);
        var volume =
            generator.SampleDensityVolume(
                originX,
                originY,
                originZ,
                Chunk.Size,
                Chunk.Size,
                Chunk.Size);

        for (var z = 0;
             z < Chunk.Size;
             z += 5)
        {
            for (var y = 0;
                 y < Chunk.Size;
                 y += 5)
            {
                for (var x = 0;
                     x < Chunk.Size;
                     x += 5)
                {
                    Assert.Equal(
                        generator.DensityAt(
                            originX + x,
                            originY + y,
                            originZ + z),
                        volume.DensityAt(
                            x,
                            y,
                            z));
                }
            }
        }
    }

    [Fact]
    public void CachedColumnDensityMatchesScalarAcrossVerticalChunkBands()
    {
        var generator = Generator(
            caves: WideCaves(),
            floating: new BiomeAdditiveDensityDefinition(
                minY: 80,
                maxY: 112,
                horizontalScale: 64,
                detailScale: 24,
                coverage: 1f,
                roughness: 0.2f,
                densityScale: 30f));
        var point = FindVolumeSolid(generator, y: 96);
        var column = VoxelCoordinates.FromWorld(
            point.X, 0, point.Z).Chunk;
        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(column);

        // Includes full rock, cave, surface, additive and empty-sky
        // bands. Generate out of order to exercise column reuse.
        foreach (var chunkY in new[] { 8, 0, 6, 2, 7, 4, 5, 3 })
        {
            var chunk = generator.Materialize(
                new ChunkCoord(column.X, chunkY, column.Z));

            for (var z = 0; z < Chunk.Size; z += 7)
            {
                for (var x = 0; x < Chunk.Size; x += 7)
                {
                    for (var y = 0; y < Chunk.Size; y += 3)
                    {
                        var worldY = chunkY * Chunk.Size + y;
                        Assert.Equal(
                            generator.DensityAt(
                                originX + x,
                                worldY,
                                originZ + z) >= 0d,
                            !chunk.GetBlock(x, y, z).IsAir);
                    }
                }
            }
        }
    }

    [Fact]
    public void FloatingFormationExtendsSurfaceRangeAndMaterializesAboveBase()
    {
        var generator = Generator(
            floating: new BiomeAdditiveDensityDefinition(
                minY: 80,
                maxY: 112,
                horizontalScale: 64,
                detailScale: 24,
                coverage: 1f,
                roughness: 0f,
                densityScale: 30f));

        var point =
            FindVolumeSolid(
                generator,
                y: 100);
        var top =
            generator.SurfaceHeight(
                point.X,
                point.Z);
        Assert.InRange(
            top,
            100,
            112);
        Assert.True(
            generator.DensityAt(
                point.X,
                100,
                point.Z) >=
            0d);
        Assert.True(
            generator.DensityAt(
                point.X,
                top + 1,
                point.Z) <
            0d);

        var horizontal =
            VoxelCoordinates.FromWorld(
                    point.X,
                    0,
                    point.Z)
                .Chunk;
        var range =
            generator.GetSurfaceRange(
                horizontal.X,
                horizontal.Z);
        Assert.True(
            range.MaximumWorldY >=
            top);

        var address =
            VoxelCoordinates.FromWorld(
                point.X,
                top,
                point.Z);
        var chunk =
            generator.Materialize(
                address.Chunk);
        Assert.Equal(
            Block("asteria:block_grass"),
            chunk.GetBlock(
                address.Local.X,
                address.Local.Y,
                address.Local.Z));
    }

    [Fact]
    public void CachedColumnDensityFastPathMatchesScalarAtEveryVoxel()
    {
        var generator = Generator(
            caves: WideCaves(),
            floating: new BiomeAdditiveDensityDefinition(
                minY: 80,
                maxY: 112,
                horizontalScale: 64,
                detailScale: 24,
                coverage: 1f,
                roughness: 0.2f,
                densityScale: 30f));
        var point = FindVolumeSolid(generator, y: 96);
        var horizontal = VoxelCoordinates.FromWorld(
            point.X, 0, point.Z).Chunk;
        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(horizontal);
        const int chunkY = 5;
        var chunk = generator.Materialize(
            new ChunkCoord(horizontal.X, chunkY, horizontal.Z));

        for (var z = 0; z < Chunk.Size; z++)
        for (var y = 0; y < Chunk.Size; y++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var worldX = originX + x;
            var worldY = chunkY * Chunk.Size + y;
            var worldZ = originZ + z;
            var expectedSolid =
                generator.DensityAt(worldX, worldY, worldZ) >= 0d;
            var actualSolid = !chunk.GetBlock(x, y, z).IsAir;
            Assert.True(
                expectedSolid == actualSolid,
                $"Density fast path diverged at {worldX},{worldY},{worldZ}: expected {expectedSolid}, actual {actualSolid}.");
        }
    }

    [Fact]
    public void DensityOnlyBoundaryColumnCacheMatchesScalarDensityAcrossCavesAndFormations()
    {
        var generator = Generator(
            caves: WideCaves(),
            floating: new BiomeAdditiveDensityDefinition(
                minY: 80,
                maxY: 112,
                horizontalScale: 64,
                detailScale: 24,
                coverage: 1f,
                roughness: 0.2f,
                densityScale: 30f));
        var cache = new SurfaceTerrainColumnCache(
            generator.Terrain,
            capacity: 8,
            densityColumnCapacity: Chunk.Area * 8);
        var volumePoint = FindVolumeSolid(generator, y: 96);
        var samples = new[]
        {
            (X: volumePoint.X, Z: volumePoint.Z, OriginY: 16),
            (X: volumePoint.X - 1, Z: volumePoint.Z, OriginY: 32),
            (X: volumePoint.X, Z: volumePoint.Z + 1, OriginY: 80),
            (X: volumePoint.X + Chunk.Size, Z: volumePoint.Z + Chunk.Size, OriginY: 96),
        };

        foreach (var sample in samples)
        {
            var mask = cache.GetDensityColumnMask(
                sample.X,
                sample.OriginY,
                sample.Z,
                out var cacheHit,
                out var densityProbes);
            Assert.False(cacheHit);
            Assert.True(densityProbes > 0);

            for (var localY = 0; localY < Chunk.Size; localY++)
            {
                var worldY = sample.OriginY + localY;
                var expectedSolid =
                    generator.DensityAt(sample.X, worldY, sample.Z) >= 0d;
                var actualSolid = (mask & (1u << localY)) != 0;
                Assert.True(
                    expectedSolid == actualSolid,
                    $"Density-only boundary cache diverged at {sample.X},{worldY},{sample.Z}: expected {expectedSolid}, actual {actualSolid}.");
            }

            var cachedMask = cache.GetDensityColumnMask(
                sample.X,
                sample.OriginY,
                sample.Z,
                out cacheHit,
                out densityProbes);
            Assert.True(cacheHit);
            Assert.Equal(0, densityProbes);
            Assert.Equal(mask, cachedMask);
        }
    }

    [Fact]
    public void SparseDensityBoundaryBatchesMatchScalarDensityAcrossOverlappingReads()
    {
        var generator = Generator(
            caves: WideCaves(),
            floating: new BiomeAdditiveDensityDefinition(
                minY: 80,
                maxY: 112,
                horizontalScale: 64,
                detailScale: 24,
                coverage: 1f,
                roughness: 0.2f,
                densityScale: 30f));
        var volumePoint = FindVolumeSolid(generator, y: 96);
        var position = VoxelCoordinates.FromWorld(
            volumePoint.X, 96, volumePoint.Z);
        var cache = new SurfaceTerrainColumnCache(
            generator.Terrain,
            capacity: 8,
            densityColumnCapacity: Chunk.Area * 8);
        var chunkX = position.Chunk.X;
        var chunkZ = position.Chunk.Z;
        var targetIndex = position.Local.Z * Chunk.Size + position.Local.X;
        var firstBatch = new[]
        {
            0, 1, 15, 16, 17, 31, 32, 127, 240, 255, targetIndex,
        };
        var secondBatch = new[]
        {
            1, 17, 31, 64, 128, 224, 240, 254, 255, targetIndex,
        };

        void AssertBatchMatchesScalar(int[] indices)
        {
            var masks = new ushort[indices.Length];
            var requestedMasks =
                Enumerable.Repeat(ushort.MaxValue, indices.Length).ToArray();
            cache.ReadDensityColumnMasks(
                chunkX,
                originY: 96,
                chunkZ,
                indices,
                requestedMasks,
                masks,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _);

            var originX = checked(chunkX * Chunk.Size);
            var originZ = checked(chunkZ * Chunk.Size);
            for (var index = 0; index < indices.Length; index++)
            {
                var localX = indices[index] % Chunk.Size;
                var localZ = indices[index] / Chunk.Size;
                var worldX = checked(originX + localX);
                var worldZ = checked(originZ + localZ);
                for (var localY = 0; localY < Chunk.Size; localY++)
                {
                    var worldY = 96 + localY;
                    var expectedSolid =
                        generator.DensityAt(worldX, worldY, worldZ) >= 0d;
                    var actualSolid =
                        (masks[index] & (1u << localY)) != 0;
                    Assert.True(
                        expectedSolid == actualSolid,
                        $"Sparse boundary batch diverged at {worldX},{worldY},{worldZ}: expected {expectedSolid}, actual {actualSolid}.");
                }
            }
        }

        AssertBatchMatchesScalar(firstBatch);
        AssertBatchMatchesScalar(secondBatch);
    }

    [Fact]
    public void VolumeAndMaterializationStayAlignedAcrossVerticalChunkBoundary()
    {
        var generator = Generator(
            caves: WideCaves(),
            floating: new BiomeAdditiveDensityDefinition(
                minY: 80,
                maxY: 112,
                horizontalScale: 64,
                detailScale: 24,
                coverage: 1f,
                roughness: 0.2f,
                densityScale: 30f));
        var point =
            FindVolumeSolid(
                generator,
                y: 96);
        var horizontal =
            VoxelCoordinates.FromWorld(
                    point.X,
                    0,
                    point.Z)
                .Chunk;
        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(
                horizontal);
        var lower =
            generator.Materialize(
                new ChunkCoord(
                    horizontal.X,
                    95 / Chunk.Size,
                    horizontal.Z));
        var upper =
            generator.Materialize(
                new ChunkCoord(
                    horizontal.X,
                    96 / Chunk.Size,
                    horizontal.Z));

        foreach (var (y, localY, chunk) in
                 new[]
                 {
                     (95, 95 % Chunk.Size, lower),
                     (96, 0, upper),
                 })
        {
            for (var z = 0;
                 z < Chunk.Size;
                 z += 7)
            {
                for (var x = 0;
                     x < Chunk.Size;
                     x += 7)
                {
                    Assert.Equal(
                        generator.DensityAt(
                            originX + x,
                            y,
                            originZ + z) >=
                        0d,
                        !chunk.GetBlock(
                                x,
                                localY,
                                z)
                            .IsAir);
                }
            }
        }
    }

    [Fact]
    public void ShellPlanesOverrideGeneratedCavesAndRemainUnbreakableByTerrain()
    {
        var generator = Generator(
            caves: WideCaves(),
            floorY: 0,
            roofY: 140);

        var floor = generator.Materialize(new ChunkCoord(0, 0, 0));
        var roof = generator.Materialize(
            new ChunkCoord(0, 140 / Chunk.Size, 0));
        Assert.Equal(
            Block("asteria:shell_sphere"),
            floor.GetBlock(0, 0, 0));
        Assert.Equal(
            Block("asteria:shell_sphere"),
            roof.GetBlock(0, 140 % Chunk.Size, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.Materialize(new ChunkCoord(0, -1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.DensityAt(0, -1, 0));
    }

    [Theory]
    [InlineData(-1, 1, 64, 24, 0.5, 0.2, 20)]
    [InlineData(80, 80, 64, 24, 0.5, 0.2, 20)]
    [InlineData(80, 100, 64, 24, 0, 0.2, 20)]
    [InlineData(80, 100, 64, 24, 0.5, 1, 20)]
    public void InvalidFloatingRulesAreRejected(
        int minY, int maxY,
        uint horizontalScale, uint detailScale,
        float coverage, float roughness, float densityScale)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeAdditiveDensityDefinition(
                minY, maxY, horizontalScale, detailScale,
                coverage, roughness, densityScale));
    }

    [Fact]
    public void Terrain3dJsonAndSphereCaveRulesParse()
    {
        var biome = BiomeDefinitionJson.Parse(
            """
            {
              "id":"asteria:test/floating",
              "volumeLayout":{},
              "palette":{"default":[{"block":"asteria:stone"}]},
              "terrain3d":{"additive":[{
                "minY":80,"maxY":112,"horizontalScale":64,
                "detailScale":24,"coverage":0.55,"roughness":0.18,
                "densityScale":28
              }]}
            }
            """);
        Assert.Null(
            biome.SurfaceLayout);
        Assert.NotNull(
            biome.VolumeLayout);
        Assert.Equal(
            80,
            biome.Terrain3d!.Additive[0].MinY);
        Assert.Equal(
            112,
            biome.Terrain3d.Additive[0].MaxY);

        var dimension = DimensionDefinitionJson.Parse(
            """
            {
              "id":"asteria:test",
              "surfaceBiomes":["asteria:test/ground"],
              "volumeBiomes":["asteria:test/floating"],
              "seaLevel":32,
              "gravityStrength":18,
              "spawn":{"x":0,"z":0},
              "environment":{
                "backgroundColor":"000000",
                "ambientColor":"FFFFFF",
                "ambientEnergy":1,
                "fogColor":"000000",
                "fogDensity":0
              },
              "caves":{"layers":[{
                "minDepth":10,"maxDepth":120,
                "noiseHalfWidth":0.18,"densityScale":24,
                "boundaryFade":8,"combination":"intersection",
                "channels":[
                  {"horizontalScale":56,"verticalScale":36},
                  {"horizontalScale":56,"verticalScale":36}
                ]
              }]}
            }
            """);
        Assert.Equal(120u, dimension.Caves!.MaximumDepth);
    }

    [Fact]
    public void AdditiveLayersUseIndependentVerticalBounds()
    {
        var generator = Generator(
            additive:
            [
                new BiomeAdditiveDensityDefinition(
                    80, 104, 64, 24, 1f, 0f, 30f,
                    verticalFalloff: 2f),
                new BiomeAdditiveDensityDefinition(
                    120, 144, 64, 24, 1f, 0f, 30f,
                    horizontalFalloff: 2f),
            ]);
        var point = FindVolumeSolid(generator, y: 92);

        Assert.NotNull(generator.VolumeBiomes.Sample(
            point.X, 92, point.Z));
        Assert.Null(generator.VolumeBiomes.Sample(
            point.X, 112, point.Z));
        Assert.NotNull(generator.VolumeBiomes.Sample(
            point.X, 132, point.Z));
        Assert.True(generator.DensityAt(
            point.X, 112, point.Z) < 0d);
    }

    [Fact]
    public void DensityBoundaryCacheDefersVolumeBiomeSamplingUntilAdditiveBands()
    {
        var generator = Generator(
            additive:
            [
                new BiomeAdditiveDensityDefinition(
                    80, 104, 64, 24, 1f, 0f, 30f,
                    verticalFalloff: 2f),
                new BiomeAdditiveDensityDefinition(
                    120, 144, 64, 24, 1f, 0f, 30f,
                    horizontalFalloff: 2f),
            ]);
        var terrain = generator.Terrain;

        Assert.False(terrain.RequiresVolumeBiomeForDensityBand(64));
        Assert.True(terrain.RequiresVolumeBiomeForDensityBand(80));
        Assert.True(terrain.RequiresVolumeBiomeForDensityBand(112));
        Assert.False(terrain.RequiresVolumeBiomeForDensityBand(160));

        var point = FindVolumeSolid(generator, y: 92);
        var (coord, local) = VoxelCoordinates.FromWorld(
            point.X, 64, point.Z);
        var columnIndices = new[]
        {
            local.Z * Chunk.Size + local.X,
        };
        var cache = new SurfaceTerrainColumnCache(terrain);
        var lowerMask = new ushort[1];

        cache.ReadDensityColumnMasks(
            coord.X,
            64,
            coord.Z,
            columnIndices,
            [ushort.MaxValue],
            lowerMask,
            out _, out _, out _, out _, out _, out _);

        for (var offset = 0; offset < Chunk.Size; offset++)
        {
            var worldY = 64 + offset;
            var expected = generator.DensityAt(
                point.X, worldY, point.Z) >= 0d;
            var actual = (lowerMask[0] & (1 << offset)) != 0;
            Assert.Equal(expected, actual);
        }

        // The same cached X/Z column must acquire its volume owner later,
        // when the requested vertical band intersects additive formations.
        var upperMask = new ushort[1];
        cache.ReadDensityColumnMasks(
            coord.X,
            80,
            coord.Z,
            columnIndices,
            [ushort.MaxValue],
            upperMask,
            out _, out _, out _, out _, out _, out _);

        for (var offset = 0; offset < Chunk.Size; offset++)
        {
            var worldY = 80 + offset;
            var expected = generator.DensityAt(
                point.X, worldY, point.Z) >= 0d;
            var actual = (upperMask[0] & (1 << offset)) != 0;
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void VolumePlacementGridOwnersMatchScalarAcrossIndependentFormationBounds()
    {
        var generator = Generator(
            additive:
            [
                new BiomeAdditiveDensityDefinition(
                    80, 104, 64, 24, 1f, 0f, 30f,
                    verticalFalloff: 2f),
                new BiomeAdditiveDensityDefinition(
                    120, 144, 64, 24, 1f, 0f, 30f,
                    horizontalFalloff: 2f),
            ]);
        var point = FindVolumeSolid(generator, y: 92);
        var coord = VoxelCoordinates.FromWorld(
            point.X, 0, point.Z).Chunk;
        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);
        var grid = generator.VolumeBiomes.SamplePlacementGrid(
            originX, originZ, Chunk.Size, Chunk.Size);
        var heights = new[]
        {
            0, 79, 80, 92, 104, 105, 112,
            119, 120, 132, 144, 145, 160,
        };

        for (var localZ = 0; localZ < Chunk.Size; localZ += 5)
        for (var localX = 0; localX < Chunk.Size; localX += 5)
        foreach (var worldY in heights)
        {
            var worldX = originX + localX;
            var worldZ = originZ + localZ;
            var expected = generator.VolumeBiomes.Sample(
                worldX, worldY, worldZ);
            var actual = generator.VolumeBiomes.SampleAtY(
                grid, localX, worldY, localZ);

            Assert.Equal(expected?.Primary, actual?.Primary);
            Assert.Equal(expected?.Influences.Count ?? 0,
                actual?.Influences.Count ?? 0);
            if (expected is null || actual is null)
                continue;

            for (var index = 0; index < expected.Influences.Count; index++)
                Assert.Equal(
                    expected.Influences[index],
                    actual.Influences[index]);
        }
    }

    [Fact]
    public void IndependentCaveLayersLeaveUncoveredDepthsSolid()
    {
        var generator = Generator(
            caves: new DimensionCaveDefinition(
            [
                new DimensionCaveLayerDefinition(
                    10, 24,
                    [new DimensionCaveNoiseChannelDefinition(56, 36)],
                    1f, 24f, 2),
                new DimensionCaveLayerDefinition(
                    50, 70,
                    [new DimensionCaveNoiseChannelDefinition(56, 36)],
                    1f, 24f, 2),
            ]));
        var surface = generator.SurfaceHeight(0, 0);

        Assert.True(generator.DensityAt(0, surface - 16, 0) < 0d);
        Assert.True(generator.DensityAt(0, surface - 36, 0) >= 0d);
        Assert.True(generator.DensityAt(0, surface - 60, 0) < 0d);
    }

    [Fact]
    public void UnionCaveChannelsCarveAtLeastAsMuchAsIntersection()
    {
        var intersection = Generator(caves: LayeredCaves(
            CaveNoiseCombination.Intersection));
        var union = Generator(caves: LayeredCaves(
            CaveNoiseCombination.Union));
        var foundDifference = false;

        for (var z = -48; z <= 48; z += 12)
        {
            for (var x = -48; x <= 48; x += 12)
            {
                var strict = intersection.DensityAt(x, 56, z);
                var broad = union.DensityAt(x, 56, z);
                Assert.True(
                    broad <= strict,
                    $"Union caves must not be less subtractive at {x},56,{z}.");
                foundDifference |= broad < strict;
            }
        }

        Assert.True(foundDifference);
    }

    [Fact]
    public void ComposableDensityDefinitionsRejectInvalidBounds()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeAdditiveDensityDefinition(
                80, 112, 64, 24, 1f, 0f, 30f,
                verticalFalloff: 0f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeAdditiveDensityDefinition(
                80, 112, 64, 24, 1f, 0f, 30f,
                densityBias: 1f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new DimensionCaveDefinition([]));
        Assert.ThrowsAny<ArgumentException>(() =>
            new DimensionCaveLayerDefinition(
                10, 80, [], 0.4f, 24f, 6));
    }

    private static DimensionCaveDefinition LayeredCaves(
        CaveNoiseCombination combination) =>
        new(
        [
            new DimensionCaveLayerDefinition(
                minDepth: 10,
                maxDepth: 80,
                channels:
                [
                    new DimensionCaveNoiseChannelDefinition(24, 16),
                    new DimensionCaveNoiseChannelDefinition(96, 64),
                ],
                noiseHalfWidth: 0.45f,
                densityScale: 24f,
                boundaryFade: 6,
                combination: combination),
        ]);

    private static BlockRuntimeId Block(string id) =>
        Blocks.GetId(id);

    private static readonly BlockRegistry Blocks = new(
    [
        new BlockDefinition("asteria:stone"),
        new BlockDefinition("asteria:block_grass"),
        new BlockDefinition("asteria:dirt"),
        new BlockDefinition("asteria:shell_sphere"),
        new BlockDefinition("asteria:spike_stone",
            shape: BlockShapeDefinition.Spike(), lightDampening: 0),
        new BlockDefinition("asteria:test_fern", lightDampening: 0),
    ]);

    private static DimensionCaveDefinition ChamberCaves(bool withChambers) =>
        new(
        [
            new DimensionCaveLayerDefinition(
                minDepth: 10,
                maxDepth: 80,
                channels:
                [
                    new DimensionCaveNoiseChannelDefinition(56, 36),
                    new DimensionCaveNoiseChannelDefinition(56, 36),
                ],
                noiseHalfWidth: 0.18f,
                densityScale: 24f,
                boundaryFade: 6,
                chambers: withChambers
                    ? new DimensionCaveChamberDefinition(
                        112, 64, -1f, -0.9f, 0.75f)
                    : null),
        ]);

    private static DimensionCaveDefinition WideCaves() =>
        new(
        [
            new DimensionCaveLayerDefinition(
                minDepth: 10,
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

    private static BiomeWorldGenerator Generator(
        DimensionCaveDefinition? caves = null,
        BiomeAdditiveDensityDefinition? floating = null,
        int? floorY = null,
        int? roofY = null,
        IEnumerable<BiomeAdditiveDensityDefinition>? additive = null,
        BiomeCaveSpikeDefinition? caveSpike = null,
        BiomeDecorationDefinition? caveDecoration = null)
    {
        var surfaceBiome =
            new BiomeDefinition(
                "asteria:test/flat",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    32f,
                    0f,
                    64,
                    0f,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:block_grass",
                        1),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:dirt",
                        4),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var formations = additive?.ToArray() ??
            (floating is { } authored
                ? [authored]
                : Array.Empty<BiomeAdditiveDensityDefinition>());
        var volumeBiome =
            formations.Length > 0
                ? new BiomeDefinition(
                    "asteria:test/floating",
                    surfaceLayout: null,
                    surfaceTerrain: null,
                    [
                        new BiomeSurfaceLayerDefinition(
                            "asteria:block_grass",
                            1),
                        new BiomeSurfaceLayerDefinition(
                            "asteria:dirt",
                            4),
                        new BiomeSurfaceLayerDefinition(
                            "asteria:stone"),
                    ],
                    terrain3d:
                        new BiomeTerrain3dDefinition(
                            formations),
                    volumeLayout:
                        new BiomeVolumeLayoutDefinition())
                : null;
        var caveBiome =
            caveSpike is null
                ? null
                : new BiomeDefinition(
                    "asteria:test/caverns",
                    null, null,
                    surfaceLayers: [new BiomeSurfaceLayerDefinition("asteria:stone")],
                    volumeLayout: new BiomeVolumeLayoutDefinition(
                        placement: VolumeBiomePlacement.CarvedVoid),
                    caveSpikes: [caveSpike],
                    decorations: caveDecoration is null
                        ? null
                        : [caveDecoration]);
        var definitions =
            new[] { surfaceBiome, volumeBiome, caveBiome }
                .Where(definition => definition is not null)
                .Select(definition => definition!)
                .ToArray();
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    surfaceBiome.Id,
                ],
                64,
                18f,
                new DimensionSpawnDefinition(
                    0,
                    0),
                new DimensionEnvironmentDefinition(
                    new DimensionColor(
                        0,
                        0,
                        0),
                    new DimensionColor(
                        255,
                        255,
                        255),
                    1f,
                    new DimensionColor(
                        0,
                        0,
                        0),
                    0f),
                floorY.HasValue ||
                roofY.HasValue
                    ? new DimensionShellDefinition(
                        "asteria:shell_sphere",
                        floorY,
                        roofY)
                    : null,
                caves,
                volumeBiomes: new[]
                {
                    volumeBiome?.Id,
                    caveBiome?.Id,
                }.OfType<string>());

        return new BiomeWorldGenerator(
            8192UL,
            dimension,
            Blocks,
            new BiomeRegistry(
                definitions));
    }

    private static (int X, int Z) FindVolumeSolid(
        BiomeWorldGenerator generator,
        int y)
    {
        for (var z = -2048;
             z <= 2048;
             z += 16)
        {
            for (var x = -2048;
                 x <= 2048;
                 x += 16)
            {
                if (generator.VolumeBiomes.Sample(
                        x,
                        y,
                        z) is not null &&
                    generator.DensityAt(
                        x,
                        y,
                        z) >=
                    0d)
                {
                    return (
                        x,
                        z);
                }
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Could not find a solid volume biome sample at Y={y}.");
    }

}
