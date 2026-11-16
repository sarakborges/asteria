using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class AmbientParticleDefinitionTests
{
    [Fact]
    public void DefaultPackParticleSourcesResolve()
    {
        var particles = AmbientParticleRegistry.FromJson(ReadJsonDirectory("ambient_particles"));
        Assert.Equal(8, particles.Definitions.Count);
        Assert.Equal(8, particles.Definitions.Select(x => x.Id).Distinct().Count());
        particles.ValidateReferences(
            DimensionRegistry.FromJson(ReadJsonDirectory("dimensions")),
            BiomeRegistry.FromJson(ReadJsonDirectory("biomes")),
            FluidRegistry.FromJson(ReadJsonDirectory("fluids")));
    }

    [Fact]
    public void MineCloneParticleSchemaPreservesWindAndBubblePop()
    {
        var definition = AmbientParticleDefinition.Parse(
            """
            {
              "id":"asteria:smoke",
              "source":{"type":"fluid_surface","id":"asteria:lava"},
              "color":{"hue":20,"saturation":0.8,"intensity":0.5},
              "size":{"min":0.04,"max":0.07},
              "lifetime":{"min":1,"max":3},
              "spawnRate":10,
              "windInfluence":2.5,
              "popAtEnd":true,
              "velocity":[0,0.1,0]
            }
            """);
        Assert.Equal(AmbientParticleSourceKind.FluidSurface, definition.SourceKind);
        Assert.Equal("asteria:lava", definition.SourceId);
        Assert.Equal(2.5f, definition.WindInfluence);
        Assert.True(definition.PopAtEnd);
        Assert.Equal(0.1f, definition.Velocity.Y);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2000)]
    public void RejectsInvalidEmissionRates(float emissionRate)
    {
        var json = """
            {
              "id":"asteria:test",
              "source":{"type":"dimension","id":"asteria:overworld"},
              "color":{"hue":72,"saturation":0.5,"intensity":0.5},
              "spawnRate":RATE
            }
            """.Replace("RATE", emissionRate.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        Assert.Throws<FormatException>(() => AmbientParticleDefinition.Parse(json));
    }

    [Fact]
    public void RegistryRejectsDuplicateRuleIds()
    {
        var json = """
            {
              "id":"asteria:test",
              "source":{"type":"dimension","id":"asteria:overworld"},
              "color":{"hue":72,"saturation":0.5,"intensity":0.5},
              "spawnRate":1
            }
            """;
        Assert.Throws<ArgumentException>(() =>
            AmbientParticleRegistry.FromJson([json, json]));
    }

    private static IEnumerable<string> ReadJsonDirectory(string category) =>
        Directory.EnumerateFiles(
            Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", category),
            "*.json")
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(File.ReadAllText);
}
