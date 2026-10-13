using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class OceanDecorationContentTests
{
    private static readonly string[] Names =
        ["seagrass", "kelp", "anemone_sea", "seashell", "starfish"];

    [Fact]
    public void OceanObjectsUseSubmergedHabitatRulesAndExistingSpriteModels()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, d => d.Id);
        biomes.ValidateObjects(objects);
        var ocean = biomes.Get("asteria:overworld/ocean");
        Assert.Equal(Names.OrderBy(x => x, StringComparer.Ordinal),
            ocean.Decorations.Select(d => d.Block["asteria:".Length..])
                .OrderBy(x => x, StringComparer.Ordinal));

        foreach (var name in Names)
        {
            var rule = Assert.Single(ocean.Decorations, d => d.Block == $"asteria:{name}");
            Assert.True(rule.IsObject);
            Assert.Equal(name switch
            {
                "seagrass" => 0.0675f,
                "kelp" => 0.035f,
                "anemone_sea" => 0.025f,
                "seashell" => 0.03f,
                "starfish" => 0.01875f,
                _ => throw new InvalidOperationException(),
            }, rule.Chance);
            Assert.Equal(DecorationFluidPlacement.Submerged, rule.FluidPlacement);
            Assert.Equal((int?)86, rule.Conditions!.MaxY);
            Assert.NotNull(rule.Cluster);
            Assert.Equal(4, rule.Cluster!.Octaves);
            Assert.Contains("asteria:sand", rule.SurfaceBlocks);
            Assert.NotNull(rule.HabitatWeights);
            Assert.False(blocks.TryGetId(rule.Block, out _));
            var definition = objects.Get(rule.Block);
            Assert.Equal("foliage", definition.Category);
            Assert.Equal(name is "seashell" or "starfish"
                ? ObjectVisualKind.ExtrudedSprite
                : ObjectVisualKind.CrossedSprite, definition.Visual.Kind);
            Assert.True(definition.Supports(BlockFace.Top));
            Assert.Equal(name is not "anemone_sea", definition.DropSelf);
            Assert.Equal($"textures/objects/{name}.png", definition.Visual.Texture);
            if (name == "anemone_sea")
            {
                Assert.Equal(.8f, definition.Visual.Width);
                Assert.Equal(.88f, definition.Visual.Height);
                Assert.Equal(4, definition.Visual.Planes);
                Assert.Equal(.5f, definition.Visual.AlphaCutoff);
                Assert.False(definition.WindSway);
                Assert.False(definition.Unlit);
                Assert.Equal(System.Numerics.Vector2.Zero, definition.PositionJitter);
            }
        }
        var grass = Assert.Single(ocean.Decorations, d => d.Block == "asteria:seagrass");
        var anemone = Assert.Single(ocean.Decorations, d => d.Block == "asteria:anemone_sea");
        Assert.True(grass.HabitatWeights!.For("sand_flats") > grass.HabitatWeights.For("rocky_reefs"));
        Assert.True(anemone.HabitatWeights!.For("rocky_reefs") > anemone.HabitatWeights.For("sand_flats"));
    }

    [Fact]
    public void OceanObjectsSpawnInWaterNotOnDrySandOrUnsupportedStone()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var ocean = BiomeRegistry.FromJson(ReadPackJson("biomes"))
            .Get("asteria:overworld/ocean");
        var objectCatalog = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, d => d.Id);
        var sample = new BiomeSample(ocean.Id,
            [new BiomeInfluence(ocean.Id, 1f)]);
        var a = new SurfaceDecorationField(2026, [ocean], blocks, objects: objectCatalog);
        var b = new SurfaceDecorationField(2026, [ocean], blocks, objects: objectCatalog);
        var sand = blocks.GetId("asteria:sand");
        var stone = blocks.GetId("asteria:stone");
        var context = new SurfacePlacementContext(75, 0);
        var foundObjects = new HashSet<string>();

        for (var z = -48; z <= 48; z++)
        for (var x = -48; x <= 48; x++)
        {
            // Every ocean floor decoration is an Object, so water stays
            // independent from a voxel block even at generated sites.
            Assert.True(a.BlockAt(sample, sand, x, z,
                suppliedPlacement: context, submerged: true).IsAir);
            Assert.True(b.BlockAt(sample, sand, x, z,
                suppliedPlacement: context, submerged: true).IsAir);
            Assert.True(a.BlockAt(sample, sand, x, z,
                suppliedPlacement: context).IsAir);
            Assert.True(a.BlockAt(sample, stone, x, z,
                suppliedPlacement: context, submerged: true).IsAir);
            Assert.Null(a.ObjectAt(sample, sand, x, z, placement: context));
            Assert.Null(a.ObjectAt(sample, stone, x, z,
                placement: context, submerged: true));
            var objectId = a.ObjectAt(sample, sand, x, z,
                placement: context, submerged: true);
            Assert.Equal(objectId, b.ObjectAt(sample, sand, x, z,
                placement: context, submerged: true));
            if (objectId is not null) foundObjects.Add(objectId);
        }
        Assert.Contains("asteria:seagrass", foundObjects);
        Assert.Contains("asteria:seashell", foundObjects);
        Assert.Contains("asteria:kelp", foundObjects);
    }

    private static IEnumerable<string> ReadPackJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
