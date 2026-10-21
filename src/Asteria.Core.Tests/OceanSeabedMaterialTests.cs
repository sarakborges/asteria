using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class OceanSeabedMaterialTests
{
    private static readonly string PackData =
        Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");

    [Fact]
    public void OceanSeabedUsesOrganicThreeDimensionalGeologyWithoutHorizontalBands()
    {
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "ocean.json")));
        var geology = Assert.IsType<BiomeGeologyDefinition>(biome.Geology);

        Assert.Equal(1u, geology.SurfaceCoverDepth);
        Assert.Equal("asteria:sand", biome.SurfaceLayers[0].Block);
        Assert.Equal(1u, biome.SurfaceLayers[0].Depth);
        Assert.Null(biome.SurfaceLayers[0].Patch);
        Assert.InRange(geology.VerticalScale / (double)geology.Scale, 0.5d, 0.75d);

        var blocks = BlockRegistry.FromJson(
            Directory.EnumerateFiles(Path.Combine(PackData, "blocks"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var materials = new BiomeSurfaceMaterialField(
            7808508665006554090UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        const int sampleCount = 97;
        var grid = new BlockRuntimeId[sampleCount, sampleCount];
        var seen = new HashSet<BlockRuntimeId>();
        for (var z = 0; z < sampleCount; z++)
        for (var x = 0; x < sampleCount; x++)
        {
            Assert.True(materials.TryGeologyBlockAt(
                sample, 355 + (x - 48) * 4, 82, -230 + (z - 48) * 4, out var block));
            grid[x, z] = block;
            seen.Add(block);
        }

        var xTransitions = 0;
        var zTransitions = 0;
        for (var z = 0; z < sampleCount; z++)
        for (var x = 0; x < sampleCount - 1; x++)
            if (grid[x, z] != grid[x + 1, z])
                xTransitions++;
        for (var z = 0; z < sampleCount - 1; z++)
        for (var x = 0; x < sampleCount; x++)
            if (grid[x, z] != grid[x, z + 1])
                zTransitions++;

        Assert.True(seen.Count >= 3,
            "The ocean floor should contain the host rock and multiple geological deposits.");
        Assert.True(xTransitions > 0 && zTransitions > 0,
            "The volumetric geology should vary along both horizontal axes.");
        var larger = Math.Max(xTransitions, zTransitions);
        var smaller = Math.Min(xTransitions, zTransitions);
        Assert.True(smaller >= larger * 0.45,
            $"Geology variation is strongly directional: X={xTransitions}, Z={zTransitions}.");

        var vertical = new HashSet<BlockRuntimeId>();
        for (var y = 8; y <= 168; y += 4)
        {
            Assert.True(materials.TryGeologyBlockAt(sample, 355, y, -230, out var block));
            vertical.Add(block);
        }
        Assert.True(vertical.Count > 1,
            "The material distribution must change within a vertical world-space column.");
    }

}
