using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class TreeHabitatAssignmentsTests
{
    [Fact]
    public void EveryRetainedTreeHasAnAuthoredHabitatAndValidBlockReferences()
    {
        var pack = Path.Combine(AppContext.BaseDirectory, "packs", "default");
        var blocks = BlockRegistry.FromJson(ReadJson(pack, "blocks"));
        var structures = StructureRegistry.FromJson(ReadJson(pack, "structures"));
        structures.ValidateBlocks(blocks);

        var dimensions = DimensionRegistry.FromJson(ReadJson(pack, "dimensions"));
        var overworld = dimensions.Get(DimensionId.Overworld);
        var umbral = dimensions.Get(new DimensionId("asteria:umbral"));
        var biomes = BiomeRegistry.FromJson(ReadJson(pack, "biomes"));
        var islands = biomes.Get("asteria:overworld/floating_islands");

        var surface = overworld.GeneratedSurfaceStructures
            .Concat(umbral.GeneratedSurfaceStructures)
            .Select(rule => rule.Structure)
            .ToHashSet(StringComparer.Ordinal);
        var volume = islands.VolumeStructures.Select(rule => rule.Structure)
            .ToHashSet(StringComparer.Ordinal);

        var texturedSpecies = Directory.EnumerateDirectories(Path.Combine(
                pack, "resources", "textures", "blocks", "trees"))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(37, texturedSpecies.Count);

        foreach (var species in texturedSpecies.Append("willow"))
        {
            var id = "asteria:tree_" + species;
            Assert.True(surface.Contains(id) || volume.Contains(id),
                species + " must belong to a generated biome.");
            Assert.NotEmpty(structures.ResolveReference(id));
            Assert.True(blocks.TryGetId("asteria:log_" + species, out _));
            Assert.True(blocks.TryGetId("asteria:leaf_" + species, out _));
        }

        foreach (var id in surface.Concat(volume).Where(
                     value => value.StartsWith("asteria:tree_", StringComparison.Ordinal)))
            Assert.NotEmpty(structures.ResolveReference(id));
    }

    private static IEnumerable<string> ReadJson(string pack, string kind) =>
        Directory.EnumerateFiles(Path.Combine(pack, "data", kind), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
