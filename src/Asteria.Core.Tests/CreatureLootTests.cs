using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CreatureLootTests
{
    private static IReadOnlyList<string> Documents(string category)
    {
        var root = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data", category);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();
    }

    [Fact]
    public void ElementalSlimesDropOnlyAuthoredAndExistingEssences()
    {
        var creatures = PackContentRegistry<CreatureDefinition>.FromJson(
            Documents("creatures"), CreatureDefinition.Parse, definition => definition.Id);
        var items = PackContentRegistry<ItemDefinition>.FromJson(
            Documents("items"), ItemDefinition.Parse, definition => definition.Id);

        var looting = creatures.Definitions.Where(c => c.LootTable.Count > 0).ToArray();
        Assert.Equal(18, looting.Length);
        foreach (var creature in looting)
        {
            var entry = Assert.Single(creature.LootTable);
            Assert.True(items.TryGet(entry.ItemId, out _));
            Assert.Equal(1f, entry.Chance);
            Assert.Equal(creature.Id.EndsWith("_large", StringComparison.Ordinal) ? 2 : 1,
                entry.Quantity);
        }
        Assert.Empty(creatures.Get("asteria:slime").LootTable);
        Assert.Empty(creatures.Get("asteria:slime_legacy").LootTable);
    }

    [Fact]
    public void LethalAttackEmitsLootExactlyOnceAndPreservesDeathLifecycle()
    {
        var creatures = PackContentRegistry<CreatureDefinition>.FromJson(
            Documents("creatures"), CreatureDefinition.Parse, definition => definition.Id);
        var runtime = new CreatureRuntime(creatures);
        Assert.True(runtime.TrySpawn("asteria:slime_aqua_large",
            new Vector3(4, 2, 8), out var slime));

        var lethal = AttackDefinition.Parse(
            """{"id":"asteria:test_lethal","damage":50}""");
        Assert.True(runtime.TryAttack(slime.Id, lethal,
            new Vector3(3, 2, 8), out var result));
        Assert.True(result.Killed);
        var loot = Assert.Single(result.Loot);
        Assert.Equal("asteria:essence_aqua", loot.ItemId);
        Assert.Equal(2, loot.Quantity);
        Assert.False(runtime.TryAttack(slime.Id, lethal,
            new Vector3(3, 2, 8), out _));

        var reloaded = new CreatureRuntime(creatures, runtime.CaptureState());
        Assert.True(Assert.Single(reloaded.ActiveCreatures).IsDying);
        reloaded.Advance(CreatureRuntime.DeathAnimationSeconds,
            new Vector3(4, 2, 8));
        Assert.Empty(reloaded.ActiveCreatures);
    }

    [Fact]
    public void LootRollsAreIndependentOfIterationOrderAndBounded()
    {
        var table = new[]
        {
            new CreatureLootEntry("asteria:essence_aqua", 0.45f, 2),
            new CreatureLootEntry("asteria:essence_luma", 0.7f, 1),
            new CreatureLootEntry("asteria:essence_terra", 0f, 1),
        };
        foreach (var id in Enumerable.Range(1, 200))
        {
            var first = CreatureLootTable.Roll(table, (ulong)id);
            var second = CreatureLootTable.Roll(table, (ulong)id);
            Assert.Equal(first, second);
            Assert.DoesNotContain(first, x => x.ItemId == "asteria:essence_terra");
        }
    }

    [Theory]
    [InlineData("""{"id":"asteria:test","lootTable":[{"item":"bad"}]}""")]
    [InlineData("""{"id":"asteria:test","lootTable":[{"item":"asteria:x","chance":1.1}]}""")]
    [InlineData("""{"id":"asteria:test","lootTable":[{"item":"asteria:x","quantity":17}]}""")]
    public void AuthoredLootRejectsInvalidEntries(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Throws<FormatException>(() =>
            CreatureLootTable.Parse(document.RootElement));
    }
}
