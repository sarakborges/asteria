using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class TerrainDensityTests
{
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
    public void ChamberDensityMatchesScalarAndVolumeAcrossChunkBoundaries()
    {
        var generator = Generator(caves: ChamberCaves(withChambers: true));
        var volume = generator.SampleDensityVolume(-17, 41, -17, 4, 4, 4);
        for (var z = 0; z < 4; z++)
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
        {
            Assert.Equal(
                generator.DensityAt(-17 + x, 41 + y, -17 + z),
                volume.DensityAt(x, y, z));
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
                "asteria:spike", 1f, 3, 5, 6,
                [CaveSpikeDirection.Up, CaveSpikeDirection.Down]));
        var floor = generator.Materialize(new ChunkCoord(0, 1, 0));
        var ceiling = generator.Materialize(new ChunkCoord(0, 5, 0));
        Assert.Contains(
            Enumerable.Range(0, Chunk.Size)
                .SelectMany(y => Enumerable.Range(0, Chunk.Size)
                    .Select(x => floor.GetCell(x, y, 0))),
            cell => cell.Block == Block("asteria:spike") &&
                !SpikeSegmentState.IsDown(cell.State));
        Assert.Contains(
            Enumerable.Range(0, Chunk.Size)
                .SelectMany(y => Enumerable.Range(0, Chunk.Size)
                    .Select(x => ceiling.GetCell(x, y, 0))),
            cell => cell.Block == Block("asteria:spike") &&
                SpikeSegmentState.IsDown(cell.State));
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
            Block("asteria:grass_block"),
            chunk.GetBlock(
                address.Local.X,
                address.Local.Y,
                address.Local.Z));
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
            Block("asteria:sphere_shell"),
            floor.GetBlock(0, 0, 0));
        Assert.Equal(
            Block("asteria:sphere_shell"),
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
              "surfaceLayers":[{"block":"asteria:stone"}],
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
        new BlockDefinition("asteria:grass_block"),
        new BlockDefinition("asteria:dirt"),
        new BlockDefinition("asteria:sphere_shell"),
        new BlockDefinition("asteria:spike",
            shape: BlockShapeDefinition.Spike(), lightDampening: 0),
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
        BiomeCaveSpikeDefinition? caveSpike = null)
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
                        "asteria:grass_block",
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
                            "asteria:grass_block",
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
        var undergroundBiome =
            caveSpike is null
                ? null
                : new BiomeDefinition(
                    "asteria:test/caverns",
                    null, null,
                    undergroundLayout: new BiomeUndergroundLayoutDefinition(),
                    caveSpikes: [caveSpike]);
        var definitions =
            new[] { surfaceBiome, volumeBiome, undergroundBiome }
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
                        "asteria:sphere_shell",
                        floorY,
                        roofY)
                    : null,
                caves,
                volumeBiomes:
                    volumeBiome is null
                        ? null
                        : new[]
                        {
                            volumeBiome.Id,
                        },
                undergroundBiomes:
                    undergroundBiome is null
                        ? null
                        : [undergroundBiome.Id]);

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
