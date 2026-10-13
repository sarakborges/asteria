using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BiomeContentDeduplicationTests
{
    private static readonly (string OldId, string ReusedId, string Texture)[] Removed =
    [
        ("mossy_willow_log", "log_willow", "textures/blocks/mossy_willow_log.png"),
        ("enchanted_hollow_wood", "log_enchanted_tree_hollow", "textures/blocks/enchanted_hollow_wood_top.png"),
        ("sandstone_spur", "spike_sandstone", "textures/blocks/sandstone_spur.png"),
        ("rock_spur", "spike_stone", "textures/blocks/rock_spur.png"),
        ("glacial_shard", "spike_ice", "textures/blocks/glacial_shard.png"),
        ("dry_exposed_roots", "roots_exposed", "textures/objects/dry_exposed_roots.png"),
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
        }
    }

    private static IEnumerable<string> ReadJson(string directory) =>
        Directory.EnumerateFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
