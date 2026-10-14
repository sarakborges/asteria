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
        var families = Directory.EnumerateFiles(Path.Combine(data, "blocks"), "log_*.json")
            .Where(file =>
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                return doc.RootElement.TryGetProperty("variant", out var variant) &&
                    variant.GetProperty("key").GetString() == "natural";
            })
            .Select(file => Path.GetFileNameWithoutExtension(file)["log_".Length..])
            .OrderByDescending(species => species.Length)
            .ThenBy(species => species, StringComparer.Ordinal)
            .ToArray();

        var checkedDefinitions = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(data, "blocks"), "*.json")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var paths = Strings(root).Where(value =>
                    value.StartsWith("textures/blocks/trees/", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (paths.Length == 0) continue;

            var folder = Assert.Single(paths.Select(path => path.Split('/')[3])
                .Distinct(StringComparer.Ordinal));
            var id = root.GetProperty("id").GetString()!;
            var type = id.StartsWith("asteria:leaf_", StringComparison.Ordinal) ? "leaf" :
                id.StartsWith("asteria:planks_", StringComparison.Ordinal) ? "planks" : "log";
            var family = families.FirstOrDefault(species =>
                id == "asteria:" + type + "_" + species ||
                id.StartsWith("asteria:" + type + "_" + species + "_",
                    StringComparison.Ordinal));
            Assert.False(string.IsNullOrEmpty(family),
                $"{id} must belong to an authored natural-log family.");
            Assert.True(folder == family || folder == "oak",
                $"{id} uses {folder}; only the family folder or Oak fallback is allowed.");
            if (root.TryGetProperty("variant", out var variant))
                Assert.Equal("asteria:log_" + family,
                    variant.GetProperty("family").GetString());
            checkedDefinitions++;
        }
        Assert.True(checkedDefinitions >= 6 * 36,
            "All 36 fantasy tree families must have their authored block variants.");
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
