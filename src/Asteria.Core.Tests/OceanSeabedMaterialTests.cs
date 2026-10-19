using System.Text.Json.Nodes;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class OceanSeabedMaterialTests
{
    private static readonly string PackData =
        Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");

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
        var materials = new BiomeSurfaceMaterialField(0x0CEA_2026UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var placement = new SurfacePlacementContext(80, 0);
        const int sampleCount = 97;
        var grid = new BlockRuntimeId[sampleCount, sampleCount];
        var seen = new HashSet<BlockRuntimeId>();
        for (var z = 0; z < sampleCount; z++)
        for (var x = 0; x < sampleCount; x++)
        {
            var block = materials.BlockAt(
                sample, (x - 48) * 4, (z - 48) * 4, 0, placement);
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
