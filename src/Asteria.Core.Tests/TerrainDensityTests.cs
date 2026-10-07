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
    public void FloatingFormationExtendsSurfaceRangeAndMaterializesAboveBase()
    {
        var generator = Generator(
            floating: new BiomeFloatingFormationDefinition(
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
                y: 96);
        var top =
            generator.SurfaceHeight(
                point.X,
                point.Z);
        Assert.InRange(
            top,
            97,
            112);
        Assert.True(
            generator.DensityAt(
                point.X,
                96,
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
            floating: new BiomeFloatingFormationDefinition(
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
            new BiomeFloatingFormationDefinition(
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
              "terrain3d":{"floatingFormation":{
                "minY":80,"maxY":112,"horizontalScale":64,
                "detailScale":24,"coverage":0.55,"roughness":0.18,
                "densityScale":28
              }}
            }
            """);
        Assert.Null(
            biome.SurfaceLayout);
        Assert.NotNull(
            biome.VolumeLayout);
        Assert.Equal(
            80,
            biome.Terrain3d!.FloatingFormation!.MinY);
        Assert.Equal(
            112,
            biome.Terrain3d.FloatingFormation.MaxY);

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
              "caves":{
                "minDepth":10,"maxDepth":120,
                "horizontalScale":56,"verticalScale":36,
                "noiseHalfWidth":0.18,"densityScale":24,
                "boundaryFade":8
              }
            }
            """);
        Assert.Equal(120u, dimension.Caves!.MaxDepth);
    }

    private static BlockRuntimeId Block(string id) =>
        Blocks.GetId(id);

    private static readonly BlockRegistry Blocks = new(
    [
        new BlockDefinition("asteria:stone"),
        new BlockDefinition("asteria:grass_block"),
        new BlockDefinition("asteria:dirt"),
        new BlockDefinition("asteria:sphere_shell"),
    ]);

    private static DimensionCaveDefinition WideCaves() =>
        new(
            minDepth: 10,
            maxDepth: 80,
            horizontalScale: 56,
            verticalScale: 36,
            noiseHalfWidth: 1f,
            densityScale: 24f,
            boundaryFade: 6);

    private static BiomeWorldGenerator Generator(
        DimensionCaveDefinition? caves = null,
        BiomeFloatingFormationDefinition? floating = null,
        int? floorY = null,
        int? roofY = null)
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
        var volumeBiome =
            floating is
                { } authored
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
                            authored),
                    volumeLayout:
                        new BiomeVolumeLayoutDefinition())
                : null;
        var definitions =
            volumeBiome is null
                ? new[]
                {
                    surfaceBiome,
                }
                : new[]
                {
                    surfaceBiome,
                    volumeBiome,
                };
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
                        });

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
