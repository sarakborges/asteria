using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VolumeObjectDecorationTests
{
    private static readonly (string Biome, string Id, DecorationSupportSurface Surface,
        BlockFace[] Faces)[] Converted =
    [
        ("asteria:overworld/caverns", "mushroom_brown", DecorationSupportSurface.Floor, [BlockFace.Top]),
        ("asteria:overworld/caverns", "mushroom_purple", DecorationSupportSurface.Floor, [BlockFace.Top]),
        ("asteria:overworld/caverns", "roots_hanging", DecorationSupportSurface.Ceiling, [BlockFace.Bottom]),
        ("asteria:overworld/caverns", "fungus_shelf_wall", DecorationSupportSurface.Wall,
            [BlockFace.Left, BlockFace.Right, BlockFace.Front, BlockFace.Back]),
        ("asteria:overworld/floating_islands", "mushroom_purple", DecorationSupportSurface.Floor, [BlockFace.Top]),
        ("asteria:overworld/floating_islands", "vines_aether", DecorationSupportSurface.Ceiling, [BlockFace.Bottom]),
        ("asteria:overworld/floating_islands", "reed_cloud", DecorationSupportSurface.Floor, [BlockFace.Top]),
        ("asteria:umbral/wraith_grove", "mushroom_brown", DecorationSupportSurface.Floor, [BlockFace.Top]),
    ];

    [Fact]
    public void ConvertedVolumeVegetationHasUniqueObjectIdentityAndSupportedFaces()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        structures.ValidateBlocks(blocks);
        structures.ValidateObjects(objects);

        foreach (var (biomeId, name, face, faces) in Converted)
        {
            var id = "asteria:" + name;
            var rule = Assert.Single(biomes.Get(biomeId).Decorations,
                d => d.Block == id);
            Assert.True(rule.IsObject);
            Assert.Equal(face, rule.SupportSurface);
            Assert.False(blocks.TryGetId(id, out _));
            var definition = objects.Get(id);
            Assert.Equal(ObjectVisualKind.CrossedSprite, definition.Visual.Kind);
            Assert.Equal(faces, definition.PlacementFaces);
        }

        // Luminescent vegetation is now attached Objects with its authored RGB light.
        foreach (var id in new[] {"mushroom_glowcap", "fern_glow", "bloom_aether"})
        {
            var key = "asteria:" + id;
            Assert.False(blocks.TryGetId(key, out _));
            Assert.False(objects.Get(key).LightEmission.IsDark);
        }
    }

    [Fact]
    public void SelectorsResolveEachObjectSupportSurfaceIndependently()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson([
            ObjectJson("floor", "top"),
            ObjectJson("ceiling", "bottom"),
            ObjectJson("wall", "left","right","front","back")
        ], ObjectDefinition.Parse, obj => obj.Id);
        var biome = new BiomeDefinition("asteria:test/objects",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(80f, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            decorations: [
                new BiomeDecorationDefinition("asteria:floor", 1f, ["asteria:stone"],
                    isObject: true),
                new BiomeDecorationDefinition("asteria:ceiling", 1f, ["asteria:stone"],
                    supportSurface: DecorationSupportSurface.Ceiling, isObject: true),
                new BiomeDecorationDefinition("asteria:wall", 1f, ["asteria:stone"],
                    supportSurface: DecorationSupportSurface.Wall, isObject: true)
            ]);
        var biomes = new BiomeRegistry([biome]);
        biomes.ValidateObjects(objects);
        var field = new SurfaceDecorationField(2026, [biome], blocks, objects: objects);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var support = blocks.GetId("asteria:stone");
        var placement = new SurfacePlacementContext(79, 0);

        Assert.True(field.HasWallObjectsFor(biome.Id));
        Assert.Equal("asteria:floor", field.ObjectAt(sample, support,
            1, 2, placement, verticalY: 79));
        Assert.Equal("asteria:ceiling", field.ObjectAt(sample, support,
            1, 2, placement, verticalY: 79,
            supportSurface: DecorationSupportSurface.Ceiling));
        Assert.Equal("asteria:wall", field.WallObjectAt(sample, support, 1, 79, 2));
        Assert.True(field.BlockAt(sample, support, 1, 2, placement,
            verticalY: 79).IsAir);
    }

    [Fact]
    public void BiomeValidationRejectsMissingWallOrCeilingFaces()
    {
        var definitions = PackContentRegistry<ObjectDefinition>.FromJson([
            ObjectJson("top_only", "top")
        ], ObjectDefinition.Parse, obj => obj.Id);
        foreach (var face in new[] { DecorationSupportSurface.Ceiling,
            DecorationSupportSurface.Wall })
        {
            var biome = new BiomeDefinition("asteria:test/objects",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(80f, 0f, 64, 0f, 32),
                [new BiomeSurfaceLayerDefinition("asteria:stone")],
                decorations: [
                    new BiomeDecorationDefinition("asteria:top_only", 1f,
                        ["asteria:stone"], supportSurface: face, isObject: true)
                ]);
            Assert.Throws<ArgumentException>(() =>
                new BiomeRegistry([biome]).ValidateObjects(definitions));
        }
    }

    private static string ObjectJson(string name, params string[] faces) =>
        System.Text.Json.JsonSerializer.Serialize(new {
            id = "asteria:" + name,
            category = "foliage",
            icon = "textures/objects/grass.png",
            placementFaces = faces,
            visual = new { type = "crossedSprite", texture = "textures/objects/grass.png",
                width = .7f, height = .6f, planes = 2 }
        });

    private static IEnumerable<string> ReadJson(string category)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", category);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
