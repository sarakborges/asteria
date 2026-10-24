using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FloatingIslandContentTests
{
    private static readonly string[] NewObjects =
        ["reed_cloud", "bloom_aether", "vines_aether"];

    [Fact]
    public void DefaultIslandObjectsAreOnlyAuthoredOnAdditiveVolume()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var islands = biomes.Get("asteria:overworld/floating_islands");
        Assert.Null(islands.SurfaceLayout);
        Assert.Equal(VolumeBiomePlacement.Additive, islands.VolumeLayout!.Placement);
        Assert.Single(islands.Terrain3d!.Additive);
        Assert.Equal(200, islands.Terrain3d.Additive[0].MinY);
        Assert.Equal(280, islands.Terrain3d.Additive[0].MaxY);

        foreach (var name in NewObjects)
        {
            var rule = Assert.Single(islands.Decorations,
                d => d.Block == $"asteria:{name}");
            Assert.NotNull(rule.Cluster);
            Assert.Equal((int?)200, rule.Conditions!.MinY);
            Assert.Equal((int?)283, rule.Conditions.MaxY);
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            Assert.Contains("asteria:block_grass", rule.SurfaceBlocks);
            Assert.True(rule.IsObject);
            Assert.False(blocks.TryGetId(rule.Block, out _));
            var objects = PackContentRegistry<ObjectDefinition>.FromJson(
                ReadJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
            var obj = objects.Get(rule.Block);
            Assert.Equal(ObjectVisualKind.CrossedSprite, obj.Visual.Kind);
            Assert.True(obj.WindSway);
            Assert.True(obj.Supports(name == "vines_aether"
                ? BlockFace.Bottom : BlockFace.Top));
            Assert.Equal(name == "vines_aether"
                    ? DecorationSupportSurface.Ceiling : DecorationSupportSurface.Floor,
                rule.SupportSurface);
        }

        var bloomObjects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
    }

    [Fact]
    public void CeilingOnlyVinesDoNotSpawnFromIslandFloor()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone")
        ]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson([
            """
            {"id":"asteria:vines_aether","category":"foliage",
             "icon":"textures/objects/vines_aether.png","placementFaces":["bottom"],
             "visual":{"type":"crossedSprite",
                "texture":"textures/objects/vines_aether.png",
                "width":0.72,"height":0.95,"planes":2}}
            """
        ], ObjectDefinition.Parse, obj => obj.Id);
        var islands = new BiomeDefinition(
            "asteria:test/islands", null, null,
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            [new BiomeDecorationDefinition("asteria:vines_aether",
                chance: 1f, surfaceBlocks: ["asteria:stone"],
                supportSurface: DecorationSupportSurface.Ceiling, isObject: true)],
            volumeLayout: new BiomeVolumeLayoutDefinition(
                placement: VolumeBiomePlacement.Additive),
            terrain3d: new BiomeTerrain3dDefinition([
                new BiomeAdditiveDensityDefinition(
                    minY: 200, maxY: 280, horizontalScale: 112,
                    detailScale: 40, coverage: 0.55f,
                    roughness: 0.18f, densityScale: 28,
                    verticalFalloff: 1, horizontalFalloff: 1,
                    densityBias: 0)
            ]));
        var field = new SurfaceDecorationField(17, [islands], blocks, objects: objects);
        var sample = new BiomeSample(islands.Id,
            [new BiomeInfluence(islands.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");
        Assert.True(field.BlockAt(sample, stone, 3, 7,
            verticalY: 240).IsAir);
        Assert.Equal("asteria:vines_aether",
            field.ObjectAt(sample, stone, 3, 7,
                verticalY: 240,
                supportSurface: DecorationSupportSurface.Ceiling));
        Assert.True(field.HasCeilingDecorationsFor(islands.Id));
    }

    [Fact]
    public void VinesHangBelowActualIslandSolidsWithoutReplacingTerrain()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var dimensions = DimensionRegistry.FromJson(ReadJson("dimensions"));
        var dimension = dimensions.Get(DimensionId.Overworld);
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes").Select(json =>
        {
            if (!json.Contains(
                "\"id\": \"asteria:overworld/floating_islands\"",
                StringComparison.Ordinal))
                return json;

            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            // A guaranteed rule makes this a test of face selection and
            // occupancy, not of probabilistic decorating frequency.
            node["decorations"] = System.Text.Json.Nodes.JsonNode.Parse("""
                [
                  {"object":"asteria:vines_aether","chance":1,
                   "surfaceBlocks":["asteria:block_grass","asteria:dirt","asteria:stone","asteria:gravel","asteria:cobble_stone"],
                   "supportSurface":"ceiling"}
                ]
                """);
            return node.ToJsonString();
        }));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        var structureSets = StructureSetRegistry.FromJson(ReadJson("structure_sets"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var generator = new BiomeWorldGenerator(
            DimensionSeed.Derive(0xA57E_2026UL, dimension.Id),
            dimension, blocks, fluids, biomes,
            structures, structureSets,
            generation: new WorldGenerationOptions(spawnStructures: false),
            objects: objects);

        const string volumeId = "asteria:overworld/floating_islands";
        (int X, int Z)? islandColumn = null;
        for (var z = -4096; z <= 4096 && islandColumn is null; z += 32)
        for (var x = -4096; x <= 4096 && islandColumn is null; x += 32)
        {
            if (generator.VolumeBiomes.Sample(x, 240, z)?.Primary == volumeId &&
                generator.DensityAt(x, 240, z) >= 0d &&
                generator.DensityAt(x, 192, z) < 0d)
                islandColumn = (x, z);
        }
        Assert.NotNull(islandColumn);

        var (worldX, worldZ) = islandColumn!.Value;
        (int X, int Y, int Z)? target = null;
        for (var y = 200; y < 280; y++)
        {
            if (generator.DensityAt(worldX, y, worldZ) < 0d &&
                generator.DensityAt(worldX, y + 1, worldZ) >= 0d &&
                generator.VolumeBiomes.Sample(worldX, y + 1, worldZ)?
                    .Primary == volumeId)
            {
                target = (worldX, y, worldZ);
                break;
            }
        }

        Assert.NotNull(target);
        var (x0, y0, z0) = target!.Value;
        var (decorationChunk, local) =
            VoxelCoordinates.FromWorld(x0, y0, z0);
        var (supportChunk, supportLocal) =
            VoxelCoordinates.FromWorld(x0, y0 + 1, z0);
        var generated = generator.Materialize(decorationChunk);
        var supported = generator.Materialize(supportChunk);

        Assert.True(generated.GetBlock(local.X, local.Y, local.Z).IsAir);
        Assert.False(supported.GetBlock(
            supportLocal.X, supportLocal.Y, supportLocal.Z).IsAir);
        var vine = Assert.Single(supported.ObjectsAt(
            supportLocal.X, supportLocal.Y, supportLocal.Z),
            obj => obj.Id == "asteria:vines_aether");
        Assert.Equal(BlockFace.Bottom, vine.Face);
        Assert.True(generator.DensityAt(x0, y0, z0) < 0d);
        Assert.True(generator.DensityAt(x0, y0 + 1, z0) >= 0d);
        Assert.Equal(generated.GetCell(local.X, local.Y, local.Z),
            generator.Materialize(decorationChunk)
                .GetCell(local.X, local.Y, local.Z));
    }

    private static IEnumerable<string> ReadJson(string directory)
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
