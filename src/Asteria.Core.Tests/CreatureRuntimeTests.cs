using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CreatureRuntimeTests
{
    private static PackContentRegistry<CreatureDefinition> Definitions(int maximumPerType = 2)
    {
        var json = $$"""
            {
              "id": "asteria:slime_aqua",
              "model": "models/creatures/slime_hydro/slime_hydro.glb",
              "health": 11,
              "maxPerType": {{maximumPerType}},
              "collider": {
                "size": [0.78, 0.84, 0.78],
                "centerOffset": [0, 0.42, 0]
              },
              "jumpSpeed": 5,
              "moveSpeed": 1.4,
              "jumpInterval": 2,
              "anticipationSeconds": 0.24,
              "landingSeconds": 0.34,
              "animations": {"idle": "Idle"},
              "textures": {"SlimeFace": "textures/creatures/slime_hydro/face.png"}
            }
            """;
        return PackContentRegistry<CreatureDefinition>.FromJson(
            [json], CreatureDefinition.Parse, definition => definition.Id);
    }

    [Fact]
    public void SpawnObeysTypeCapacityAndStableIdOrder()
    {
        var runtime = new CreatureRuntime(Definitions());
        Assert.True(runtime.TrySpawn("asteria:slime_aqua", new Vector3(0, 20, 0), out var first));
        Assert.True(runtime.TrySpawn("asteria:slime_aqua", new Vector3(1, 20, 0), out var second));
        Assert.False(runtime.TrySpawn("asteria:slime_aqua", new Vector3(2, 20, 0), out _));
        Assert.False(runtime.TrySpawn("asteria:unknown", new Vector3(2, 20, 0), out _));
        Assert.Equal(1UL, first.Id.Value);
        Assert.Equal(2UL, second.Id.Value);
        Assert.Equal(2, runtime.CountType("asteria:slime_aqua"));
        Assert.Equal([first.Id, second.Id], runtime.ActiveCreatures.Select(entity => entity.Id));
    }

    [Fact]
    public void RestoredPopulationRetainsIdsHealthAndPerTypeLimit()
    {
        var definitions = Definitions();
        var current = new CreatureRuntime(definitions);
        Assert.True(current.TrySpawn("asteria:slime_aqua", new Vector3(5, 24, 0), out var first));
        Assert.True(current.TryDamage(first.Id, 4));
        var snapshot = current.CaptureState();
        var restored = new CreatureRuntime(definitions, snapshot);
        Assert.Equal(7f, Assert.Single(restored.ActiveCreatures).Health);
        Assert.True(restored.TrySpawn("asteria:slime_aqua", new Vector3(7, 24, 0), out var second));
        Assert.Equal(2UL, second.Id.Value);
        Assert.False(restored.TrySpawn("asteria:slime_aqua", new Vector3(9, 24, 0), out _));
        Assert.True(restored.TryDamage(first.Id, 7));
        Assert.Equal(1, restored.Count);
        Assert.Equal(1, restored.CountType("asteria:slime_aqua"));
    }

    [Fact]
    public void DistantCreaturesObserveGraceThenDespawnDeterministically()
    {
        var runtime = new CreatureRuntime(Definitions());
        Assert.True(runtime.TrySpawn("asteria:slime_aqua", new Vector3(200, 20, 0), out _));
        Assert.True(runtime.TrySpawn("asteria:slime_aqua", new Vector3(2, 20, 0), out var near));
        Assert.Equal(0, runtime.Advance(4.5, new Vector3(0, 20, 0)));
        Assert.Equal(1, runtime.Advance(0.5, new Vector3(0, 20, 0)));
        Assert.Equal(near.Id, Assert.Single(runtime.ActiveCreatures).Id);
    }

    [Fact]
    public void InvalidRestoresAndSpawnPositionsCannotEnterRuntime()
    {
        var definitions = Definitions();
        var invalid = new CreatureRuntimeSnapshot(2,
            [new CreatureInstanceState(new CreatureInstanceId(3),
                "asteria:slime_aqua", new Vector3(0, 20, 0), 10, 0)]);
        Assert.Throws<ArgumentException>(() => new CreatureRuntime(definitions, invalid));
        var runtime = new CreatureRuntime(definitions);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            runtime.TrySpawn("asteria:slime_aqua", new Vector3(float.NaN, 1, 0), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            runtime.TryDamage(new CreatureInstanceId(1), float.PositiveInfinity));
    }

    [Fact]
    public void DimensionStateCanHoldRetiredCreaturePopulation()
    {
        var registry = Definitions();
        var runtime = new CreatureRuntime(registry);
        Assert.True(runtime.TrySpawn("asteria:slime_aqua", new Vector3(3, 20, 4), out var first));
        var snapshot = runtime.CaptureState();
        var copy = new CreatureRuntime(registry, snapshot);
        Assert.Equal(first, Assert.Single(copy.ActiveCreatures));
    }
}
