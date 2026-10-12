using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GroundDecorationDensityTests
{
    [Fact]
    public void AuthoredSurfaceBiomesLeaveUnoccupiedGroundBetweenDecorations()
    {
        var data = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data", "biomes");
        var biomes = Directory.EnumerateFiles(data, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => BiomeDefinitionJson.Parse(File.ReadAllText(path)))
            .Where(biome => biome.SurfaceLayout is not null)
            .ToArray();

        Assert.NotEmpty(biomes);
        foreach (var biome in biomes)
        {
            // Wall/ceiling growth, submerged plants and floating-on-water
            // decorations have different placement budgets from dry ground.
            var ground = biome.Decorations.Where(rule =>
                rule.SupportSurface == DecorationSupportSurface.Floor &&
                rule.FluidPlacement != DecorationFluidPlacement.Submerged &&
                rule.FluidRequirement?.Relation != DecorationFluidRelation.Below)
                .ToArray();

            var baseBudget = ground.Sum(rule => (double)rule.Chance);
            Assert.True(baseBudget <= 0.75d,
                $"High ground-decoration density in {biome.Id}: {baseBudget:F3}");

            if (biome.SurfaceHabitats is null)
                continue;

            foreach (var habitat in biome.SurfaceHabitats.Bands)
            {
                var weightedBudget = ground.Sum(rule =>
                    (double)rule.Chance *
                    (rule.HabitatWeights?.For(habitat.Id) ?? 1d));
                Assert.True(weightedBudget <= 1.1d,
                    $"Crowded decoration habitat {biome.Id}/{habitat.Id}: {weightedBudget:F3}");
            }
        }
    }
}
