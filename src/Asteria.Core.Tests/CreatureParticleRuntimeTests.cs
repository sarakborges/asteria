using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CreatureParticleRuntimeTests
{
    private const string CreatureJson = """
        {
          "id": "asteria:slime_test",
          "model": "models/creatures/slime/slime.glb",
          "health": 10,
          "maxPerType": 8,
          "collider": {"size":[1,1,1],"centerOffset":[0,0.5,0]},
          "jumpSpeed": 4,
          "moveSpeed": 1,
          "jumpInterval": 2,
          "anticipationSeconds": 0.2,
          "landingSeconds": 0.2,
          "animations": {"idle":"Idle","land":"Land","hurt":"Hurt","death":"Death"},
          "textures": {},
          "particleEffects": {
            "idle": {"count": 2, "color":[0.25,0.5,1,0.8],
              "size":0.04,"lifetime":1,"interval":0.1,
              "horizontalSpeed":0.1,"endScale":0.1},
            "land": {"count": 3, "color":[0.25,0.5,1,0.8],
              "size":0.04,"lifetime":0.5},
            "hurt": {"count": 4, "color":[1,0.25,0.5,0.9],
              "size":0.04,"lifetime":0.5},
            "death": {"count": 6, "color":[1,0.25,0.5,0.9],
              "size":0.04,"lifetime":0.5}
          }
        }
        """;

    private static PackContentRegistry<CreatureDefinition> Definitions(string json = CreatureJson) =>
        PackContentRegistry<CreatureDefinition>.FromJson(
            [json], CreatureDefinition.Parse, creature => creature.Id);

    private static CreatureInstanceState Creature(ulong id = 1) =>
        new(new CreatureInstanceId(id), "asteria:slime_test",
            new Vector3(3, 1, 4), 10f, 0);

    [Fact]
    public void AuthoredStatesEmitOnceThenRepeatOnTheirOwnInterval()
    {
        var particles = new CreatureParticleRuntime(Definitions());
        var creature = Creature();
        particles.Advance(0.05f, [creature]);
        Assert.Equal(2, particles.Particles.Count);
        var original = particles.Particles[0].Position;

        particles.Advance(0.05f, [creature]);
        Assert.Equal(2, particles.Particles.Count);
        Assert.NotEqual(original, particles.Particles[0].Position);
        particles.Advance(0.05f, [creature]);
        Assert.Equal(4, particles.Particles.Count);

        var landing = creature with
        {
            Motion = new CreatureHopMotion(
                CreatureHopPhase.Land, 0.2f, 0f, Vector2.Zero, 0f, 1)
        };
        particles.Advance(0.05f, [landing]);
        Assert.Equal(7, particles.Particles.Count);
        particles.Advance(0.05f, [landing]);
        Assert.Equal(7, particles.Particles.Count);
    }

    [Fact]
    public void RepeatedHurtAndDeathUseAuthoritativeCombatRevisions()
    {
        var particles = new CreatureParticleRuntime(Definitions());
        var creature = Creature() with
        {
            HurtSecondsRemaining = 0.3f, AttacksReceived = 1
        };
        particles.Advance(0.05f, [creature]);
        Assert.Equal(4, particles.Particles.Count);
        particles.Advance(0.05f, [creature]);
        Assert.Equal(4, particles.Particles.Count);

        particles.Advance(0.05f, [creature with { AttacksReceived = 2 }]);
        Assert.Equal(8, particles.Particles.Count);
        Assert.Equal("hurt", CreatureVisualState.From(creature).Phase);

        var dying = creature with
        {
            Health = 0f, DeathSecondsRemaining = 0.5f, AttacksReceived = 3
        };
        particles.Advance(0.05f, [dying]);
        Assert.Equal(14, particles.Particles.Count);
        Assert.Equal("death", CreatureVisualState.From(dying).Phase);
    }

    [Fact]
    public void ParticleLifetimeAndEmitterRetirementAreBounded()
    {
        var particles = new CreatureParticleRuntime(Definitions());
        for (var i = 1UL; i <= 200UL; i++)
            particles.Advance(0f, Enumerable.Range(1, (int)i)
                .Select(id => Creature((ulong)id)).ToArray());
        Assert.Equal(CreatureParticleRuntime.MaximumActive, particles.Particles.Count);

        for (var i = 0; i < 30; i++)
            particles.Advance(0.05f, []);
        Assert.Empty(particles.Particles);

        particles.Advance(0.05f, [Creature()]);
        Assert.Equal(2, particles.Particles.Count);
        particles.Clear();
        Assert.Empty(particles.Particles);
    }

    [Fact]
    public void ParticleRandomnessIsStableAndDoesNotDependOnInputOrder()
    {
        var first = new CreatureParticleRuntime(Definitions());
        var second = new CreatureParticleRuntime(Definitions());
        first.Advance(0.05f, [Creature(2), Creature(1)]);
        second.Advance(0.05f, [Creature(1), Creature(2)]);
        Assert.Equal(
            first.Particles.Select(p => (p.Position, p.Velocity, p.Color)),
            second.Particles.Select(p => (p.Position, p.Velocity, p.Color)));
    }

    [Theory]
    [InlineData("\"count\": 65")]
    [InlineData("\"size\": 0")]
    [InlineData("\"lifetime\": -1")]
    [InlineData("\"interval\": -0.5")]
    [InlineData("\"gravity\": 200")]
    [InlineData("\"unsupported\": 1")]
    public void RejectsUnboundedOrUnsupportedParticleFields(string invalid)
    {
        var json = CreatureJson.Replace(
            "\"count\": 2, \"color\":[0.25,0.5,1,0.8]",
            invalid + ", \"color\":[0.25,0.5,1,0.8]",
            StringComparison.Ordinal);
        // The second occurrence of size/lifetime/etc. is still valid; the
        // first invalid override is either a duplicate or out of range.
        Assert.Throws<FormatException>(() => CreatureDefinition.Parse(json));
    }

    [Fact]
    public void DefaultPackElementalSlimesAuthorPhaseParticles()
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", "creatures");
        var definitions = Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => CreatureDefinition.Parse(File.ReadAllText(path)))
            .ToArray();
        Assert.Equal(20, definitions.Length);
        foreach (var definition in definitions.Where(d => d.ParticleEffects.Count > 0))
            Assert.Contains("death", definition.ParticleEffects.Keys);
    }
}
