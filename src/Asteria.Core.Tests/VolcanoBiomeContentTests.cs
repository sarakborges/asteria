using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VolcanoBiomeContentTests
{
    private static readonly string PackData = Path.Combine(
        AppContext.BaseDirectory, "packs", "default", "data");

    [Fact]
    public void ScorchedWastesIsBurntLandscapeAndNotAnElevatedVolcanoBiome()
    {
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "scorched_wastes.json")));
        Assert.Equal("asteria:overworld/scorched_wastes", biome.Id);
        var terrain = Assert.IsType<BiomeRollingTerrainShapeDefinition>(
            biome.SurfaceTerrain!.Shape);
        Assert.InRange(terrain.BaseHeight, 0f, 8f);
        Assert.InRange(terrain.Amplitude, 0f, 4f);
        Assert.Equal("asteria:basalt", biome.SurfaceLayers[^1].Block);
        Assert.Contains(biome.Decorations,
            item => item.Block == "asteria:ash_thorn");
        Assert.Contains(biome.Decorations,
            item => item.Block == "asteria:cinder_bloom");
        Assert.NotNull(biome.SurfaceHabitats);
    }

    [Fact]
    public void CalderaIsAReusableStructureWithAnEnclosedFiftyBlockCrater()
    {
        var structure = StructureDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "structures", "volcanic_caldera.json")));
        var shape = Assert.IsType<StructureCalderaDefinition>(structure.Caldera);
        Assert.Equal(25, shape.CraterRadius);
        Assert.Equal(44, shape.Radius);
        Assert.Equal(14, shape.BiomeMargin);
        Assert.True(shape.Height > shape.LavaLevel + 6);
        Assert.Equal("asteria:lava", shape.Fluid);
        Assert.Equal("asteria:basalt", shape.WallBlock);
        Assert.Equal(StructureReplacePolicy.Any, structure.Generation.ReplacePolicy);
        Assert.True(structure.Generation.ReserveSpace);
        Assert.Equal(1f, structure.Restrictions.RequiredBiomeCoverage);

        for (var angle = 0; angle < 360; angle++)
        {
            var radians = angle * Math.PI / 180d;
            int At(int radius)
            {
                var x = (int)Math.Round(radius * Math.Cos(radians));
                var z = (int)Math.Round(radius * Math.Sin(radians));
                return shape.SurfaceHeightAt(x, z);
            }

            Assert.True(At(0) < shape.LavaLevel, "The crater floor must be submerged.");
            Assert.True(At(24) > shape.LavaLevel + 8, "Every rim direction must confine lava.");
            Assert.True(At(32) < At(24), "Exterior slopes must descend from the crater rim.");
            Assert.True(At(44) <= 2, "Mountain base must meet the surrounding terrain.");
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StructureCalderaDefinition(44, 25, 9, 30, 6,
                29, 6, 14, "asteria:basalt", "asteria:basalt_cobble", "asteria:lava"));
    }

    [Fact]
    public void OverworldRegistersCalderaAsStructureInsideScorchedWastes()
    {
        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        Assert.Contains("asteria:overworld/scorched_wastes",
            dimension.SurfaceBiomes);
        Assert.DoesNotContain("asteria:overworld/volcano",
            dimension.SurfaceBiomes);
        Assert.Contains(dimension.GeneratedSurfaceStructures,
            rule => rule.Biome == "asteria:overworld/scorched_wastes" &&
                rule.Structure == "asteria:volcanic_caldera");
    }

    [Fact]
    public void CalderaMaterializesSolidRimAndLavaInIndependentChunks()
    {
        var dimension = DimensionDefinitionJson.Parse(
            """
            {
              "id":"asteria:test",
              "seaLevel":40,
              "gravityStrength":18,
              "spawn":{"x":0,"z":0},
              "environment":{
                "backgroundColor":"000000",
                "ambientColor":"FFFFFF",
                "ambientEnergy":1,
                "fogColor":"000000",
                "fogDensity":0
              },
              "surfaceBiomes":["asteria:test/scorched"],
              "generatedSurfaceStructures":[{
                "biome":"asteria:test/scorched",
                "structure":"asteria:volcanic_caldera",
                "spacing":256,"jitter":0,"chance":1
              }]
            }
            """);
        var biome = BiomeDefinitionJson.Parse(
            """
            {
              "id":"asteria:test/scorched",
              "surfaceLayout":{"regionSize":{"min":384,"max":384}},
              "surfaceTerrain":{
                "type":"rolling","baseHeight":5,
                "amplitude":0,"scale":0.009,
                "detailAmplitude":0,"detailScale":0.027
              },
              "palette":{"default":[{"block":"asteria:basalt"}]}
            }
            """);
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:basalt"),
            new BlockDefinition("asteria:basalt_cobble")
        ]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:lava", new FluidColor(255, 90, 10), 0.8f)
        ]);
        var structure = StructureDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "structures", "volcanic_caldera.json")));
        var generator = new BiomeWorldGenerator(
            77UL, dimension, blocks, fluids,
            new BiomeRegistry([biome]),
            new StructureRegistry([structure]));
        var placement = Assert.Single(generator.SurfaceStructuresIntersecting(
            96, 96, 64, 64));
        var shape = Assert.IsType<StructureCalderaDefinition>(structure.Caldera);
        var x = placement.AnchorX;
        var z = placement.AnchorZ;
        var floorY = placement.AnchorY + shape.FloorHeight;
        var fluidY = placement.AnchorY + shape.LavaLevel;
        var rimY = placement.AnchorY + shape.Height;
        var centerChunk = generator.Materialize(new ChunkCoord(
            x / Chunk.Size, floorY / Chunk.Size, z / Chunk.Size));
        Assert.Equal(blocks.GetId(shape.FloorBlock),
            centerChunk.GetBlock(x % Chunk.Size, floorY % Chunk.Size,
                z % Chunk.Size));
        var lavaChunk = generator.Materialize(new ChunkCoord(
            x / Chunk.Size, fluidY / Chunk.Size, z / Chunk.Size));
        Assert.Equal(fluids.GetId(shape.Fluid),
            lavaChunk.GetFluid(x % Chunk.Size, fluidY % Chunk.Size,
                z % Chunk.Size).Fluid);
        var rimX = x + shape.CraterRadius;
        var rimChunk = generator.Materialize(new ChunkCoord(
            rimX / Chunk.Size, rimY / Chunk.Size, z / Chunk.Size));
        Assert.Equal(blocks.GetId(shape.WallBlock),
            rimChunk.GetBlock(rimX % Chunk.Size, rimY % Chunk.Size,
                z % Chunk.Size));
        Assert.All(generator.SurfaceStructuresIntersecting(96,96,64,64),
            p => Assert.Equal("asteria:volcanic_caldera", p.StructureId));
    }
}
