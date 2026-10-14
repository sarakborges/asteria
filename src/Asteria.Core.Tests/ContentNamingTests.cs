using System.Text.Json;

namespace Asteria.Core.Tests;

public sealed class ContentNamingTests
{
    // Authoring taxonomy: new variations need no test edits; new types are reviewed here.
    private static readonly IReadOnlyDictionary<string, string[]> Types =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["blocks"] = ["basalt", "block", "bloom", "bone", "box", "bricks", "cactus",
                "clay", "cobble", "coral", "crystal", "dirt", "edelweiss", "fern", "flower",
                "fungus", "glass", "grass", "gravel", "ice", "lamp", "layer", "leaf", "lichen",
                "log", "moss", "mud", "mushroom", "planks", "pod", "portal", "roots", "rosette",
                "sand", "sandstone", "saxifrage", "sedge", "shell", "shrub", "snow", "spike",
                "starflower", "stone", "stonecrop", "terracotta", "thistle", "thorn", "thornbush",
                "tiles", "tuft", "yucca"],
            ["objects"] = ["anemone", "aster", "bloom", "bulrush", "cattail", "clover", "daisy",
                "fern", "fungus", "grass", "iris", "kelp", "mushroom", "orchid", "pad", "pebble",
                "reed", "roots", "sagebrush", "sapling", "seagrass", "seashell", "seedheads",
                "starfish", "stick", "vines", "wildflower"],
            ["items"] = ["boots", "chestplate", "essence", "fiber", "helmet", "leggings", "ore",
                "pebble", "slicer", "spores", "stick"],
            ["tools"] = ["axe", "brush", "bucket", "compass", "hatchet", "kit", "pickaxe",
                "shears", "shovel"],
            ["layers"] = ["layer", "lichen", "moss", "ore"],
            ["dyes"] = ["dye"],
            ["inventory_categories"] = ["blocks", "foliage", "light_sources", "materials", "tools"],
            ["attacks"] = ["punch"],
            ["fluids"] = ["lava", "water"],
            ["creatures"] = ["slime"],
            ["entities"] = ["player"],
            ["crafting_recipes"] = ["hatchet"],
            ["structures"] = ["arch", "bed", "boulder", "brush", "bush", "cactus", "caldera",
                "canopy", "chandelier", "cluster", "garden", "lake", "log", "mouth", "mushroom",
                "outcrop", "platform", "pond", "reef", "rib", "ring", "rock", "scrub", "segment",
                "serac", "snag", "spire", "stair", "stump", "thicket", "tree", "waterfall"],
            ["structure_sets"] = ["cluster", "grove", "thicket"],
            ["ambient_particles"] = ["ash", "bubbles", "fireflies", "matter", "sand", "snow"],
        };

    [Fact]
    public void ContentIdsUseTypeFirstAndMatchDefinitionFilenames()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");
        foreach (var (domain, types) in Types.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var files = Directory.EnumerateFiles(Path.Combine(data, domain), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            Assert.NotEmpty(files);
            foreach (var file in files)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var id = document.RootElement.GetProperty("id").GetString()!;
                if (domain != "inventory_categories")
                    Assert.StartsWith("asteria:", id);
                Assert.True(ids.Add(id), $"Duplicate {domain} ID: {id}");
                var local = id[(id.LastIndexOfAny([':', '/']) + 1)..];
                Assert.Matches("^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$", local);
                Assert.Equal(local, Path.GetFileNameWithoutExtension(file));
                Assert.True(types.Any(type => local == type ||
                                              local.StartsWith(type + "_", StringComparison.Ordinal)),
                    $"{id} must start with a reviewed {domain} type (see docs/content-naming.md).");
            }
        }
    }

    [Fact]
    public void TreeIdsAndVariantFamiliesFollowOakSpeciesOrder()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");
        var treeSpeciesByFolder = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(data, "blocks"), "log_*.json")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            if (!root.TryGetProperty("variant", out var variant) ||
                variant.GetProperty("key").GetString() != "natural")
                continue;
            var naturalLogPath = Strings(root).FirstOrDefault(value =>
                value.StartsWith("textures/blocks/trees/", StringComparison.Ordinal));
            if (naturalLogPath is null)
                continue;
            var species = root.GetProperty("id").GetString()!["asteria:log_".Length..];
            Assert.True(treeSpeciesByFolder.TryAdd(naturalLogPath.Split('/')[3], species));
        }

        var checkedDefinitions = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(data, "blocks"), "*.json")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var paths = Strings(root).Where(value =>
                    value.StartsWith("textures/blocks/trees/", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (paths.Length == 0)
                continue;

            var folder = Assert.Single(paths.Select(path => path.Split('/')[3])
                .Distinct(StringComparer.Ordinal));
            Assert.True(treeSpeciesByFolder.TryGetValue(folder, out var species),
                $"{folder} has no natural log family.");
            var id = root.GetProperty("id").GetString()!;
            var type = paths.Any(path => path.EndsWith("/leaf.png", StringComparison.Ordinal)) ? "leaf" :
                paths.Any(path => path.EndsWith("/planks.png", StringComparison.Ordinal)) ? "planks" : "log";
            var expected = "asteria:" + type + "_" + species;
            Assert.True(id == expected || id.StartsWith(expected + "_", StringComparison.Ordinal),
                $"{id} must use {expected} as its type/species prefix.");
            if (root.TryGetProperty("variant", out var variant))
                Assert.Equal("asteria:log_" + species, variant.GetProperty("family").GetString());
            checkedDefinitions++;
        }
        Assert.True(checkedDefinitions >= 44, "Every existing tree family must be covered.");
    }

    [Fact]
    public void InventoryResourceFilenamesUseSnakeCase()
    {
        var textures = Path.Combine(AppContext.BaseDirectory, "packs", "default", "resources", "textures");
        foreach (var domain in new[] { "blocks", "items", "objects", "tools", "layers", "creative_categories" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(textures, domain), "*.png",
                     SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            Assert.Matches("^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$", Path.GetFileNameWithoutExtension(file));
    }

    private static IEnumerable<string> Strings(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                yield return node.GetString()!;
                break;
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                foreach (var value in Strings(property.Value))
                    yield return value;
                break;
            case JsonValueKind.Array:
                foreach (var child in node.EnumerateArray())
                foreach (var value in Strings(child))
                    yield return value;
                break;
        }
    }
}
