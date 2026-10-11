using System.Text.Json;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BiomeContentDeduplicationTests
{
    private static readonly (string OldId, string ReusedId, string Texture)[] Removed =
    [
        ("mossy_willow_log", "log_willow", "textures/blocks/mossy_willow_log.png"),
        ("enchanted_hollow_wood", "log_enchanted_hollow", "textures/blocks/enchanted_hollow_wood_top.png"),
        ("sandstone_spur", "sandstone_spike", "textures/blocks/sandstone_spur.png"),
        ("rock_spur", "stone_spike", "textures/blocks/rock_spur.png"),
        ("glacial_shard", "ice_spike", "textures/blocks/glacial_shard.png"),
        ("dry_exposed_roots", "exposed_roots", "textures/objects/dry_exposed_roots.png"),
    ];

    [Fact]
    public void RemovedDefinitionsReuseExistingContentWithoutDanglingPackReferences()
    {
        var pack = Path.Combine(AppContext.BaseDirectory, "packs", "default");
        var data = Path.Combine(pack, "data");
        var blocksDirectory = Path.Combine(data, "blocks");
        var blocks = BlockRegistry.FromJson(ReadJson(blocksDirectory));
        var biomes = BiomeRegistry.FromJson(ReadJson(Path.Combine(data, "biomes")));
        var structures = StructureRegistry.FromJson(ReadJson(Path.Combine(data, "structures")));

        biomes.ValidateBlocks(blocks);
        structures.ValidateBlocks(blocks);

        var references = Directory.EnumerateFiles(Path.Combine(data, "biomes"), "*.json")
            .Concat(Directory.EnumerateFiles(Path.Combine(data, "structures"), "*.json"))
            .Select(File.ReadAllText)
            .ToArray();

        foreach (var (oldId, reusedId, texture) in Removed)
        {
            Assert.False(File.Exists(Path.Combine(blocksDirectory, oldId + ".json")));
            Assert.True(File.Exists(Path.Combine(blocksDirectory, reusedId + ".json")));
            Assert.False(File.Exists(Path.Combine(pack, "resources",
                texture.Replace('/', Path.DirectorySeparatorChar))));
            Assert.DoesNotContain(references,
                json => json.Contains("asteria:" + oldId + "\"", StringComparison.Ordinal));

            foreach (var language in new[] { "english", "portuguese_brazil", "spanish" })
            {
                var localization = Path.Combine(data, "localization", language, "blocks.json");
                using var catalog = JsonDocument.Parse(File.ReadAllText(localization));
                Assert.False(catalog.RootElement.TryGetProperty("asteria:" + oldId, out _));
                Assert.True(catalog.RootElement.TryGetProperty("asteria:" + reusedId, out _));
            }
        }
    }

    private static IEnumerable<string> ReadJson(string directory) =>
        Directory.EnumerateFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
