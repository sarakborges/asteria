using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerHazardTests
{
    [Fact]
    public void OxygenRunsOutAfterFifteenSecondsAndDrowningStopsOnSurfacing()
    {
        var health = new PlayerHealth();
        var hazards = new PlayerHazardRuntime();
        for (var i = 0; i < 15; i++)
            Assert.Equal(PlayerDamageResult.Ignored,
                hazards.AdvanceFluid(1f, true, true, true, 0f, health,
                    PlayerGameMode.Survival));
        Assert.Equal(0f, hazards.BreathSeconds);
        Assert.Equal(PlayerDamageResult.Hurt,
            hazards.AdvanceFluid(1f, true, true, true, 0f, health,
                PlayerGameMode.Survival));
        Assert.Equal(18f, health.Current);
        hazards.AdvanceFluid(0.1f, false, true, true, 0f, health,
            PlayerGameMode.Survival);
        Assert.Equal(PlayerHazardRuntime.BreathCapacitySeconds, hazards.BreathSeconds);
        Assert.Equal(PlayerDamageResult.Ignored,
            hazards.AdvanceFluid(1f, true, true, true, 0f, health,
                PlayerGameMode.Survival));
    }

    [Fact]
    public void LavaDamagesOncePerSecondWithoutConsumingBreathWhenAuthored()
    {
        var health = new PlayerHealth();
        var hazards = new PlayerHazardRuntime();
        for (var i = 0; i < 2; i++)
            hazards.AdvanceFluid(0.5f, true, true, false, 8f, health,
                PlayerGameMode.Survival);
        Assert.Equal(12f, health.Current);
        Assert.Equal(PlayerHazardRuntime.BreathCapacitySeconds, hazards.BreathSeconds);
        hazards.AdvanceFluid(0.25f, false, false, false, 0f, health,
            PlayerGameMode.Survival);
        Assert.Equal(12f, health.Current);
    }

    [Fact]
    public void CreatureContactCooldownAndArmorProtectionAreNotPerFrame()
    {
        var health = new PlayerHealth();
        var hazards = new PlayerHazardRuntime();
        Assert.Equal(PlayerDamageResult.Hurt,
            hazards.TouchCreature(4f, 0.5f, health, PlayerGameMode.Survival));
        Assert.Equal(18f, health.Current);
        Assert.Equal(PlayerDamageResult.Ignored,
            hazards.TouchCreature(4f, 0f, health, PlayerGameMode.Survival));
        hazards.AdvanceTime(0.8f);
        Assert.Equal(PlayerDamageResult.Hurt,
            hazards.TouchCreature(4f, 0.5f, health, PlayerGameMode.Survival));
        Assert.Equal(16f, health.Current);
    }

    [Fact]
    public void CreativeAndDeadPlayersDoNotAccumulateEnvironmentalDamage()
    {
        var health = new PlayerHealth();
        var hazards = new PlayerHazardRuntime();
        for (var i = 0; i < 20; i++)
            hazards.AdvanceFluid(1f, true, true, true, 10f, health,
                PlayerGameMode.Creative);
        Assert.Equal(20f, health.Current);
        Assert.Equal(PlayerHazardRuntime.BreathCapacitySeconds, hazards.BreathSeconds);
        Assert.Equal(PlayerDamageResult.Ignored,
            hazards.TouchCreature(5f, 0f, health, PlayerGameMode.Spectator));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            hazards.AdvanceFluid(float.NaN, true, true, true, 0f, health,
                PlayerGameMode.Survival));
    }

    [Fact]
    public void AuthoredProtectionUsesOnlyMatchingOccupiedSlotsAndIsCapped()
    {
        var registry = PackContentRegistry<ItemDefinition>.FromJson(
        [
            """{"id":"asteria:helm","category":"tools","icon":"textures/helm.png","maxStackSize":1,"equipmentSlot":"helmet","damageReduction":0.5,"maxDurability":100}""",
            """{"id":"asteria:chest","category":"tools","icon":"textures/chest.png","maxStackSize":1,"equipmentSlot":"chest","damageReduction":0.5,"maxDurability":100}"""
        ], ItemDefinition.Parse, item => item.Id);
        var inventory = new PlayerInventory();
        foreach (var (id, slot) in new[] {
            ("asteria:helm", EquipmentSlot.Helmet),
            ("asteria:chest", EquipmentSlot.Chest)
        })
        {
            Assert.True(inventory.TryCreativePick(InventoryEntry.FromItem(id, maxStackSize: 1)));
            Assert.True(inventory.ClickEquipment(slot, (entry, chosen) =>
                entry.Id == id && chosen == slot));
        }
        Assert.Equal(PlayerEquipmentProtection.MaximumReduction,
            PlayerEquipmentProtection.Calculate(inventory, registry));
        Assert.Throws<FormatException>(() => ItemDefinition.Parse(
            """{"id":"asteria:bad","category":"tools","icon":"textures/bad.png","damageReduction":0.2}"""));
        Assert.Throws<FormatException>(() => ItemDefinition.Parse(
            """{"id":"asteria:bad","category":"tools","icon":"textures/bad.png","maxStackSize":1,"equipmentSlot":"helmet","damageReduction":0.9}"""));
    }

    [Fact]
    public void FluidJsonHazardsAreExplicitValidatedAndOptional()
    {
        var lava = FluidDefinitionJson.Parse(
            """{"id":"asteria:lava","color":"851701","opacity":1,"contactDamagePerSecond":8,"depletesBreath":false}""");
        Assert.Equal(8f, lava.ContactDamagePerSecond);
        Assert.False(lava.DepletesBreath);
        var water = FluidDefinitionJson.Parse(
            """{"id":"asteria:water","color":"4f9fd6","opacity":0.7}""");
        Assert.Equal(0f, water.ContactDamagePerSecond);
        Assert.True(water.DepletesBreath);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FluidDefinitionJson.Parse(
                """{"id":"asteria:lava","color":"851701","opacity":1,"contactDamagePerSecond":-1}"""));
    }

    [Fact]
    public void ContactDamageUsesColliderAndIgnoresDeadOrNoAiCreatures()
    {
        var definitions = PackContentRegistry<CreatureDefinition>.FromJson(
        [
            """{"id":"asteria:slime","model":"models/slime.glb","health":10,"maxPerType":5,
            "collider":{"size":[1,1,1],"centerOffset":[0,0.5,0]},
            "jumpSpeed":5,"moveSpeed":1,"jumpInterval":2,
            "anticipationSeconds":0.2,"landingSeconds":0.2,
            "animations":{"idle":"Idle"},"textures":{},"contactDamage":3}"""
        ], CreatureDefinition.Parse, creature => creature.Id);
        var runtime = new CreatureRuntime(definitions);
        Assert.True(runtime.TrySpawn("asteria:slime", new Vector3(6, 1, 6),
            out var creature));
        var player = new WorldAabb(
            new Vector3(5.75f, 1, 5.75f),
            new Vector3(6.25f, 2.8f, 6.25f));
        Assert.Equal(3f, runtime.ContactDamageAt(player));
        Assert.Equal(0f, runtime.ContactDamageAt(new WorldAabb(
            new Vector3(3, 1, 3), new Vector3(4, 3, 4))));
        Assert.True(runtime.TrySetNoAi(creature.Id, true));
        Assert.Equal(0f, runtime.ContactDamageAt(player));
        Assert.True(runtime.TrySetNoAi(creature.Id, false));
        Assert.True(runtime.TryKill(creature.Id, out _));
        Assert.Equal(0f, runtime.ContactDamageAt(player));
    }
}
