using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VolcanoBiomeContentTests
{
    private static readonly string PackData = Path.Combine(
        AppContext.BaseDirectory, "packs", "default", "data");

    private static readonly (string Id, int X, int Z)[] Quadrants =
    [
        ("east", 1, 0),
        ("west", -1, 0),
        ("north", 0, 1),
        ("south", 0, -1),
    ];

    [Fact]
    public void ScorchedWastesIsAFlatBurntEcosystemRatherThanAVolcanoTerrainShape()
    {
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "scorched_wastes.json")));
        Assert.Equal("asteria:overworld/scorched_wastes", biome.Id);
        var rolling = Assert.IsType<BiomeRollingTerrainShapeDefinition>(
            biome.SurfaceTerrain!.Shape);
        Assert.InRange(rolling.Amplitude, 0f, 4f);
        Assert.Equal("asteria:basalt", biome.SurfaceLayers[^1].Block);
        Assert.Contains(biome.Decorations,
            item => item.Block == "asteria:thorn_ash");
        Assert.Contains(biome.Decorations,
            item => item.Block == "asteria:bloom_cinder");
        Assert.NotNull(biome.SurfaceHabitats);
    }

    [Theory]
    [InlineData(1.0f, 224)]
    [InlineData(0.5f, 112)]
    public void ScorchedWastesHasAContinuousInteriorWideEnoughForTheVolcano(
        float biomeSize, int minimumInteriorWidth)
    {
        var directory = Path.Combine(PackData, "biomes");
        var biomes = BiomeRegistry.FromJson(
            Directory.EnumerateFiles(directory, "*.json")
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var dimension = DimensionDefinitionJson.Parse(
            File.ReadAllText(Path.Combine(
                PackData, "dimensions", "overworld.json")));
        const string scorched = "asteria:overworld/scorched_wastes";
        var field = new BiomeField(0xA57E2026UL, dimension, biomes,
            biomeSizeMultiplier: biomeSize);
        var target = field.FindNearestSurfaceBiome(
            scorched, 0, 0, 6144);
        Assert.NotNull(target);

        var sampler = field.CreateSampler();
        var longestRun = 0;
        for (var z = target.Z - 192; z <= target.Z + 192; z += 8)
        {
            var run = 0;
            for (var x = target.X - 192; x <= target.X + 192; x += 8)
            {
                if (sampler.Sample(x, z).Primary == scorched)
                {
                    run++;
                    longestRun = Math.Max(longestRun, run);
                }
                else
                {
                    run = 0;
                }
            }
        }

        Assert.True(longestRun * 8 >= minimumInteriorWidth,
            $"Scorched Wastes collapsed to a narrow strip: longest continuous " +
            $"row was {longestRun * 8} blocks (biome size {biomeSize}).");
    }

    [Fact]
    public void VolcanoUsesOnlyNativeStructuresAndFourExplicitConnectors()
    {
        var root = Load("caldera_volcanic");
        Assert.Equal(4, root.Connectors.Count);
        Assert.All(root.Connectors,
            connector => Assert.NotNull(connector.Target));
        Assert.Equal("asteria:volcano", root.GroupReference);
        Assert.Equal(5, root.Generation.MinimumPieces);
        Assert.True(root.Locatable);
        Assert.True(root.Generation.ReserveSpace);
        Assert.Equal(StructureReplacePolicy.Any, root.Generation.ReplacePolicy);

        foreach (var (id, _, _) in Quadrants)
        {
            var piece = Load("caldera_volcanic_" + id);
            Assert.Contains(root.Connectors,
                c => c.Target == piece.Id);
            Assert.Single(piece.Connectors);
            Assert.Null(piece.Connectors[0].Target);
            Assert.False(piece.Locatable);
            Assert.Equal(1f, piece.Restrictions.RequiredBiomeCoverage);
            Assert.True(piece.Voxels.Count > 5000);
            Assert.True(piece.Voxels.Count + piece.FluidVoxels.Count +
                piece.ClearVoxels.Count < StructureDefinition.MaximumVoxelCount);
        }
        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        Assert.Contains("asteria:overworld/scorched_wastes",
            dimension.SurfaceBiomes);
        Assert.DoesNotContain("asteria:overworld/volcano",
            dimension.SurfaceBiomes);
        Assert.Contains(dimension.GeneratedSurfaceStructures,
            rule => rule.Biome == "asteria:overworld/scorched_wastes" &&
                rule.Structure == root.GroupReference);
    }

    [Fact]
    public void ConnectedTemplatesFormAContinuousCraterOfFiftyBlocksDiameter()
    {
        var heights = new Dictionary<(int X, int Z), int>();
        var lava = new HashSet<(int X, int Y, int Z)>();
        foreach (var (definition, originX, originZ) in
            new[] { (Load("caldera_volcanic"), 0, 0) }
                .Concat(Quadrants.Select(piece =>
                    (Load("caldera_volcanic_" + piece.Id), piece.X, piece.Z))))
        {
            foreach (var block in definition.Voxels)
            {
                var position = (originX + block.X, originZ + block.Z);
                if (!heights.TryGetValue(position, out var existing) ||
                    block.Y > existing)
                    heights[position] = block.Y;
            }
            foreach (var fluid in definition.FluidVoxels)
            {
                Assert.Equal("asteria:lava", fluid.Fluid);
                Assert.True(lava.Add((
                    originX + fluid.X, fluid.Y, originZ + fluid.Z)),
                    "Connected pieces must never overlap fluid voxels.");
            }
        }

        for (var z = -44; z <= 44; z++)
        for (var x = -44; x <= 44; x++)
        {
            if (x * x + z * z > 44 * 44)
                continue;
            Assert.True(heights.ContainsKey((x, z)),
                $"Disconnected mountain wall at ({x}, {z}).");
        }

        Assert.Equal(6, heights[(0, 0)]);
        Assert.Contains((0, 13, 0), lava);
        for (var direction = 0; direction < 72; direction++)
        {
            var radians = direction * Math.Tau / 72d;
            int At(int radius) => heights[(
                (int)Math.Round(radius * Math.Cos(radians)),
                (int)Math.Round(radius * Math.Sin(radians)))];
            Assert.True(At(24) >= 27,
                $"Crater rim cannot contain lava along spoke {direction}.");
            Assert.True(At(32) < At(24),
                $"The flank must slope down from the rim: spoke {direction}.");
            Assert.True(At(43) <= 3,
                $"The mountain must meet the ground at spoke {direction}.");
        }
    }

    [Fact]
    public void ConnectedVolcanoMaterializesAcrossActualChunkBoundaries()
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
                "structure":"asteria:volcano",
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
            new BlockDefinition("asteria:cobble_basalt")
        ]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:lava",
                new FluidColor(255, 90, 10), 0.8f)
        ]);
        var structures = new StructureRegistry(
            new[] { Load("caldera_volcanic") }
                .Concat(Quadrants.Select(piece =>
                    Load("caldera_volcanic_" + piece.Id))));
        var generator = new BiomeWorldGenerator(
            77UL, dimension, blocks, fluids,
            new BiomeRegistry([biome]), structures);
        var placements = generator.SurfaceStructuresIntersecting(
            96, 96, 64, 64);
        Assert.Equal(5, placements.Count);
        var root = Assert.Single(placements,
            p => p.StructureId == "asteria:caldera_volcanic");

        void AssertBlock(int x, int y, int z, string block)
        {
            var location = VoxelCoordinates.FromWorld(x, y, z);
            var chunk = generator.Materialize(location.Chunk);
            Assert.Equal(blocks.GetId(block),
                chunk.GetBlock(location.Local.X,
                    location.Local.Y, location.Local.Z));
        }

        var cx = root.AnchorX;
        var cz = root.AnchorZ;
        var y0 = root.AnchorY;
        AssertBlock(cx, y0 + 6, cz, "asteria:basalt");
        AssertBlock(cx + 25, y0 + 30, cz, "asteria:basalt");
        var lavaPosition = VoxelCoordinates.FromWorld(cx, y0 + 13, cz);
        var lavaChunk = generator.Materialize(lavaPosition.Chunk);
        Assert.Equal(fluids.GetId("asteria:lava"),
            lavaChunk.GetFluid(lavaPosition.Local.X, lavaPosition.Local.Y,
                lavaPosition.Local.Z).Fluid);
    }

    [Fact]
    public void ActualOverworldGeneratesALocatableCompleteVolcano()
    {
        var biomeDocuments = ReadPackDirectory("biomes");
        var biomes = BiomeRegistry.FromJson(biomeDocuments);
        var blocks = BlockRegistry.FromJson(ReadPackDirectory("blocks"));
        var fluids = FluidRegistry.FromJson(ReadPackDirectory("fluids"));
        var structures = StructureRegistry.FromJson(
            ReadPackDirectory("structures"));

        // Keep all actual Overworld biome placement rules, terrain and seed
        // behavior, but query only the volcano structure rule. Otherwise the
        // test would spend its budget placing unrelated forests and boulders.
        var document = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(PackData, "dimensions",
                "overworld.json")))!.AsObject();
        var volcanoRule = document["generatedSurfaceStructures"]!.AsArray()
            .Single(rule => rule!["structure"]!.GetValue<string>() ==
                "asteria:volcano")!.DeepClone();
        document["generatedSurfaceStructures"] =
            new System.Text.Json.Nodes.JsonArray(volcanoRule);
        var dimension = DimensionDefinitionJson.Parse(document.ToJsonString());

        var generator = new BiomeWorldGenerator(
            DimensionSeed.Derive(0xA57E2026UL, dimension.Id),
            dimension, blocks, fluids, biomes, structures);
        Assert.True(generator.HasGeneratedSurfaceStructure("asteria:volcano"));
        var biome = generator.Biomes.FindNearestSurfaceBiome(
            "asteria:overworld/scorched_wastes", 0, 0, 4096);
        Assert.NotNull(biome);
        var nearest = generator.FindNearestSurfaceStructure(
            "asteria:volcano", biome.X, biome.Z, 1536);
        Assert.True(nearest.HasValue,
            "The actual Overworld biome field must produce a full volcano, " +
            "not an ungenerated landmark or a lone lava pillar.");
        var result = nearest!.Value;
        Assert.Equal("asteria:caldera_volcanic", result.StructureId);
        var pieces = generator.SurfaceStructuresIntersecting(
            result.AnchorX - 52, result.AnchorZ - 52, 105, 105)
            .Where(piece => piece.PlacementAnchorX == result.PlacementAnchorX &&
                piece.PlacementAnchorZ == result.PlacementAnchorZ)
            .ToArray();
        Assert.Equal(5, pieces.Length);
        Assert.All(pieces, piece =>
            Assert.Equal("asteria:volcano", piece.Reference));
    }

    private static IEnumerable<string> ReadPackDirectory(string category) =>
        Directory.EnumerateFiles(Path.Combine(PackData, category), "*.json")
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(File.ReadAllText);

    private static StructureDefinition Load(string id) =>
        StructureDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "structures", id + ".json")));
}
