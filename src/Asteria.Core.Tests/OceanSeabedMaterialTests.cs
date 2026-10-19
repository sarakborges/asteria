using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class OceanSeabedMaterialTests
{
    private static readonly string PackData =
        Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");

    [Fact]
    public void OceanSeabedVerticalCrossSectionPreservesPatchAndStrataLayers()
    {
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "ocean.json")));
        var blocks = BlockRegistry.FromJson(
            Directory.EnumerateFiles(Path.Combine(PackData, "blocks"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var materials = new BiomeSurfaceMaterialField(
            7808508665006554090UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var placement = new SurfacePlacementContext(82, 0);

        // Model a vertical cut through the reported seabed: X runs along the
        // wall and depth runs vertically into the terrain.
        const int width = 97;
        const uint depthCount = 8;
        var wall = new BlockRuntimeId[width, depthCount];
        var surfaceMaterials = new HashSet<BlockRuntimeId>();
        for (var x = 0; x < width; x++)
        {
            var worldX = 355 + (x - width / 2) * 4;
            for (uint depth = 0; depth < depthCount; depth++)
            {
                var block = materials.BlockAt(
                    sample, worldX, -230, depth, placement);
                wall[x, depth] = block;
                if (depth == 0)
                    surfaceMaterials.Add(block);
            }
        }

        Assert.True(surfaceMaterials.Count >= 3,
            "The seabed wall should show sand mixed with multiple patch materials.");
        Assert.All(Enumerable.Range(0, width), x =>
        {
            Assert.Equal(blocks.GetId("asteria:gravel"), wall[x, 4]);
            Assert.Equal(blocks.GetId("asteria:gravel"), wall[x, 5]);
            Assert.Equal(blocks.GetId("asteria:stone"), wall[x, 6]);
            Assert.Equal(blocks.GetId("asteria:stone"), wall[x, 7]);
        });
    }

    [Fact]
    public void OceanSeabedPatchHasOrganicVariationInBothHorizontalDirections()
    {
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "ocean.json")));
        var patch = Assert.IsType<BiomeSurfacePatchDefinition>(
            biome.SurfaceLayers[0].Patch);

        // Anisotropic stretching turned the seabed patch into long, stripe-like
        // regions. Keep the authored noise isotropic while retaining domain warp.
        Assert.Equal(1d, patch.StretchZ);
        Assert.True(patch.WarpStrength > 0d);

        var blocks = BlockRegistry.FromJson(
            Directory.EnumerateFiles(Path.Combine(PackData, "blocks"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        // Reproduce the reported world seed and center the sample grid on the reported seabed coordinate.
        var materials = new BiomeSurfaceMaterialField(7808508665006554090UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var placement = new SurfacePlacementContext(82, 0);
        const int sampleCount = 97;
        var grid = new BlockRuntimeId[sampleCount, sampleCount];
        var seen = new HashSet<BlockRuntimeId>();
        for (var z = 0; z < sampleCount; z++)
        for (var x = 0; x < sampleCount; x++)
        {
            var block = materials.BlockAt(
                sample, 355 + (x - 48) * 4, -230 + (z - 48) * 4, 0, placement);
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
            "The ocean floor should mix sand with multiple patch materials.");
        Assert.True(xTransitions > 0 && zTransitions > 0,
            "The ocean floor patch must vary along both horizontal axes.");
        var larger = Math.Max(xTransitions, zTransitions);
        var smaller = Math.Min(xTransitions, zTransitions);
        Assert.True(smaller >= larger * 0.55,
            $"Seabed patch variation is strongly directional: X={xTransitions}, Z={zTransitions}.");
    }
}
