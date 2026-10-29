using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BiomeWorldGenerationTests
{
    [Fact]
    public void DefaultBiomePackParsesAndValidatesBlockReferences()
    {
        var blocks =
            LoadDefaultBlocks();
        var biomes =
            LoadDefaultBiomes();

        biomes.ValidateBlocks(
            blocks);

        var biomeDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "biomes");
        Assert.Equal(
            Directory.EnumerateFiles(
                    biomeDirectory,
                    "*.json")
                .Count(),
            biomes.Count);

        foreach (var biomeId in
                 new[]
                 {
                     "asteria:overworld/alps",
                     "asteria:overworld/arctic",
                     "asteria:overworld/caverns",
                     "asteria:overworld/enchanted_forest",
                     "asteria:overworld/floating_islands",
                     "asteria:overworld/gorge",
                     "asteria:overworld/mountain_belt",
                     "asteria:overworld/mountains",
                     "asteria:overworld/ocean",
                     "asteria:overworld/scorched_wastes",
                 })
        {
            _ =
                biomes.Get(
                    biomeId);
        }

        var swamp =
            biomes.Get(
                "asteria:overworld/swamp");

        Assert.Contains(
            "asteria:overworld/desert",
            swamp.SurfaceLayout!.CannotBorder);
        Assert.Contains(
            "asteria:overworld/wasteland",
            swamp.SurfaceLayout.CannotBorder);
        Assert.Contains(
            "asteria:overworld/mountains",
            swamp.SurfaceLayout.CannotBorder);
        Assert.Contains(
            swamp.Decorations,
            decoration =>
                decoration.Block ==
                "asteria:mushroom_brown");
        Assert.NotNull(swamp.Palette.SurfaceMosaic);
        Assert.Null(swamp.SurfaceLayers[0].Patch);

        foreach (var biomeId in new[]
                 {
                     "plains", "wasteland", "mountains", "gorge", "alps",
                     "arctic", "swamp", "mountain_belt",
                 })
        {
            var biome = biomes.Get($"asteria:overworld/{biomeId}");
            var pebble = Assert.Single(
                biome.Decorations,
                decoration => decoration.Block == "asteria:pebble");
            Assert.NotNull(pebble.Cluster);
        }

        Assert.DoesNotContain(biomes.Get("asteria:overworld/desert").Decorations,
            decoration => decoration.Block == "asteria:pebble");

        foreach (var biomeId in new[]
                 {
                     "asteria:overworld/plains",
                     "asteria:overworld/swamp",
                     "asteria:overworld/enchanted_forest",
                     "asteria:umbral/wraith_grove",
                 })
        {
            var biome = biomes.Get(biomeId);
            var stick = Assert.Single(
                biome.Decorations,
                decoration => decoration.Block == "asteria:stick");
            Assert.True(stick.Cluster is { Scale: >= 2 });
            Assert.NotEmpty(stick.SurfaceBlocks);
        }

        var caverns =
            biomes.Get(
                "asteria:overworld/caverns");
        Assert.Null(
            caverns.SurfaceLayout);
        Assert.NotNull(caverns.VolumeLayout);
        Assert.Equal(VolumeBiomePlacement.CarvedVoid,
            caverns.VolumeLayout.Placement);
        Assert.Equal("asteria:stone", caverns.SurfaceLayers[0].Block);
        Assert.NotNull(caverns.SurfaceLayers[0].Patch);
        Assert.Equal(6, caverns.CaveSpikes.Count);
        var stoneSpike = Assert.Single(caverns.CaveSpikes,
            spike => spike.Block == "asteria:spike_stone" &&
                spike.SurfaceBiomes.Count == 0);
        Assert.Empty(stoneSpike.SurfaceBiomes);
        Assert.Contains(CaveSpikeDirection.Up, stoneSpike.Directions);
        Assert.Contains(CaveSpikeDirection.Down, stoneSpike.Directions);
        Assert.Equal(1, stoneSpike.MinSpacing);
        Assert.NotNull(stoneSpike.Cluster);
        Assert.Equal(32, stoneSpike.Cluster.HorizontalScale);
        Assert.Equal(22, stoneSpike.Cluster.VerticalScale);
        var iceSpike = Assert.Single(caverns.CaveSpikes,
            spike => spike.Block == "asteria:spike_ice");
        Assert.Equal(
            ["asteria:overworld/alps", "asteria:overworld/arctic"],
            iceSpike.SurfaceBiomes);
        Assert.Equal(1, iceSpike.MinSpacing);
        Assert.NotNull(iceSpike.Cluster);
        Assert.Equal(24, iceSpike.Cluster.HorizontalScale);

        var floating =
            biomes.Get(
                "asteria:overworld/floating_islands");
        Assert.Null(
            floating.SurfaceLayout);
        Assert.Null(
            floating.SurfaceTerrain);
        Assert.NotNull(
            floating.VolumeLayout);
        Assert.NotNull(
            floating.Terrain3d?.Additive);
        Assert.Equal(
            200,
            Assert.Single(floating.Terrain3d!.Additive).MinY);
        Assert.Equal(
            280,
            floating.Terrain3d.Additive[0].MaxY);

        Assert.Equal(
            new[]
            {
                ("asteria:sand", (uint?)4),
                ("asteria:sandstone", (uint?)null),
            },
            biomes.Get(
                    "asteria:overworld/desert")
                .SurfaceLayers
                .Select(layer =>
                    (layer.Block, layer.Depth)));

        Assert.Equal(
            new[]
            {
                ("asteria:block_grass", (uint?)1),
                ("asteria:dirt", (uint?)4),
                ("asteria:stone", (uint?)null),
            },
            biomes.Get(
                    "asteria:umbral/umbral_reach")
                .SurfaceLayers
                .Select(layer =>
                    (layer.Block, layer.Depth)));

        Assert.Equal(
            new[]
            {
                ("asteria:dirt", (uint?)2),
                ("asteria:stone", (uint?)null),
            },
            biomes.Get(
                    "asteria:umbral/withered_waste")
                .SurfaceLayers
                .Select(layer =>
                    (layer.Block, layer.Depth)));

        Assert.Equal(
            new[]
            {
                ("asteria:block_grass", (uint?)1),
                ("asteria:mud", (uint?)4),
                ("asteria:stone", (uint?)null),
            },
            biomes.Get(
                    "asteria:umbral/wraith_grove")
                .SurfaceLayers
                .Select(layer =>
                    (layer.Block, layer.Depth)));
    }

    [Fact]
    public void CaveSpikeSurfaceBiomesMustReferenceActualSurfaceBiomes()
    {
        var cave = new BiomeDefinition(
            "asteria:test/caverns", null, null,
            volumeLayout: new BiomeVolumeLayoutDefinition(
                placement: VolumeBiomePlacement.CarvedVoid),
            surfaceLayers: [new BiomeSurfaceLayerDefinition("asteria:stone")],
            caveSpikes:
            [
                new BiomeCaveSpikeDefinition(
                    "asteria:spike_stone", 0.1f, 2, 5, 6,
                    [CaveSpikeDirection.Up],
                    ["asteria:test/unknown"]),
            ]);
        Assert.Throws<ArgumentException>(() => new BiomeRegistry([cave]));
    }

    [Fact]
    public void BiomeFieldIsIndependentFromRegistryInsertionOrder()
    {
        var definitions =
            StandardBiomeDefinitions();
        var forward =
            new BiomeField(
                77,
                TestDimension(
                    definitions.Select(
                        definition =>
                            definition.Id)),
                new BiomeRegistry(
                    definitions));
        var reverse =
            new BiomeField(
                77,
                TestDimension(
                    definitions.Select(
                        definition =>
                            definition.Id)),
                new BiomeRegistry(
                    definitions.Reverse()));

        for (var z = -640;
             z <= 640;
             z += 41)
        {
            for (var x = -640;
                 x <= 640;
                 x += 37)
            {
                var left =
                    forward.Sample(
                        x,
                        z);
                var right =
                    reverse.Sample(
                        x,
                        z);

                Assert.Equal(
                    left.Primary,
                    right.Primary);
                Assert.Equal(
                    left.Influences.ToArray(),
                    right.Influences.ToArray());
            }
        }
    }

    [Fact]
    public void BiomeAssignmentMemoEvictionAndConcurrentQueriesPreserveSemantics()
    {
        var definitions = StandardBiomeDefinitions();
        var dimension = TestDimension(definitions.Select(x => x.Id));
        var registry = new BiomeRegistry(definitions);
        var smallCache = new BiomeField(
            157UL, dimension, registry, assignmentCacheCapacity: 32);
        var regularCache = new BiomeField(
            157UL, dimension, registry);
        var coordinates = Enumerable.Range(0, 96)
            .Select(index => (
                X: -2048 + index * 47,
                Z: 1536 - index * 61))
            .ToArray();
        var actual = new BiomeSample[coordinates.Length];

        Parallel.For(
            0,
            coordinates.Length,
            index =>
            {
                var (x, z) = coordinates[index];
                actual[index] = smallCache.Sample(x, z);
            });

        for (var index = 0; index < coordinates.Length; index++)
        {
            var (x, z) = coordinates[index];
            var expected = regularCache.Sample(x, z);
            Assert.Equal(expected.Primary, actual[index].Primary);
            Assert.Equal(
                expected.Influences.ToArray(),
                actual[index].Influences.ToArray());

            var repeated = smallCache.Sample(x, z);
            Assert.Equal(actual[index].Primary, repeated.Primary);
            Assert.Equal(
                actual[index].Influences.ToArray(),
                repeated.Influences.ToArray());
        }
    }

    [Fact]
    public void BiomeInfluencesAreNormalizedAndContainPrimary()
    {
        var field =
            new BiomeField(
                91,
                TestDimension(
                    StandardBiomeDefinitions()
                        .Select(
                            definition =>
                                definition.Id)),
                new BiomeRegistry(
                    StandardBiomeDefinitions()));

        var grid =
            field.SampleGrid(
                -1024,
                -1024,
                width: 64,
                depth: 64,
                step: 32);

        for (var z = 0;
             z < grid.Depth;
             z++)
        {
            for (var x = 0;
                 x < grid.Width;
                 x++)
            {
                var sample =
                    grid[x, z];
                var total =
                    sample.Influences.Sum(
                        influence =>
                            influence.Weight);

                Assert.InRange(
                    total,
                    0.9999f,
                    1.0001f);
                Assert.Contains(
                    sample.Influences,
                    influence =>
                        influence.BiomeId ==
                        sample.Primary);
            }
        }
    }

    [Theory]
    [InlineData("asteria:overworld/desert", "asteria:overworld/swamp", false)]
    [InlineData("asteria:overworld/wasteland", "asteria:overworld/swamp", false)]
    [InlineData("asteria:overworld/wasteland", "asteria:overworld/ocean", true)]
    public void OverworldBoundaryRulesAreSymmetric(
        string first,
        string second,
        bool canBorder)
    {
        var biomes = LoadDefaultBiomes();
        var field = new BiomeField(
            123UL,
            [first, second],
            biomes);

        Assert.Equal(canBorder, field.AreCompatible(first, second));
        Assert.Equal(canBorder, field.AreCompatible(second, first));
    }

    [Fact]
    public void CannotBorderIsRespectedBySampledPrimaryBiomes()
    {
        var field =
            new BiomeField(
                123,
                TestDimension(
                    StandardBiomeDefinitions()
                        .Select(
                            definition =>
                                definition.Id)),
                new BiomeRegistry(
                    StandardBiomeDefinitions()));
        var grid =
            field.SampleGrid(
                -2048,
                -2048,
                width: 96,
                depth: 96,
                step: 32);

        for (var z = 0;
             z < grid.Depth;
             z++)
        {
            for (var x = 0;
                 x < grid.Width;
                 x++)
            {
                var sample =
                    grid[x, z];

                if (x + 1 <
                    grid.Width)
                {
                    Assert.True(
                        field.AreCompatible(
                            sample.Primary,
                            grid[
                                x + 1,
                                z].Primary));
                }

                if (z + 1 <
                    grid.Depth)
                {
                    Assert.True(
                        field.AreCompatible(
                            sample.Primary,
                            grid[
                                x,
                                z + 1].Primary));
                }
            }
        }
    }

    [Fact]
    public void SurfaceColumnBatchMatchesScalarAtNegativeCoordinates()
    {
        var definitions = StandardBiomeDefinitions();
        var dimension = TestDimension(definitions.Select(x => x.Id));
        var registry =
            new BiomeRegistry(
                definitions);
        var biomes =
            new BiomeField(
                947UL,
                dimension,
                registry);
        var volumes =
            new VolumeBiomeField(
                947UL,
                dimension,
                registry);
        var terrain =
            new SurfaceTerrainField(
                947UL,
                dimension,
                biomes,
                volumes,
                new GeneratedFluidField(
                    947UL,
                    dimension,
                    new FluidRegistry(
                        Array.Empty<FluidDefinition>())),
                definitions,
                Array.Empty<BiomeDefinition>());
        var column = terrain.SampleColumn(-2, 1);
        var originX = -2 * Chunk.Size;
        var originZ = Chunk.Size;
        var minimum = int.MaxValue;
        var maximum = int.MinValue;

        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                var worldX = originX + x;
                var worldZ = originZ + z;
                var height = terrain.SurfaceHeight(worldX, worldZ);
                Assert.Equal(height, column.HeightAt(x, z));
                Assert.Equal(
                    biomes.Sample(worldX, worldZ).Primary,
                    column.BiomeAt(x, z).Primary);
                minimum = Math.Min(minimum, height);
                maximum = Math.Max(maximum, height);
            }
        }

        Assert.Equal(minimum, column.Range.MinimumWorldY);
        Assert.Equal(maximum, column.Range.MaximumWorldY);
    }

    [Fact]
    public void BiomeMeshTintGridReusesWorldSpaceSamplesAcrossChunkSeams()
    {
        var definitions = StandardBiomeDefinitions();
        var dimension = TestDimension(definitions.Select(x => x.Id));
        var registry =
            new BiomeRegistry(
                definitions);
        var biomes =
            new BiomeField(
                157UL,
                dimension,
                registry);
        var volumes =
            new VolumeBiomeField(
                157UL,
                dimension,
                registry);
        var terrain =
            new SurfaceTerrainField(
                157UL,
                dimension,
                biomes,
                volumes,
                new GeneratedFluidField(
                    157UL,
                    dimension,
                    new FluidRegistry(
                        Array.Empty<FluidDefinition>())),
                definitions,
                Array.Empty<BiomeDefinition>());
        var columns = new SurfaceTerrainColumnCache(
            terrain, capacity: 4);
        var grid = columns.SampleChunkBiomes(-2, 1);
        var direct = biomes.SampleGrid(
            -2 * Chunk.Size,
            Chunk.Size,
            Chunk.Size + 1,
            Chunk.Size + 1);

        Assert.Equal(4, columns.CachedColumnCount);
        for (var z = 0; z <= Chunk.Size; z++)
        {
            for (var x = 0; x <= Chunk.Size; x++)
            {
                Assert.Equal(
                    direct[x, z].Primary,
                    grid[x, z].Primary);
                Assert.Equal(
                    direct[x, z].Influences.ToArray(),
                    grid[x, z].Influences.ToArray());
            }
        }
    }

    [Fact]
    public void SurfaceColumnCacheIsBoundedAndEvictionCannotChangeResults()
    {
        var definitions = new[] { TestBiome("asteria:test/only") };
        var dimension = TestDimension(definitions.Select(x => x.Id));
        var registry =
            new BiomeRegistry(
                definitions);
        var biomes =
            new BiomeField(
                17UL,
                dimension,
                registry);
        var volumes =
            new VolumeBiomeField(
                17UL,
                dimension,
                registry);
        var terrain =
            new SurfaceTerrainField(
                17UL,
                dimension,
                biomes,
                volumes,
                new GeneratedFluidField(
                    17UL,
                    dimension,
                    new FluidRegistry(
                        Array.Empty<FluidDefinition>())),
                definitions,
                Array.Empty<BiomeDefinition>());
        var cache = new SurfaceTerrainColumnCache(terrain, capacity: 2);
        var first = cache.Get(-1, 0);
        var second = cache.Get(0, 0);

        Assert.Same(first, cache.Get(-1, 0));
        _ = cache.Get(1, 0);
        Assert.Equal(2, cache.CachedColumnCount);
        Assert.Same(first, cache.Get(-1, 0));

        var restored = cache.Get(0, 0);
        Assert.NotSame(second, restored);
        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                Assert.Equal(
                    second.HeightAt(x, z),
                    restored.HeightAt(x, z));
                Assert.Equal(
                    second.BiomeAt(x, z).Influences.ToArray(),
                    restored.BiomeAt(x, z).Influences.ToArray());
            }
        }
    }

    [Fact]
    public void PlainsKeepsShallowGrassCoverAndUsesThreeDimensionalGeology()
    {
        var blocks = LoadDefaultBlocks();
        var plains = LoadDefaultBiomes().Get("asteria:overworld/plains");
        Assert.Null(plains.SurfaceLayers[0].Patch);
        Assert.Equal(5u, Assert.IsType<BiomeGeologyDefinition>(plains.Geology).SurfaceCoverDepth);

        var first = new BiomeSurfaceMaterialField(112233, [plains], blocks);
        var second = new BiomeSurfaceMaterialField(112233, [plains], blocks);
        var sample = new BiomeSample(plains.Id,
            [new BiomeInfluence(plains.Id, 1f)]);
        var grass = blocks.GetId("asteria:block_grass");
        var seen = new HashSet<BlockRuntimeId>();
        var exposedSurfaceDeposits = new HashSet<BlockRuntimeId>();

        for (var z = -384; z <= 384; z += 8)
        for (var x = -384; x <= 384; x += 8)
        {
            Assert.Equal(grass, first.BlockAt(sample, x, z, 0));
            Assert.True(first.TryGeologyBlockAt(sample, x, 30, z, out var block));
            Assert.True(second.TryGeologyBlockAt(sample, x, 30, z, out var repeated));
            Assert.Equal(block, repeated);
            seen.Add(block);

            // Exercise the authored Plains surface rule at a coarser spatial
            // interval without tying it to terrain altitude or material depth.
            if (x % 24 == 0 && z % 24 == 0 &&
                first.TrySurfaceGeologyBlockAt(sample, x, 4, z, out var exposed))
                exposedSurfaceDeposits.Add(exposed);
        }

        Assert.Contains(blocks.GetId("asteria:dirt"), exposedSurfaceDeposits);
        Assert.Contains(blocks.GetId("asteria:gravel"), exposedSurfaceDeposits);
        Assert.Contains(blocks.GetId("asteria:clay"), exposedSurfaceDeposits);
        Assert.Contains(blocks.GetId("asteria:stone"), seen);
        Assert.Contains(blocks.GetId("asteria:dirt"), seen);
        Assert.Contains(blocks.GetId("asteria:gravel"), seen);
        Assert.All(plains.Decorations.Where(decoration =>
            decoration.Block is "asteria:pebble" or "asteria:stick"),
            decoration => Assert.Contains("asteria:gravel", decoration.SurfaceBlocks));
    }

    [Fact]
    public void MaterializedFlatPlainsSurfaceShowsGeologicalVariation()
    {
        const ulong seed = 112233UL;
        var blocks = LoadDefaultBlocks();
        var fluids = LoadDefaultFluids();
        var biomes = LoadDefaultBiomes();
        var dimensions = LoadDefaultDimensions();
        var plains = biomes.Get("asteria:overworld/plains");
        var dimension = dimensions.Get(DimensionId.Overworld);
        var generator = new BiomeWorldGenerator(
            DimensionSeed.Derive(seed, dimension.Id),
            dimension, blocks, fluids, biomes,
            LoadDefaultStructures(), LoadDefaultStructureSets(),
            new WorldGenerationOptions(
                mode: WorldGenerationMode.Flat,
                spawnBiome: plains.Id,
                spawnStructures: false,
                singleBiome: true,
                spawnCaves: false,
                spawnOceans: false));

        var grass = blocks.GetId("asteria:block_grass");
        var exposedMaterials = new HashSet<BlockRuntimeId>();
        var chunks = new Dictionary<ChunkCoord, Chunk>();

        // Inspect the actual top voxel from materialized flat-world chunks,
        // not only the geology field in isolation.
        for (var z = -32; z < 32; z++)
        for (var x = -32; x < 32; x++)
        {
            var surfaceY = generator.SurfaceHeight(x, z);
            var (coord, local) = VoxelCoordinates.FromWorld(x, surfaceY, z);
            if (!chunks.TryGetValue(coord, out var chunk))
            {
                chunk = generator.Materialize(coord);
                chunks.Add(coord, chunk);
            }

            exposedMaterials.Add(chunk.GetBlock(local.X, local.Y, local.Z));
        }

        Assert.Contains(grass, exposedMaterials);
        Assert.Contains(blocks.GetId("asteria:dirt"), exposedMaterials);
        Assert.Contains(blocks.GetId("asteria:gravel"), exposedMaterials);
        Assert.Contains(blocks.GetId("asteria:clay"), exposedMaterials);
        Assert.True(exposedMaterials.Count >= 4,
            "The actual flat Plains surface should show the authored geological variation.");
    }

    [Fact]
    public void DefaultPlainsGeologyKeepsMaterialVarietyAcrossAltitudeSlices()
    {
        var blocks = LoadDefaultBlocks();
        var plains = LoadDefaultBiomes().Get("asteria:overworld/plains");
        var field = new BiomeSurfaceMaterialField(0xA57E_3D10_2026UL, [plains], blocks);
        var sample = new BiomeSample(
            plains.Id, [new BiomeInfluence(plains.Id, 1f)]);
        var expectedMaterials = new[]
        {
            blocks.GetId("asteria:stone"),
            blocks.GetId("asteria:dirt"),
            blocks.GetId("asteria:gravel"),
            blocks.GetId("asteria:clay"),
        };
        var altitudeSlices = new[] { 12, 28, 44, 60, 76, 92 };
        var distributions = new Dictionary<int, Dictionary<BlockRuntimeId, double>>();

        foreach (var y in altitudeSlices)
        {
            var counts = expectedMaterials.ToDictionary(block => block, _ => 0);
            for (var z = -512; z <= 512; z += 16)
            for (var x = -512; x <= 512; x += 16)
            {
                Assert.True(field.TryGeologyBlockAt(sample, x, y, z, out var block));
                Assert.True(counts.ContainsKey(block),
                    $"Unexpected geology block {blocks.GetDefinition(block).Id} at Y={y}.");
                counts[block]++;
            }

            var total = counts.Values.Sum();
            Assert.Equal(expectedMaterials.Length, counts.Count(pair => pair.Value > 0));
            distributions[y] = counts.ToDictionary(
                pair => pair.Key, pair => pair.Value / (double)total);
        }

        // A world-height threshold would collapse at least one slice to a
        // predictable material. The 3D geology contract requires all authored
        // materials to remain represented at every sampled altitude.
        foreach (var material in expectedMaterials)
        {
            var shares = distributions.Values.Select(slice => slice[material]).ToArray();
            Assert.True(
                shares.Max() - shares.Min() < 0.30,
                $"Material {blocks.GetDefinition(material).Id} varies too strongly by altitude: " +
                $"{shares.Min():P1}..{shares.Max():P1}.");
        }
    }

    [Fact]
    public void AuthoredMountainWallPaletteTakesPrecedenceOverThreeDimensionalGeology()
    {
        var blocks = LoadDefaultBlocks();
        var mountains = LoadDefaultBiomes().Get("asteria:overworld/mountains");
        var field = new BiomeSurfaceMaterialField(445566UL, [mountains], blocks);
        var sample = new BiomeSample(
            mountains.Id, [new BiomeInfluence(mountains.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");
        var geologyProducedAlternative = false;

        Assert.True(field.HasDirectionalOverride(sample, BiomePaletteFace.Walls));

        for (var y = 12; y <= 60; y += 8)
        for (var z = -96; z <= 96; z += 8)
        for (var x = -96; x <= 96; x += 8)
        {
            Assert.True(field.TryGeologyBlockAt(sample, x, y, z, out var geology));
            geologyProducedAlternative |= geology != stone;
            Assert.Equal(stone, field.VolumeBlockAt(
                sample, x, y, z, BiomePaletteFace.Walls));
        }

        Assert.True(geologyProducedAlternative,
            "The test seed did not exercise any non-stone geological material.");
    }

    [Fact]
    public void MaterializedGeologyFollowsSurfaceBiomeAcrossABiomeTransition()
    {
        const ulong seed = 0xA57E_2026UL;
        const int sampleStep = 16;
        var blocks = LoadDefaultBlocks();
        var dimension = LoadDefaultDimensions().Get(DimensionId.Overworld);
        var biomes = LoadDefaultBiomes();
        var generator = new BiomeWorldGenerator(
            DimensionSeed.Derive(seed, dimension.Id),
            dimension, blocks, LoadDefaultFluids(), biomes,
            LoadDefaultStructures(), LoadDefaultStructureSets(),
            new WorldGenerationOptions(spawnStructures: false));
        var geologyBiomeIds = dimension.SurfaceBiomes
            .Where(id => biomes.Get(id).Geology is not null &&
                id != "asteria:overworld/ocean")
            .ToHashSet(StringComparer.Ordinal);
        var geologyField = new BiomeSurfaceMaterialField(
            DimensionSeed.Derive(seed, dimension.Id),
            geologyBiomeIds.Select(biomes.Get),
            blocks);

        (int X, int Y, int Z, BlockRuntimeId Expected)? leftVoxel = null;
        (int X, int Y, int Z, BlockRuntimeId Expected)? rightVoxel = null;
        for (var z = -2048; z <= 2048 && leftVoxel is null; z += sampleStep)
        {
            for (var x = -2048; x < 2048; x += sampleStep)
            {
                var leftSample = generator.Biomes.Sample(x, z);
                var rightSample = generator.Biomes.Sample(x + sampleStep, z);
                if (leftSample.Primary == rightSample.Primary ||
                    !geologyBiomeIds.Contains(leftSample.Primary) ||
                    !geologyBiomeIds.Contains(rightSample.Primary))
                    continue;

                if (!TryFindVoxel(leftSample, x, z, out var left) ||
                    !TryFindVoxel(rightSample, x + sampleStep, z, out var right))
                    continue;

                leftVoxel = left;
                rightVoxel = right;
                break;
            }
        }

        Assert.True(leftVoxel.HasValue && rightVoxel.HasValue,
            "No suitable boundary between two geology-enabled surface biomes was found.");
        var leftCheck = leftVoxel!.Value;
        var leftActual = ReadMaterializedBlock(leftCheck.X, leftCheck.Y, leftCheck.Z);
        Assert.True(leftCheck.Expected == leftActual,
            $"Left geology mismatch at ({leftCheck.X},{leftCheck.Y},{leftCheck.Z}); " +
            $"surface={generator.SurfaceHeight(leftCheck.X, leftCheck.Z)}, " +
            $"primary={generator.Biomes.Sample(leftCheck.X, leftCheck.Z).Primary}, " +
            $"volumeAtVoxel={generator.VolumeBiomes.Sample(leftCheck.X, leftCheck.Y, leftCheck.Z)?.Primary ?? "none"}, " +
            $"expected={blocks.GetDefinition(leftCheck.Expected).Id}, actual={blocks.GetDefinition(leftActual).Id}.");

        var rightCheck = rightVoxel.Value;
        var rightActual = ReadMaterializedBlock(rightCheck.X, rightCheck.Y, rightCheck.Z);
        Assert.True(rightCheck.Expected == rightActual,
            $"Right geology mismatch at ({rightCheck.X},{rightCheck.Y},{rightCheck.Z}); " +
            $"surface={generator.SurfaceHeight(rightCheck.X, rightCheck.Z)}, " +
            $"primary={generator.Biomes.Sample(rightCheck.X, rightCheck.Z).Primary}, " +
            $"volumeAtVoxel={generator.VolumeBiomes.Sample(rightCheck.X, rightCheck.Y, rightCheck.Z)?.Primary ?? "none"}, " +
            $"expected={blocks.GetDefinition(rightCheck.Expected).Id}, actual={blocks.GetDefinition(rightActual).Id}.");

        bool TryFindVoxel(
            BiomeSample sample,
            int x,
            int z,
            out (int X, int Y, int Z, BlockRuntimeId Expected) voxel)
        {
            var cover = Assert.IsType<BiomeGeologyDefinition>(
                biomes.Get(sample.Primary).Geology).SurfaceCoverDepth;
            var surfaceY = generator.SurfaceHeight(x, z);
            for (var depth = checked((int)cover + 2);
                 depth <= cover + 16;
                 depth++)
            {
                var y = surfaceY - depth;
                if (y < 2 ||
                    generator.VolumeBiomes.Sample(x, y, z) is not null ||
                    !IsInteriorSolid(x, y, z))
                    continue;
                if (geologyField.TryGeologyBlockAt(sample, x, y, z, out var expected))
                {
                    voxel = (x, y, z, expected);
                    return true;
                }
            }

            voxel = default;
            return false;
        }

        bool IsInteriorSolid(int x, int y, int z)
        {
            if (generator.DensityAt(x, y, z) < 0d ||
                generator.IsCaveVoidAt(x, y, z))
                return false;

            return !generator.IsCaveVoidAt(x - 1, y, z) &&
                !generator.IsCaveVoidAt(x + 1, y, z) &&
                !generator.IsCaveVoidAt(x, y - 1, z) &&
                !generator.IsCaveVoidAt(x, y + 1, z) &&
                !generator.IsCaveVoidAt(x, y, z - 1) &&
                !generator.IsCaveVoidAt(x, y, z + 1);
        }

        BlockRuntimeId ReadMaterializedBlock(int x, int y, int z)
        {
            var (chunkCoord, local) = VoxelCoordinates.FromWorld(x, y, z);
            return generator.Materialize(chunkCoord).GetBlock(
                local.X, local.Y, local.Z);
        }
    }

    [Fact]
    public void DeepMaterialCoreBypassesSurfacePatchSelectionWithoutChangingBlock()
    {
        var blocks = LoadDefaultBlocks();
        var biomes = LoadDefaultBiomes();
        var materialField = new BiomeSurfaceMaterialField(
            123UL,
            [
                biomes.Get("asteria:overworld/plains"),
                biomes.Get("asteria:overworld/swamp"),
                biomes.Get("asteria:overworld/wasteland"),
            ],
            blocks);

        foreach (var biomeId in new[]
        {
            "asteria:overworld/plains",
            "asteria:overworld/swamp",
            "asteria:overworld/wasteland",
        })
        {
            var sample = new BiomeSample(
                biomeId,
                [new BiomeInfluence(biomeId, 1f)]);
            var core = materialField.CoreLayer(sample);
            Assert.Equal(blocks.GetId("asteria:stone"), core.Block);
            Assert.True(core.StartDepth > 0u);

            for (var z = -32; z <= 32; z += 16)
            {
                for (var x = -32; x <= 32; x += 16)
                {
                    for (uint depth = core.StartDepth;
                         depth < core.StartDepth + 24u; depth++)
                    {
                        Assert.Equal(core.Block, materialField.BlockAt(
                            sample, x, z, depth));
                    }
                }
            }
        }
    }

    [Fact]
    public void WastelandDirtHasNoBiomeTintOrTextureOverlay()
    {
        var blocks = LoadDefaultBlocks();
        var dirt = blocks.GetDefinition(blocks.GetId("asteria:dirt"));
        var wasteland = LoadDefaultBiomes().Get("asteria:overworld/wasteland");

        Assert.Equal(BlockTint.None, dirt.Tint);
        Assert.Single(dirt.Textures.ResolveForFace(BlockFace.Top));
        Assert.Equal("asteria:dirt", wasteland.SurfaceLayers[0].Block);
        Assert.Equal(2u, wasteland.SurfaceLayers[0].Depth);
    }

    [Fact]
    public void ResolvedMaterialColumnMatchesScalarDepthAndPatchQueries()
    {
        var blocks = LoadDefaultBlocks();
        var biomes = LoadDefaultBiomes();
        var materials = new BiomeSurfaceMaterialField(
            123UL,
            [
                biomes.Get("asteria:overworld/swamp"),
            ],
            blocks);
        var sample = new BiomeSample(
            "asteria:overworld/swamp",
            [
                new BiomeInfluence("asteria:overworld/swamp", 1f),
            ]);

        for (var z = -17; z <= 17; z += 17)
        {
            for (var x = -17; x <= 17; x += 17)
            {
                var context = new SurfacePlacementContext(90, 1d);
                var column = materials.SampleColumn(
                    sample, x, z, context);
                for (uint depth = 0; depth < 80; depth++)
                {
                    Assert.Equal(
                        materials.BlockAt(sample, x, z, depth, context),
                        column.BlockAt(depth));
                }
            }
        }
    }

    [Fact]
    public void TerrainHeightRemainsContinuousAcrossNegativeNoiseLatticeBoundary()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/noisy",
                new BiomeSurfaceLayoutDefinition(
                    regionMin: 192,
                    regionMax: 192),
                new BiomeTerrainDefinition(
                    baseHeightOffset: 64f,
                    macroAmplitude: 48f,
                    macroScale: 64,
                    detailAmplitude: 0f,
                    detailScale: 32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var generator =
            new BiomeWorldGenerator(
                91,
                TestDimension(
                [
                    biome.Id,
                ]),
                blocks,
                new BiomeRegistry(
                [
                    biome,
                ]));

        var left =
            generator.SurfaceHeight(
                -65,
                17);
        var boundary =
            generator.SurfaceHeight(
                -64,
                17);
        var right =
            generator.SurfaceHeight(
                -63,
                17);

        Assert.InRange(
            Math.Abs(
                boundary -
                left),
            0,
            2);
        Assert.InRange(
            Math.Abs(
                right -
                boundary),
            0,
            2);
    }

    [Theory]
    [InlineData("asteria:overworld/plains", "asteria:leaf_oak")]
    [InlineData("asteria:overworld/swamp", "asteria:leaf_willow")]
    public void AuthoredTreeLeavesReceiveBiomeTintInRenderedMesh(
        string biomeId,
        string leafId)
    {
        var blocks = LoadDefaultBlocks();
        var biomes = LoadDefaultBiomes();
        var biome = biomes.Get(biomeId);
        var leaf = blocks.GetDefinition(blocks.GetId(leafId));

        Assert.Equal(BlockTint.Leaf, leaf.Tint);
        Assert.NotEmpty(leaf.Textures.AllLayers());
        Assert.All(leaf.Textures.AllLayers(),
            layer => Assert.True(layer.Dyable));

        var field = new BiomeField(17UL, [biomeId], biomes);
        var tintGrid = new BiomeTintField(
            field, [biome]).SampleGrid(0, 0, Chunk.Size + 1, Chunk.Size + 1);
        var expected = tintGrid.Resolve(
            BlockTint.Leaf, leaf.PreviewColor, 2, 2);

        var chunk = new Chunk();
        chunk.SetBlock(2, 2, 2, blocks.GetId(leafId));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var layer = Assert.Single(
            leaf.Textures.ResolveForFace(BlockFace.Top));
        var mesh = ChunkMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks,
            new TerrainTextureLookup(
                new Dictionary<string, int>
                {
                    [layer.Texture] = 1,
                }),
            meshletIndex: 0,
            tintSamples: tintGrid);
        var vertices = mesh.RenderBatches
            .SelectMany(batch => batch.Vertices)
            .ToArray();

        Assert.NotEmpty(vertices);
        Assert.All(vertices, vertex =>
        {
            Assert.InRange(
                MathF.Abs(vertex.Tint.X - expected.X), 0f, 0.02f);
            Assert.InRange(
                MathF.Abs(vertex.Tint.Y - expected.Y), 0f, 0.02f);
            Assert.InRange(
                MathF.Abs(vertex.Tint.Z - expected.Z), 0f, 0.02f);
            // Texture code includes tint and wind flags: 0.25 + 0.5.
            var encodedFlags = vertex.EncodedTextureLayers.X -
                MathF.Floor(vertex.EncodedTextureLayers.X);
            Assert.InRange(encodedFlags, 0.74f, 0.76f);
        });
    }

    [Fact]
    public void MyceloraLeavesStayIntentionallyUntinted()
    {
        var blocks = LoadDefaultBlocks();
        var leaf = blocks.GetDefinition(blocks.GetId("asteria:leaf_mycelora"));
        Assert.Equal(BlockTint.None, leaf.Tint);
        Assert.All(leaf.Textures.AllLayers(), layer => Assert.False(layer.Dyable));
    }

    [Fact]
    public void OakFoliageDoesNotLosePlainsTintNearUncoloredMountains()
    {
        var biomes = LoadDefaultBiomes();
        var blocks = LoadDefaultBlocks();
        var plains = biomes.Get("asteria:overworld/plains");
        var mountains = biomes.Get("asteria:overworld/mountains");
        var oak = blocks.GetDefinition(blocks.GetId("asteria:leaf_oak"));
        Assert.Equal(BlockTint.Leaf, oak.Tint);
        Assert.Null(mountains.Tints.Leaf);
        var expected = plains.Tints.Leaf!.Value;

        var layout = new BiomeField(
            17UL, [plains.Id, mountains.Id], biomes);
        var tints = new BiomeTintField(
            layout, [plains, mountains]);
        var found = false;

        for (var z = -1024; z <= 1024 && !found; z += 16)
        {
            for (var x = -1024; x <= 1024; x += 16)
            {
                var biome = layout.Sample(x, z);
                if (biome.Primary != plains.Id ||
                    !biome.Influences.Any(influence =>
                        influence.BiomeId == mountains.Id &&
                        influence.Weight > 0f))
                {
                    continue;
                }

                var resolved = tints.SampleGrid(x, z, 1, 1).Resolve(
                    BlockTint.Leaf, oak.PreviewColor, x, z);
                Assert.InRange(
                    MathF.Abs(resolved.X - expected.Red / 255f), 0f, 0.001f);
                Assert.InRange(
                    MathF.Abs(resolved.Y - expected.Green / 255f), 0f, 0.001f);
                Assert.InRange(
                    MathF.Abs(resolved.Z - expected.Blue / 255f), 0f, 0.001f);
                found = true;
                break;
            }
        }

        Assert.True(found, "Test seed should expose a Plains/Mountains biome blend.");
    }

    [Fact]
    public void UncoloredBiomeRetainsLegacyTintFallback()
    {
        var biomes = LoadDefaultBiomes();
        var mountain = biomes.Get("asteria:overworld/mountains");
        var layout = new BiomeField(17UL, [mountain.Id], biomes);
        var tints = new BiomeTintField(layout, [mountain]);
        var fallback = new BlockPreviewColor(12, 64, 178);
        var actual = tints.SampleGrid(0, 0, 1, 1).Resolve(
            BlockTint.Leaf, fallback, 0, 0);

        Assert.InRange(MathF.Abs(actual.X - 12f / 255f), 0f, 0.001f);
        Assert.InRange(MathF.Abs(actual.Y - 64f / 255f), 0f, 0.001f);
        Assert.InRange(MathF.Abs(actual.Z - 178f / 255f), 0f, 0.001f);
    }

    [Fact]
    public void BiomeTintBlendsAuthoredInfluenceColors()
    {
        var first =
            new BiomeDefinition(
                "asteria:test/first",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0,
                    0,
                    64,
                    0,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ],
                tints:
                    new BiomeTintPaletteDefinition(
                        grass:
                            new BiomeTintColor(
                                255,
                                0,
                                0)));
        var second =
            new BiomeDefinition(
                "asteria:test/second",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0,
                    0,
                    64,
                    0,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ],
                tints:
                    new BiomeTintPaletteDefinition(
                        grass:
                            new BiomeTintColor(
                                0,
                                0,
                                255)));
        var field =
            new BiomeField(
                57,
                TestDimension(
                [
                    first.Id,
                    second.Id,
                ]),
                new BiomeRegistry(
                [
                    first,
                    second,
                ]));
        var tints =
            new BiomeTintField(
                field,
                [
                    first,
                    second,
                ]);
        var found = false;

        for (var z = -1024;
             z <= 1024 &&
             !found;
             z += 4)
        {
            for (var x = -1024;
                 x <= 1024;
                 x += 4)
            {
                var sample =
                    field.Sample(
                        x,
                        z);

                if (sample.Influences.Count <= 1)
                {
                    continue;
                }

                var color =
                    tints.SampleGrid(
                            x,
                            z,
                            1,
                            1)
                        .Resolve(
                            BlockTint.Grass,
                            new BlockPreviewColor(
                                0,
                                255,
                                0),
                            x,
                            z);

                Assert.True(
                    color.X > 0f);
                Assert.True(
                    color.Z > 0f);
                Assert.True(
                    color.Y < 0.001f);
                found = true;
                break;
            }
        }

        Assert.True(
            found);
    }

    [Fact]
    public void SurfaceMaterialBelongsToPrimaryBiomeNotInfluenceWeight()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:block_grass"),
                new BlockDefinition(
                    "asteria:sand"),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var primary =
            FlatBiome(
                "asteria:test/primary",
                "asteria:block_grass",
                "asteria:stone");
        var secondary =
            FlatBiome(
                "asteria:test/secondary",
                "asteria:sand",
                "asteria:stone");
        var materials =
            new BiomeSurfaceMaterialField(
                17,
                [
                    primary,
                    secondary,
                ],
                blocks);
        var sample =
            new BiomeSample(
                primary.Id,
                [
                    new BiomeInfluence(
                        secondary.Id,
                        0.99f),
                    new BiomeInfluence(
                        primary.Id,
                        0.01f),
                ]);

        Assert.Equal(
            blocks.GetId(
                "asteria:block_grass"),
            materials.BlockAt(
                sample,
                120,
                -45,
                depth: 0));
    }

    [Fact]
    public void SurfaceMaterialUsesNoiseWarpedFiniteLayerBoundariesThenCore()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:block_grass"),
                new BlockDefinition(
                    "asteria:dirt"),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/layers",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0,
                    0,
                    64,
                    0,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:block_grass",
                        depth: 1,
                        boundaryNoise: 1),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:dirt",
                        depth: 4,
                        boundaryNoise: 2),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var materials =
            new BiomeSurfaceMaterialField(
                21,
                [
                    biome,
                ],
                blocks);
        var sample =
            new BiomeSample(
                biome.Id,
                [
                    new BiomeInfluence(
                        biome.Id,
                        1f),
                ]);

        var grass = blocks.GetId("asteria:block_grass");
        var dirt = blocks.GetId("asteria:dirt");
        var stone = blocks.GetId("asteria:stone");
        var grassBoundaryMaterials = new HashSet<BlockRuntimeId>();
        var stoneBoundaryMaterials = new HashSet<BlockRuntimeId>();

        for (var z = 0; z <= 96; z += 4)
        {
            for (var x = 0; x <= 96; x += 4)
            {
                var column = materials.SampleColumn(sample, x, z);
                for (uint depth = 0; depth <= 12; depth++)
                {
                    Assert.Equal(
                        materials.BlockAt(sample, x, z, depth),
                        column.BlockAt(depth));
                }

                grassBoundaryMaterials.Add(column.BlockAt(1));
                stoneBoundaryMaterials.Add(column.BlockAt(5));
                Assert.Equal(stone, column.BlockAt(12));
            }
        }

        Assert.Contains(grass, grassBoundaryMaterials);
        Assert.Contains(dirt, grassBoundaryMaterials);
        Assert.Contains(dirt, stoneBoundaryMaterials);
        Assert.Contains(stone, stoneBoundaryMaterials);
    }

    [Fact]
    public void WeightedSurfacePatchesStayDeterministicAndPreserveScalarColumnParity()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:block_grass"),
            new BlockDefinition("asteria:mud"),
            new BlockDefinition("asteria:gravel"),
            new BlockDefinition("asteria:stone"),
        ]);

        BiomeDefinition PatchBiome(
            IReadOnlyDictionary<string, double>? weights = null,
            double warp = 0d,
            double stretch = 1d) =>
            new(
                "asteria:test/weighted",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(0, 0, 64, 0, 32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:block_grass", 1,
                        new BiomeSurfacePatchDefinition(
                            36, 1f, 0.25f,
                            ["asteria:mud", "asteria:gravel"],
                            detailScale: 11,
                            selectionScale: 48,
                            warpScale: 52,
                            warpStrength: warp,
                            stretchZ: stretch,
                            weights: weights)),
                    new BiomeSurfaceLayerDefinition("asteria:stone"),
                ]);
        var uniform = new BiomeSurfaceMaterialField(
            47, [PatchBiome()], blocks);
        var weighted = new BiomeSurfaceMaterialField(
            47,
            [PatchBiome(new Dictionary<string, double>
            {
                ["asteria:mud"] = 9d,
                ["asteria:gravel"] = 1d,
            })],
            blocks);
        var warped = new BiomeSurfaceMaterialField(
            47, [PatchBiome(warp: 14d, stretch: 1.7d)], blocks);
        var sample = new BiomeSample(
            "asteria:test/weighted",
            [new BiomeInfluence("asteria:test/weighted", 1f)]);
        var gravel = blocks.GetId("asteria:gravel");
        var weightedGravel = 0;
        var uniformGravel = 0;
        var changedByWarp = 0;

        for (var z = -96; z <= 96; z += 4)
        {
            for (var x = -96; x <= 96; x += 4)
            {
                var original = uniform.BlockAt(sample, x, z, 0);
                var chosen = weighted.BlockAt(sample, x, z, 0);
                var distorted = warped.BlockAt(sample, x, z, 0);
                if (chosen == gravel)
                {
                    Assert.Equal(gravel, original);
                    weightedGravel++;
                }

                uniformGravel += original == gravel ? 1 : 0;
                changedByWarp += original != distorted ? 1 : 0;
                Assert.Equal(chosen,
                    weighted.SampleColumn(sample, x, z).BlockAt(0));
            }
        }

        Assert.True(weightedGravel < uniformGravel);
        Assert.True(changedByWarp > 0);
    }

    [Fact]
    public void SurfacePatchShapeAndWeightsParseAndValidate()
    {
        var biome = BiomeDefinitionJson.Parse(
            """
            {
              "id":"asteria:test/patch",
              "surfaceLayout":{},
              "surfaceTerrain":{
                "baseHeightOffset":0,"macroAmplitude":0,
                "macroScale":64,"detailAmplitude":0,"detailScale":32
              },
              "palette":{"default":[
                {"block":"asteria:block_grass","depth":1,
                 "patch":{"scale":32,"coverage":0.5,"roughness":0.2,
                          "detailScale":9,"selectionScale":64,
                          "warpScale":60,"warpStrength":12,"stretchZ":1.5,
                          "blocks":["asteria:mud","asteria:gravel"],
                          "weights":{"asteria:mud":4,"asteria:gravel":1}}},
                {"block":"asteria:stone"}
              ]}
            }
            """);

        var patch = biome.SurfaceLayers[0].Patch!;
        Assert.Equal(9u, patch.DetailScale);
        Assert.Equal(64u, patch.SelectionScale);
        Assert.Equal(60u, patch.WarpScale);
        Assert.Equal(12d, patch.WarpStrength);
        Assert.Equal(1.5d, patch.StretchZ);
        Assert.Equal(new[] { 4d, 1d }, patch.BlockWeights);

        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeSurfacePatchDefinition(
                32, 0.5f, 0.2f, ["asteria:mud"],
                weights: new Dictionary<string, double>
                {
                    ["asteria:unknown"] = 2d,
                }));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeSurfacePatchDefinition(
                32, 0.5f, 0.2f, ["asteria:mud"],
                weights: new Dictionary<string, double>
                {
                    ["asteria:mud"] = double.NaN,
                }));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeSurfacePatchDefinition(
                32, 0.5f, 0.2f, ["asteria:mud"],
                stretchZ: 0d));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeSurfacePatchDefinition(
                32, 0.5f, 0.2f, ["asteria:mud"],
                detailScale: 1));
    }

    [Fact]
    public void SurfacePatchUsesOrganicDeterministicWorldSpaceNoise()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:block_grass"),
                new BlockDefinition(
                    "asteria:mud"),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/patch",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0,
                    0,
                    64,
                    0,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:block_grass",
                        depth: 1,
                        patch:
                            new BiomeSurfacePatchDefinition(
                                scale: 24,
                                coverage: 0.5f,
                                roughness: 0.3f,
                                blocks:
                                [
                                    "asteria:mud",
                                ])),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var first =
            new BiomeSurfaceMaterialField(
                33,
                [
                    biome,
                ],
                blocks);
        var second =
            new BiomeSurfaceMaterialField(
                33,
                [
                    biome,
                ],
                blocks);
        var sample =
            new BiomeSample(
                biome.Id,
                [
                    new BiomeInfluence(
                        biome.Id,
                        1f),
                ]);
        var grass =
            blocks.GetId(
                "asteria:block_grass");
        var mud =
            blocks.GetId(
                "asteria:mud");
        var grassCount = 0;
        var mudCount = 0;

        for (var z = -32;
             z <= 32;
             z++)
        {
            for (var x = -32;
                 x <= 32;
                 x++)
            {
                var material =
                    first.BlockAt(
                        sample,
                        x,
                        z,
                        0);

                Assert.Equal(
                    material,
                    second.BlockAt(
                        sample,
                        x,
                        z,
                        0));

                if (material == grass)
                {
                    grassCount++;
                }
                else if (material == mud)
                {
                    mudCount++;
                }
                else
                {
                    throw new Xunit.Sdk.XunitException(
                        $"Unexpected patch material {material}.");
                }
            }
        }

        Assert.True(
            grassCount > 0);
        Assert.True(
            mudCount > 0);
    }


    [Theory]
    [InlineData(3f)]
    [InlineData(18f)]
    [InlineData(102f)]
    public void ElevatedTerrainBeginsOnlyInsideItsPrimaryBiome(
        float highOffset)
    {
        var low = TestBiome(
            "asteria:test/low", baseHeightOffset: 2f);
        var high = TestBiome(
            "asteria:test/high", baseHeightOffset: highOffset);
        var generator = new BiomeWorldGenerator(
            44,
            TestDimension([low.Id, high.Id]),
            new BlockRegistry([new BlockDefinition("asteria:stone")]),
            new BiomeRegistry([low, high]));

        var foundLowEdge = false;
        var foundHighEdge = false;
        var foundSlope = false;

        for (var z = -1024;
             z <= 1024 &&
             !(foundLowEdge && foundHighEdge &&
               (foundSlope || highOffset <= 3f));
             z += 8)
        {
            for (var x = -1024; x <= 1024; x += 8)
            {
                var sample = generator.Biomes.Sample(x, z);
                if (sample.Influences.Count < 2)
                {
                    continue;
                }

                var neighbor = sample.Primary == low.Id
                    ? high.Id
                    : low.Id;
                if (!sample.Influences.Any(influence =>
                        influence.BiomeId == neighbor &&
                        influence.Weight > 0f))
                {
                    continue;
                }

                var height = generator.SurfaceHeight(x, z);
                if (sample.Primary == low.Id)
                {
                    // Even the smallest positive uplift cannot leak
                    // into the lower biome's transition band.
                    Assert.Equal(2, height);
                    foundLowEdge = true;
                }
                else
                {
                    Assert.InRange(height, 2, (int)highOffset);
                    foundHighEdge = true;
                    foundSlope |= height > 2 &&
                        height < (int)highOffset;
                }
            }
        }

        Assert.True(foundLowEdge, "Missing low-side boundary sample.");
        Assert.True(foundHighEdge, "Missing high-side boundary sample.");
        if (highOffset > 3f)
        {
            Assert.True(foundSlope, "Elevation must ramp inside the high biome.");
        }
    }

    [Fact]
    public void SphereShellMaterializesFloorAndRoofAndRejectsNegativeChunks()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:shell_sphere",
                    mining:
                        new BlockMiningDefinition(
                            unbreakable: true),
                    dropsSelf: false),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            TestBiome(
                "asteria:test/plain",
                baseHeightOffset: 20f);
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    biome.Id,
                ],
                seaLevel: 0,
                gravityStrength: 18f,
                new DimensionSpawnDefinition(
                    0,
                    0),
                new DimensionEnvironmentDefinition(
                    new DimensionColor(0, 0, 0),
                    new DimensionColor(255, 255, 255),
                    1f,
                    new DimensionColor(0, 0, 0),
                    0f),
                new DimensionShellDefinition(
                    "asteria:shell_sphere",
                    floorY: 0,
                    roofY: 12));
        var generator =
            new BiomeWorldGenerator(
                5,
                dimension,
                blocks,
                new BiomeRegistry(
                [
                    biome,
                ]));
        var chunk =
            generator.Materialize(
                ChunkCoord.Zero);
        var shell =
            blocks.GetId(
                "asteria:shell_sphere");
        var stone =
            blocks.GetId(
                "asteria:stone");

        Assert.Equal(
            11,
            generator.SurfaceHeight(
                0,
                0));
        Assert.Equal(
            shell,
            chunk.GetBlock(
                0,
                0,
                0));
        Assert.Equal(
            stone,
            chunk.GetBlock(
                0,
                11,
                0));
        Assert.Equal(
            shell,
            chunk.GetBlock(
                0,
                12,
                0));
        Assert.True(
            chunk.GetCell(
                    0,
                    13,
                    0)
                .IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                generator.Materialize(
                    new ChunkCoord(
                        0,
                        -1,
                        0)));
    }

    [Theory]
    [InlineData(WorldGenerationMode.Normal)]
    [InlineData(WorldGenerationMode.Flat)]
    public void SphereShellFloorHasOpaqueTerrainCoverInNormalAndFlatWorlds(
        WorldGenerationMode mode)
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:shell_sphere",
                mining: new BlockMiningDefinition(unbreakable: true)),
            new BlockDefinition("asteria:block_grass"),
            new BlockDefinition("asteria:stone"),
        ]);
        var biome = FlatBiome(
            "asteria:test/plain", "asteria:block_grass", "asteria:stone");
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"), [biome.Id],
            seaLevel: 0, gravityStrength: 18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            new DimensionShellDefinition("asteria:shell_sphere", floorY: 0));
        var generator = new BiomeWorldGenerator(
            77UL, dimension, blocks,
            new FluidRegistry(Array.Empty<FluidDefinition>()),
            new BiomeRegistry([biome]),
            StructureRegistry.Empty,
            generation: new WorldGenerationOptions(
                mode, spawnOceans: false, spawnCaves: false));

        var first = generator.Materialize(ChunkCoord.Zero);
        var neighbor = generator.Materialize(new ChunkCoord(1, 0, 1));
        var shell = blocks.GetId("asteria:shell_sphere");
        var core = blocks.GetId("asteria:stone");

        foreach (var chunk in new[] { first, neighbor })
        {
            Assert.Equal(shell, chunk.GetBlock(8, 0, 8));
            var cover = chunk.GetBlock(8, 1, 8);
            Assert.NotEqual(shell, cover);
            Assert.True(blocks.GetDefinition(cover).IsOpaque);
            Assert.Equal(BlockShapeKind.Cube, blocks.GetDefinition(cover).Shape.Kind);
            Assert.True(chunk.GetFluid(8, 1, 8).IsEmpty);
        }

        Assert.True(generator.DensityAt(8, 1, 8) >= 0d);
        if (mode == WorldGenerationMode.Normal)
        {
            // Sealing the floor must preserve the natural surface palette.
            Assert.Equal(blocks.GetId("asteria:block_grass"),
                first.GetBlock(8, 1, 8));
            Assert.Equal(1, generator.SurfaceHeight(8, 8));
        }
        else
        {
            Assert.Equal(core, first.GetBlock(8, 1, 8));
        }
    }

    [Fact]
    public void SphereShellRoofIsHiddenEvenAboveNaturalTerrain()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:shell_sphere",
                mining: new BlockMiningDefinition(unbreakable: true)),
            new BlockDefinition("asteria:stone"),
        ]);
        var biome = TestBiome("asteria:test/plain", baseHeightOffset: 4f);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"), [biome.Id],
            seaLevel: 0, gravityStrength: 18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            new DimensionShellDefinition(
                "asteria:shell_sphere", floorY: 0, roofY: 12));
        var generator = new BiomeWorldGenerator(
            78UL, dimension, blocks, new BiomeRegistry([biome]));
        var chunk = generator.Materialize(ChunkCoord.Zero);

        Assert.Equal(4, generator.SurfaceHeight(8, 8));
        Assert.True(chunk.GetBlock(8, 10, 8).IsAir);
        Assert.Equal(blocks.GetId("asteria:stone"),
            chunk.GetBlock(8, 11, 8));
        Assert.Equal(blocks.GetId("asteria:shell_sphere"),
            chunk.GetBlock(8, 12, 8));
        Assert.True(generator.DensityAt(8, 11, 8) >= 0d);
    }

    [Fact]
    public void OverworldCavesCannotExposeTheSphereShellFloor()
    {
        var blocks = LoadDefaultBlocks();
        var dimension = LoadDefaultDimensions().Get(DimensionId.Overworld);
        var generator = new BiomeWorldGenerator(
            2947UL, dimension, blocks, LoadDefaultFluids(),
            LoadDefaultBiomes(), LoadDefaultStructures(),
            LoadDefaultStructureSets());
        var chunk = generator.Materialize(ChunkCoord.Zero);
        var shell = blocks.GetId("asteria:shell_sphere");

        for (var z = 0; z < Chunk.Size; z += 3)
        for (var x = 0; x < Chunk.Size; x += 3)
        {
            Assert.Equal(shell, chunk.GetBlock(x, 0, z));
            Assert.False(generator.IsCaveVoidAt(x, 1, z));
            Assert.True(generator.DensityAt(x, 1, z) >= 0d);
            var cover = chunk.GetBlock(x, 1, z);
            Assert.NotEqual(shell, cover);
            Assert.True(blocks.GetDefinition(cover).IsOpaque);
            Assert.True(chunk.GetFluid(x, 1, z).IsEmpty);
        }
    }

    [Fact]
    public void DimensionSeaLevelOffsetsAuthoredBiomeHeight()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            TestBiome(
                "asteria:test/plain",
                baseHeightOffset: 8f);
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    biome.Id,
                ],
                seaLevel: 90,
                gravityStrength: 18f,
                new DimensionSpawnDefinition(
                    0,
                    0),
                new DimensionEnvironmentDefinition(
                    new DimensionColor(0, 0, 0),
                    new DimensionColor(255, 255, 255),
                    1f,
                    new DimensionColor(0, 0, 0),
                    0f));
        var generator =
            new BiomeWorldGenerator(
                5,
                dimension,
                blocks,
                new BiomeRegistry(
                [
                    biome,
                ]));

        Assert.Equal(
            98,
            generator.SurfaceHeight(
                0,
                0));
    }

    [Fact]
    public void MaterializedTerrainKeepsFiniteSurfaceCoverBeforeApplyingThreeDimensionalGeology()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:grass"),
            new BlockDefinition("asteria:dirt"),
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:gravel"),
            new BlockDefinition("asteria:clay"),
        ]);
        var geology = new BiomeGeologyDefinition(
            "asteria:stone",
            ["asteria:gravel", "asteria:clay"],
            scale: 64,
            verticalScale: 42,
            detailScale: 16,
            selectionScale: 128,
            coverage: 0.6f,
            roughness: 0.3f,
            surfaceCoverDepth: 5);
        var biome = new BiomeDefinition(
            "asteria:test/plain",
            new BiomeSurfaceLayoutDefinition(regionMin: 128, regionMax: 128),
            new BiomeTerrainDefinition(
                baseHeightOffset: 10f,
                macroAmplitude: 0f,
                macroScale: 128,
                detailAmplitude: 0f,
                detailScale: 32),
            [
                new BiomeSurfaceLayerDefinition("asteria:grass", depth: 1),
                new BiomeSurfaceLayerDefinition("asteria:dirt", depth: 4),
                new BiomeSurfaceLayerDefinition("asteria:stone"),
            ],
            geology: geology);
        var generator = new BiomeWorldGenerator(
            2468UL,
            TestDimension([biome.Id]),
            blocks,
            new BiomeRegistry([biome]));

        var chunk = generator.Materialize(new ChunkCoord(0, 0, 0));
        var grass = blocks.GetId("asteria:grass");
        var dirt = blocks.GetId("asteria:dirt");
        var allowedGeology = new HashSet<BlockRuntimeId>
        {
            blocks.GetId("asteria:stone"),
            blocks.GetId("asteria:gravel"),
            blocks.GetId("asteria:clay"),
        };

        Assert.Equal(grass, chunk.GetBlock(2, 10, 2));
        Assert.Equal(dirt, chunk.GetBlock(2, 9, 2));
        Assert.Equal(dirt, chunk.GetBlock(2, 6, 2));
        Assert.Contains(chunk.GetBlock(2, 5, 2), allowedGeology);
        Assert.Contains(chunk.GetBlock(2, 3, 2), allowedGeology);
    }

    [Fact]
    public void MaterializedRockSurfaceUsesGeologyWhenNoCoverIsAuthored()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:gravel"),
        ]);
        var geology = new BiomeGeologyDefinition(
            "asteria:stone",
            ["asteria:gravel"],
            scale: 64,
            verticalScale: 42,
            detailScale: 16,
            selectionScale: 128,
            coverage: 1f,
            surfaceCoverDepth: 0);
        var biome = new BiomeDefinition(
            "asteria:test/rock",
            new BiomeSurfaceLayoutDefinition(regionMin: 128, regionMax: 128),
            new BiomeTerrainDefinition(
                baseHeightOffset: 10f,
                macroAmplitude: 0f,
                macroScale: 128,
                detailAmplitude: 0f,
                detailScale: 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            geology: geology);
        var generator = new BiomeWorldGenerator(
            97531UL,
            TestDimension([biome.Id]),
            blocks,
            new BiomeRegistry([biome]));

        var chunk = generator.Materialize(new ChunkCoord(0, 0, 0));
        var gravel = blocks.GetId("asteria:gravel");

        Assert.Equal(gravel, chunk.GetBlock(2, 10, 2));
        Assert.Equal(gravel, chunk.GetBlock(2, 5, 2));
    }

    [Fact]
    public void DecorationUsesAuthoredSurfaceAndIsChunkDeterministic()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:mud"),
                new BlockDefinition(
                    "asteria:mushroom_brown",
                    tags:
                    [
                        BlockPhysicsCapabilities.SupportBelow,
                    ],
                    isCollidable: false),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/swamp",
                new BiomeSurfaceLayoutDefinition(
                    regionMin: 128,
                    regionMax: 128),
                new BiomeTerrainDefinition(
                    baseHeightOffset: 5f,
                    macroAmplitude: 0f,
                    macroScale: 128,
                    detailAmplitude: 0f,
                    detailScale: 32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:mud",
                        depth: 1),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ],
                [
                    new BiomeDecorationDefinition(
                        "asteria:mushroom_brown",
                        chance: 1f,
                        surfaceBlocks:
                        [
                            "asteria:mud",
                        ]),
                ]);
        var generator =
            new BiomeWorldGenerator(
                1,
                TestDimension(
                [
                    biome.Id,
                ]),
                blocks,
                new BiomeRegistry(
                [
                    biome,
                ]));

        var first =
            generator.Materialize(
                ChunkCoord.Zero);
        var second =
            generator.Materialize(
                ChunkCoord.Zero);
        var mud =
            blocks.GetId(
                "asteria:mud");
        var mushroom =
            blocks.GetId(
                "asteria:mushroom_brown");

        for (var z = 0;
             z < Chunk.Size;
             z++)
        {
            for (var x = 0;
                 x < Chunk.Size;
                 x++)
            {
                Assert.Equal(
                    mud,
                    first.GetBlock(
                        x,
                        5,
                        z));
                Assert.Equal(
                    mushroom,
                    first.GetBlock(
                        x,
                        6,
                        z));
                Assert.Equal(
                    first.GetCell(
                        x,
                        6,
                        z),
                    second.GetCell(
                        x,
                        6,
                        z));
            }
        }
    }

    [Fact]
    public void CaveWallPaletteOverridesOnlySolidWallsOnActualCarvedVoid()
    {
        var blocks = LoadDefaultBlocks();
        var dimension = LoadDefaultDimensions().Get(DimensionId.Overworld);
        var biomes = BiomeRegistry.FromJson(
            Directory.EnumerateFiles(
                Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", "biomes"),
                "*.json").OrderBy(path => path, StringComparer.Ordinal)
                .Select(path =>
                {
                    var content = File.ReadAllText(path);
                    if (!path.EndsWith("caverns.json", StringComparison.Ordinal))
                        return content;
                    var root = System.Text.Json.Nodes.JsonNode.Parse(content)!;
                    root["palette"] = System.Text.Json.Nodes.JsonNode.Parse(
                        """
                        {
                          "default":[{"block":"asteria:stone"}],
                          "walls":[
                            {"block":"asteria:basalt","depth":2},
                            {"block":"asteria:stone"}
                          ]
                        }
                        """);
                    root["decorations"] = System.Text.Json.Nodes.JsonNode.Parse("[]");
                    root["caveSpikes"] = System.Text.Json.Nodes.JsonNode.Parse("[]");
                    return root.ToJsonString();
                }));
        var generator = new BiomeWorldGenerator(
            DimensionSeed.Derive(0xA57E_2026UL, dimension.Id), dimension,
            blocks, LoadDefaultFluids(), biomes, LoadDefaultStructures(),
            LoadDefaultStructureSets());
        var cave = FindCaveVoid(generator);
        (int X, int Y, int Z)? wall = null;
        var directions = new (int X, int Z)[]
        {
            (1, 0), (-1, 0), (0, 1), (0, -1),
        };

        for (var dy = -8; dy <= 8 && wall is null; dy++)
        {
            var y = cave.Y + dy;
            if (y <= 1 || !generator.IsCaveVoidAt(cave.X, y, cave.Z))
                continue;
            foreach (var (dx, dz) in directions)
            {
                for (var distance = 1; distance <= 64; distance++)
                {
                    var x = cave.X + dx * distance;
                    var z = cave.Z + dz * distance;
                    if (generator.IsCaveVoidAt(x, y, z))
                        continue;
                    if (y > generator.SurfaceHeight(x, z) - 5 ||
                        generator.DensityAt(x, y, z) < 0d ||
                        generator.IsCaveVoidAt(x, y + 1, z) ||
                        generator.IsCaveVoidAt(x, y - 1, z))
                        break;
                    wall = (x, y, z);
                    break;
                }
                if (wall is not null)
                    break;
            }
        }

        Assert.NotNull(wall);
        var target = wall!.Value;
        var (chunkCoord, local) = VoxelCoordinates.FromWorld(
            target.X, target.Y, target.Z);
        var actual = generator.Materialize(chunkCoord).GetBlock(
            local.X, local.Y, local.Z);
        Assert.Equal(blocks.GetId("asteria:basalt"), actual);
        Assert.False(generator.IsCaveVoidAt(target.X, target.Y, target.Z));
    }

    [Fact]
    public void UndergroundDecoratorsOnlyMaterializeOnActuallyCarvedCaveFloors()
    {
        var blocks = LoadDefaultBlocks();
        var dimension = LoadDefaultDimensions().Get(DimensionId.Overworld);
        var caveId = "asteria:overworld/caverns";
        var biomes = BiomeRegistry.FromJson(
            Directory.EnumerateFiles(
                Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", "biomes"),
                "*.json").OrderBy(path => path, StringComparer.Ordinal)
                .Select(path =>
                {
                    var content = File.ReadAllText(path);
                    if (!path.EndsWith("caverns.json", StringComparison.Ordinal))
                        return content;
                    var node = System.Text.Json.Nodes.JsonNode.Parse(content)!;
                    node["decorations"] = System.Text.Json.Nodes.JsonNode.Parse(
                        """[{"object":"asteria:pebble","chance":1,"surfaceBlocks":["asteria:stone","asteria:gravel","asteria:basalt","asteria:sandstone","asteria:clay","asteria:terracotta","asteria:cobble_stone"]}]""");
                    return node.ToJsonString();
                }));
        const ulong seed = 0xA57E_2026UL;
        var objects = Asteria.Core.Content.PackContentRegistry<Asteria.Core.Content.ObjectDefinition>
            .FromJson(Directory.EnumerateFiles(
                Path.Combine(AppContext.BaseDirectory, "packs", "default",
                    "data", "objects"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText),
                Asteria.Core.Content.ObjectDefinition.Parse, d => d.Id);
        var generator = new BiomeWorldGenerator(
            DimensionSeed.Derive(seed, dimension.Id), dimension, blocks,
            LoadDefaultFluids(), biomes, LoadDefaultStructures(),
            LoadDefaultStructureSets(), objects: objects);

        var cave = FindCaveVoid(generator);
        var floorY = cave.Y;
        while (floorY > 1 && generator.IsCaveVoidAt(cave.X, floorY - 1, cave.Z))
            floorY--;

        Assert.True(generator.IsCaveVoidAt(cave.X, floorY, cave.Z));
        Assert.True(generator.DensityAt(cave.X, floorY - 1, cave.Z) >= 0d);
        Assert.Equal(caveId, generator.EffectiveBiomeAt(cave.X, floorY, cave.Z));

        var (targetChunk, targetLocal) = VoxelCoordinates.FromWorld(
            cave.X, floorY, cave.Z);
        var actual = generator.Materialize(targetChunk).GetBlock(
            targetLocal.X, targetLocal.Y, targetLocal.Z);
        Assert.True(actual.IsAir);
        var (hostChunk, hostLocal) = VoxelCoordinates.FromWorld(
            cave.X, floorY - 1, cave.Z);
        var support = generator.Materialize(hostChunk);
        Assert.Contains(support.ObjectsAt(hostLocal.X, hostLocal.Y, hostLocal.Z),
            obj => obj.Id == "asteria:pebble" && obj.Face == BlockFace.Top);

        // The same XY position above normal solid rock must never be
        // decorated as Caverns merely because of the X/Z underground owner.
        Assert.False(generator.IsCaveVoidAt(cave.X, floorY - 1, cave.Z));
        Assert.Equal(actual, generator.Materialize(targetChunk).GetBlock(
            targetLocal.X, targetLocal.Y, targetLocal.Z));
    }

    [Fact]
    public void CavernFloorObjectsAreStoredOnTheActualCarvedSupportBlock()
    {
        var blocks = LoadDefaultBlocks();
        var dimensions = LoadDefaultDimensions();
        var dimension = dimensions.Get(DimensionId.Overworld);
        var objects = Asteria.Core.Content.PackContentRegistry<Asteria.Core.Content.ObjectDefinition>.FromJson(
            Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", "objects"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText),
            Asteria.Core.Content.ObjectDefinition.Parse, obj => obj.Id);
        var biomes = BiomeRegistry.FromJson(
            Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", "biomes"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path =>
                {
                    var content = File.ReadAllText(path);
                    if (!path.EndsWith("caverns.json", StringComparison.Ordinal))
                        return content;
                    var node = System.Text.Json.Nodes.JsonNode.Parse(content)!;
                    node["decorations"] = System.Text.Json.Nodes.JsonNode.Parse(
                        """
                        [{"object":"asteria:mushroom_brown","chance":1,
                          "surfaceBlocks":["asteria:stone","asteria:gravel",
                            "asteria:dirt","asteria:cobble_stone","asteria:clay",
                            "asteria:sandstone","asteria:basalt","asteria:terracotta"]}]
                        """);
                    return node.ToJsonString();
                }));
        var generator = new BiomeWorldGenerator(
            DimensionSeed.Derive(0xA57E_2026UL, dimension.Id), dimension,
            blocks, LoadDefaultFluids(), biomes, LoadDefaultStructures(),
            LoadDefaultStructureSets(),
            generation: new WorldGenerationOptions(spawnStructures: false),
            objects: objects);

        var cave = FindCaveVoid(generator);
        var floorY = cave.Y;
        while (floorY > 1 && generator.IsCaveVoidAt(cave.X, floorY - 1, cave.Z))
            floorY--;
        Assert.True(generator.IsCaveVoidAt(cave.X, floorY, cave.Z));
        Assert.True(generator.DensityAt(cave.X, floorY - 1, cave.Z) >= 0d);

        var (supportChunk, supportLocal) = VoxelCoordinates.FromWorld(
            cave.X, floorY - 1, cave.Z);
        var support = generator.Materialize(supportChunk);
        Assert.False(support.GetCell(
            supportLocal.X, supportLocal.Y, supportLocal.Z).IsEmpty);
        var mushroom = Assert.Single(support.ObjectsAt(
            supportLocal.X, supportLocal.Y, supportLocal.Z),
            obj => obj.Id == "asteria:mushroom_brown");
        Assert.Equal(BlockFace.Top, mushroom.Face);

        var (airChunk, airLocal) = VoxelCoordinates.FromWorld(
            cave.X, floorY, cave.Z);
        Assert.True(generator.Materialize(airChunk)
            .GetCell(airLocal.X, airLocal.Y, airLocal.Z).IsEmpty);
        Assert.Equal(support.ObjectsAt(
            supportLocal.X, supportLocal.Y, supportLocal.Z),
            generator.Materialize(supportChunk).ObjectsAt(
                supportLocal.X, supportLocal.Y, supportLocal.Z));
    }

    [Fact]
    public void FloatingVolumeDecoratorsUseEachExposedTopNotTheBaseSurface()
    {
        var blocks = LoadDefaultBlocks();
        var dimension = LoadDefaultDimensions().Get(DimensionId.Overworld);
        var volumeId = "asteria:overworld/floating_islands";
        var biomes = BiomeRegistry.FromJson(
            Directory.EnumerateFiles(
                Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", "biomes"),
                "*.json").OrderBy(path => path, StringComparer.Ordinal)
                .Select(path =>
                {
                    var content = File.ReadAllText(path);
                    if (!path.EndsWith("floating_islands.json", StringComparison.Ordinal))
                        return content;
                    var node = System.Text.Json.Nodes.JsonNode.Parse(content)!;
                    node["decorations"] = System.Text.Json.Nodes.JsonNode.Parse(
                        """[{"object":"asteria:grass","chance":1,"surfaceBlocks":["asteria:block_grass"]}]""");
                    return node.ToJsonString();
                }));
        var generator = new BiomeWorldGenerator(
            DimensionSeed.Derive(0xA57E_2026UL, dimension.Id), dimension, blocks,
            LoadDefaultFluids(), biomes, LoadDefaultStructures(),
            LoadDefaultStructureSets(), objects: LoadDefaultObjects());
        // Biome ownership does not imply solid additive density in that
        // column. Select a real island interior, not the first volume-biome
        // region; its X/Z location may change with the authored surface layout.
        // SurfaceHeight includes additive islands, so use density below the
        // island's minimum Y to prove this is not an elevated base surface.
        (int X, int Z)? islandColumn = null;
        for (var z = -4096; z <= 4096 && islandColumn is null; z += 32)
        for (var x = -4096; x <= 4096 && islandColumn is null; x += 32)
        {
            if (generator.VolumeBiomes.Sample(x, 240, z)?.Primary == volumeId &&
                generator.DensityAt(x, 240, z) >= 0d &&
                generator.DensityAt(x, 192, z) < 0d)
            {
                islandColumn = (x, z);
            }
        }

        Assert.NotNull(islandColumn);
        var (islandX, islandZ) = islandColumn!.Value;
        (int X, int Y, int Z)? target = null;
        for (var y = 281; y > 240; y--)
        {
            if (generator.VolumeBiomes.Sample(islandX, y - 1, islandZ)?.Primary != volumeId ||
                generator.DensityAt(islandX, y - 1, islandZ) < 0d ||
                generator.DensityAt(islandX, y, islandZ) >= 0d)
            {
                continue;
            }

            target = (islandX, y, islandZ);
            break;
        }

        Assert.NotNull(target);
        var found = target!.Value;
        var (chunkCoord, local) = VoxelCoordinates.FromWorld(
            found.X, found.Y, found.Z);
        var chunk = generator.Materialize(chunkCoord);
        Assert.True(chunk.GetCell(local.X, local.Y, local.Z).IsEmpty);
        var (hostCoord, hostLocal) = VoxelCoordinates.FromWorld(
            found.X, found.Y - 1, found.Z);
        var host = generator.Materialize(hostCoord);
        Assert.Contains(host.ObjectsAt(hostLocal.X, hostLocal.Y, hostLocal.Z),
            obj => obj.Id == "asteria:grass" && obj.Face == BlockFace.Top);
        Assert.Equal(
            host.ObjectsAt(hostLocal.X, hostLocal.Y, hostLocal.Z),
            generator.Materialize(hostCoord)
                .ObjectsAt(hostLocal.X, hostLocal.Y, hostLocal.Z));
    }

    [Fact]
    public void EffectiveBiomeUsesSurfaceBiomeOutsideFloatingVolume()
    {
        var blocks =
            LoadDefaultBlocks();
        var biomes =
            LoadDefaultBiomes();
        var fluids =
            LoadDefaultFluids();
        var structures =
            LoadDefaultStructures();
        var dimensions =
            LoadDefaultDimensions();
        var dimension =
            dimensions.Get(
                DimensionId.Overworld);
        var generator =
            new BiomeWorldGenerator(
                DimensionSeed.Derive(
                    0xA57E_2026UL,
                    dimension.Id),
                dimension,
                blocks,
                fluids,
                biomes,
                structures,
                LoadDefaultStructureSets());
        var floating =
            FindVolumeBiomeInterior(
                generator.VolumeBiomes,
                "asteria:overworld/floating_islands",
                y: 220);

        var surfaceBiome =
            generator.EffectiveBiomeAt(
                floating.X,
                dimension.SeaLevel,
                floating.Z);
        var volumeBiome =
            generator.EffectiveBiomeAt(
                floating.X,
                220,
                floating.Z);
        var surfaceSample =
            generator.Biomes.Sample(
                floating.X,
                floating.Z);
        var volumeSample =
            generator.VolumeBiomes.Sample(
                floating.X,
                220,
                floating.Z);

        Assert.NotEqual(
            "asteria:overworld/floating_islands",
            surfaceBiome);
        Assert.DoesNotContain(
            surfaceSample.Influences,
            influence =>
                dimension.VolumeBiomes.Contains(
                    influence.BiomeId,
                    StringComparer.Ordinal));
        Assert.Equal(
            "asteria:overworld/floating_islands",
            volumeBiome);
        Assert.NotNull(
            volumeSample);
        Assert.All(
            volumeSample!.Influences,
            influence =>
                Assert.Contains(
                    influence.BiomeId,
                    dimension.VolumeBiomes));
    }

    [Fact]
    public void EffectiveBiomeUsesCavernsOnlyInsideCarvedCaveVoid()
    {
        var blocks =
            LoadDefaultBlocks();
        var biomes =
            LoadDefaultBiomes();
        var fluids =
            LoadDefaultFluids();
        var structures =
            LoadDefaultStructures();
        var dimensions =
            LoadDefaultDimensions();
        var dimension =
            dimensions.Get(
                DimensionId.Overworld);
        var generator =
            new BiomeWorldGenerator(
                DimensionSeed.Derive(
                    0xA57E_2026UL,
                    dimension.Id),
                dimension,
                blocks,
                fluids,
                biomes,
                structures,
                LoadDefaultStructureSets());
        var cave =
            FindCaveVoid(
                generator);

        Assert.True(
            generator.IsCaveVoidAt(
                cave.X,
                cave.Y,
                cave.Z));
        Assert.True(
            generator.DensityAt(
                cave.X,
                cave.Y,
                cave.Z) <
            0d);
        Assert.Equal(
            "asteria:overworld/caverns",
            generator.EffectiveBiomeAt(
                cave.X,
                cave.Y,
                cave.Z));

        var solidY =
            generator.SurfaceHeight(
                cave.X,
                cave.Z) -
            1;
        Assert.True(
            solidY >= 0);
        Assert.False(
            generator.IsCaveVoidAt(
                cave.X,
                solidY,
                cave.Z));
        Assert.NotEqual(
            "asteria:overworld/caverns",
            generator.EffectiveBiomeAt(
                cave.X,
                solidY,
                cave.Z));
    }

    [Fact]
    public void DefaultWorldGeneratorProducesGroundPlantsFromBiomeDecorators()
    {
        var blocks =
            LoadDefaultBlocks();
        var biomes =
            LoadDefaultBiomes();
        var fluids =
            LoadDefaultFluids();
        var structures =
            LoadDefaultStructures();
        var dimensions =
            LoadDefaultDimensions();
        dimensions.ValidateBiomes(
            biomes);
        dimensions.ValidateFluids(
            fluids);
        var dimension =
            dimensions.Get(
                DimensionId.Overworld);
        var generator =
            new BiomeWorldGenerator(
                DimensionSeed.Derive(
                    0xA57E_2026UL,
                    dimension.Id),
                dimension,
                blocks,
                fluids,
                biomes,
                structures,
                LoadDefaultStructureSets(),
                objects: Asteria.Core.Content.PackContentRegistry<Asteria.Core.Content.ObjectDefinition>.FromJson(
                    Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,
                        "packs", "default", "data", "objects"), "*.json")
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .Select(File.ReadAllText),
                    Asteria.Core.Content.ObjectDefinition.Parse, obj => obj.Id));

        var plains =
            FindBiomeInterior(
                generator.Biomes,
                "asteria:overworld/plains");
        var swamp =
            FindBiomeInterior(
                generator.Biomes,
                "asteria:overworld/swamp");

        Assert.True(HasObjectNear(generator, plains, "asteria:grass"));
        Assert.True(
            HasObjectNear(generator, swamp, "asteria:mushroom_brown"));
    }

    private static (int X, int Y, int Z) FindCaveVoid(
        BiomeWorldGenerator generator)
    {
        for (var z = -1024;
             z <= 1024;
             z += 32)
        {
            for (var x = -1024;
                 x <= 1024;
                 x += 32)
            {
                var surfaceY =
                    generator.SurfaceHeight(
                        x,
                        z);
                var minimumY =
                    Math.Max(
                        1,
                        surfaceY - 110);
                var maximumY =
                    surfaceY - 10;

                for (var y = minimumY;
                     y <= maximumY;
                     y += 3)
                {
                    if (generator.IsCaveVoidAt(
                            x,
                            y,
                            z))
                    {
                        return (
                            x,
                            y,
                            z);
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException(
            "Could not find a deterministic carved cave void.");
    }

    private static (int X, int Z) FindVolumeBiomeInterior(
        VolumeBiomeField field,
        string biomeId,
        int y)
    {
        for (var z = -4096;
             z <= 4096;
             z += 64)
        {
            for (var x = -4096;
                 x <= 4096;
                 x += 64)
            {
                var sample =
                    field.Sample(
                        x,
                        y,
                        z);

                if (sample?.Primary ==
                        biomeId &&
                    sample.PrimaryWeight >=
                        0.9f)
                {
                    return (
                        x,
                        z);
                }
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Could not find an interior volume sample for {biomeId}.");
    }

    private static (int X, int Z) FindBiomeInterior(
        BiomeField field,
        string biomeId)
    {
        for (var z = -4096;
             z <= 4096;
             z += 64)
        {
            for (var x = -4096;
                 x <= 4096;
                 x += 64)
            {
                var sample =
                    field.Sample(
                        x,
                        z);

                if (sample.Primary ==
                        biomeId &&
                    sample.PrimaryWeight >=
                        0.9f)
                {
                    return (
                        x,
                        z);
                }
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Could not find an interior sample for {biomeId}.");
    }

    private static Asteria.Core.Content.PackContentRegistry<Asteria.Core.Content.ObjectDefinition> LoadDefaultObjects() =>
        Asteria.Core.Content.PackContentRegistry<Asteria.Core.Content.ObjectDefinition>.FromJson(
            Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", "objects"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText),
            Asteria.Core.Content.ObjectDefinition.Parse, obj => obj.Id);

    private static bool HasObjectNear(
        BiomeWorldGenerator generator, (int X, int Z) center, string id) =>
        HasContentNear(generator, center, chunk =>
        {
            var found = false;
            chunk.VisitObjectCells((_, _, _, obj) => found |= obj.Id == id);
            return found;
        });

    private static bool HasBlockNear(
        BiomeWorldGenerator generator, (int X, int Z) center, BlockRuntimeId target) =>
        HasContentNear(generator, center, chunk =>
        {
            var found = false;
            chunk.VisitBlockCells((_, _, _, cell) => found |= cell.Block == target);
            return found;
        });

    private static bool HasContentNear(
        BiomeWorldGenerator generator, (int X, int Z) center,
        Func<Chunk, bool> matches)
    {
        var centerChunk =
            VoxelCoordinates
                .FromWorld(
                    center.X,
                    0,
                    center.Z)
                .Chunk;

        for (var dz = -1;
             dz <= 1;
             dz++)
        {
            for (var dx = -1;
                 dx <= 1;
                 dx++)
            {
                var chunkX =
                    centerChunk.X +
                    dx;
                var chunkZ =
                    centerChunk.Z +
                    dz;
                var surface =
                    generator.GetSurfaceRange(
                        chunkX,
                        chunkZ);
                var minimumChunkY =
                    VoxelCoordinates.FromWorld(
                            0,
                            surface.MinimumWorldY,
                            0)
                        .Chunk.Y;
                var maximumChunkY =
                    VoxelCoordinates.FromWorld(
                            0,
                            surface.MaximumWorldY +
                            1,
                            0)
                        .Chunk.Y;

                for (var chunkY = minimumChunkY;
                     chunkY <= maximumChunkY;
                     chunkY++)
                {
                    var chunk =
                        generator.Materialize(
                            new ChunkCoord(
                                chunkX,
                                chunkY,
                                chunkZ));
                    if (matches(chunk))
                        return true;
                }
            }
        }

        return false;
    }

    private static BlockRegistry LoadDefaultBlocks()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "blocks");

        return BlockRegistry.FromJson(
            Directory
                .EnumerateFiles(
                    directory,
                    "*.json")
                .OrderBy(
                    path => path,
                    StringComparer.Ordinal)
                .Select(
                    File.ReadAllText));
    }

    private static FluidRegistry LoadDefaultFluids()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "fluids");

        return FluidRegistry.FromJson(
            Directory
                .EnumerateFiles(
                    directory,
                    "*.json")
                .OrderBy(
                    path => path,
                    StringComparer.Ordinal)
                .Select(
                    File.ReadAllText));
    }

    private static StructureRegistry LoadDefaultStructures()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "structures");

        return StructureRegistry.FromJson(
            Directory
                .EnumerateFiles(
                    directory,
                    "*.json")
                .OrderBy(
                    path => path,
                    StringComparer.Ordinal)
                .Select(
                    File.ReadAllText));
    }

    private static StructureSetRegistry LoadDefaultStructureSets()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data", "structure_sets");
        return StructureSetRegistry.FromJson(
            Directory.EnumerateFiles(directory, "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static DimensionRegistry LoadDefaultDimensions()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "dimensions");

        return DimensionRegistry.FromJson(
            Directory
                .EnumerateFiles(
                    directory,
                    "*.json")
                .OrderBy(
                    path => path,
                    StringComparer.Ordinal)
                .Select(
                    File.ReadAllText));
    }

    private static BiomeRegistry LoadDefaultBiomes()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "biomes");

        return BiomeRegistry.FromJson(
            Directory
                .EnumerateFiles(
                    directory,
                    "*.json")
                .OrderBy(
                    path => path,
                    StringComparer.Ordinal)
                .Select(
                    File.ReadAllText));
    }

    private static BiomeDefinition[] StandardBiomeDefinitions() =>
    [
        TestBiome(
            "asteria:test/a",
            cannotBorder:
            [
                "asteria:test/b",
            ]),
        TestBiome(
            "asteria:test/b",
            cannotBorder:
            [
                "asteria:test/a",
            ]),
        TestBiome(
            "asteria:test/c"),
        TestBiome(
            "asteria:test/d",
            weight: 0.6f),
    ];

    private static BiomeDefinition FlatBiome(
        string id,
        string surface,
        string core) =>
        new(
            id,
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(
                0,
                0,
                64,
                0,
                32),
            [
                new BiomeSurfaceLayerDefinition(
                    surface,
                    depth: 1),
                new BiomeSurfaceLayerDefinition(
                    core),
            ]);

    private static DimensionDefinition TestDimension(
        IEnumerable<string> biomeIds) =>
        new(
            new DimensionId(
                "asteria:test"),
            biomeIds,
            seaLevel: 0,
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
                0f));

    private static BiomeDefinition TestBiome(
        string id,
        float baseHeightOffset = 8f,
        float weight = 1f,
        IEnumerable<string>? cannotBorder = null) =>
        new(
            id,
            new BiomeSurfaceLayoutDefinition(
                weight,
                regionMin: 192,
                regionMax: 384,
                cannotBorder:
                    cannotBorder),
            new BiomeTerrainDefinition(
                baseHeightOffset,
                macroAmplitude: 0f,
                macroScale: 128,
                detailAmplitude: 0f,
                detailScale: 32),
            [
                new BiomeSurfaceLayerDefinition(
                    "asteria:stone"),
            ]);

    [Fact]
    public void CreationGenerationSettingsValidateSingleBiomeAndSizeBounds()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorldGenerationOptions(singleBiome: true));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldGenerationOptions(biomeSizeTenths: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldGenerationOptions(biomeSizeTenths: 51));
        var creation = new WorldCreationOptions(
            "Flat Test", 123UL,
            generation: new WorldGenerationOptions(
                WorldGenerationMode.Flat, biomeSizeTenths: 23));
        Assert.Equal(WorldGenerationMode.Flat, creation.Generation.Mode);
        Assert.Equal(2.3f, creation.Generation.BiomeSizeMultiplier);
    }

    [Fact]
    public void FlatAndVoidCreateDifferentDeterministicVoxelTerrain()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:block_grass"),
            new BlockDefinition("asteria:stone"),
        ]);
        var plains = FlatBiome(
            "asteria:test/plains", "asteria:block_grass", "asteria:stone");
        var dimension = TestDimension([plains.Id]);
        var biomes = new BiomeRegistry([plains]);
        var fluids = new FluidRegistry(Array.Empty<FluidDefinition>());
        BiomeWorldGenerator Generate(WorldGenerationMode mode) =>
            new(
                199UL, dimension, blocks, fluids, biomes,
                StructureRegistry.Empty,
                generation: new WorldGenerationOptions(
                    mode, spawnCaves: false, spawnOceans: false));

        var flat = Generate(WorldGenerationMode.Flat);
        var voidWorld = Generate(WorldGenerationMode.Void);
        var flatChunk = flat.Materialize(new ChunkCoord(0, 0, 0));
        var voidChunk = voidWorld.Materialize(new ChunkCoord(0, 0, 0));
        Assert.False(flatChunk.GetBlock(8, 2, 8).IsAir);
        Assert.True(voidChunk.GetBlock(8, 2, 8).IsAir);
        Assert.False(voidChunk.GetBlock(0, 2, 0).IsAir);
        Assert.Equal(new GeneratedSurfaceDestination(0, 3, 0),
            voidWorld.FindGeneratedSpawn(64, 12));
        Assert.Equal(2, flat.SurfaceHeight(8, 8));
        Assert.Equal(0, voidWorld.SurfaceHeight(8, 8));
        Assert.True(voidWorld.Materialize(new ChunkCoord(2, 0, 2)).IsEmpty);
    }

    [Fact]
    public void ChosenSpawnBiomeUsesSafeGeneratedDestinationInsideThatBiome()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var low = TestBiome("asteria:test/low");
        var high = TestBiome("asteria:test/high");
        var dimension = TestDimension([low.Id, high.Id]);
        var generator = new BiomeWorldGenerator(
            541UL, dimension, blocks,
            new FluidRegistry(Array.Empty<FluidDefinition>()),
            new BiomeRegistry([low, high]),
            StructureRegistry.Empty,
            generation: new WorldGenerationOptions(
                spawnBiome: high.Id, spawnCaves: false, spawnOceans: false,
                spawnStructures: false));

        var spawn = generator.FindGeneratedSpawn(
            maxBiomeDistance: 1024, maxLocalRadius: 64);
        Assert.NotNull(spawn);
        Assert.Equal(high.Id, generator.Biomes.Sample(
            spawn.Value.X, spawn.Value.Z).Primary);
        Assert.Equal(spawn, generator.FindGeneratedSpawn(
            maxBiomeDistance: 1024, maxLocalRadius: 64));
    }

    [Fact]
    public void ExplicitSingleBiomeAndBiomeSizeAffectActualFieldRules()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var low = TestBiome("asteria:test/low");
        var high = TestBiome("asteria:test/high");
        var biomes = new BiomeRegistry([low, high]);
        var dimension = TestDimension([low.Id, high.Id]);
        var options = new WorldGenerationOptions(
            spawnBiome: low.Id, biomeSizeTenths: 25, singleBiome: true);
        var generator = new BiomeWorldGenerator(
            541UL, dimension, blocks,
            new FluidRegistry(Array.Empty<FluidDefinition>()),
            biomes, StructureRegistry.Empty, generation: options);
        Assert.Equal(low.Id, generator.Biomes.Sample(310, -270).Primary);
        Assert.Equal(low.Id, generator.Biomes.Sample(-500, 720).Primary);
        var defaultField = new BiomeField(541UL, dimension, biomes);
        var scaledField = new BiomeField(
            541UL, dimension, biomes, biomeSizeMultiplier: 2.5f);
        Assert.True(scaledField.SeedSpacing >= defaultField.SeedSpacing);
        Assert.Equal(options, new DimensionSessionState(
            dimension, 541UL, new WorldGameRules(),
            options).Generation);
    }

}
