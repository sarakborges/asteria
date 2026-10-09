using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerDeathConsequencesTests
{
    private static DroppedBlockRuntime Drops(int maximum = 2048)
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        return new DroppedBlockRuntime(world, new BlockRegistry([
            new BlockDefinition("asteria:stone")
        ]), maximumActive: maximum);
    }

    [Fact]
    public void DefaultRuleKeepsInventoryWithoutProducingDrops()
    {
        var player = new PlayerSessionState();
        var stack = new InventoryStack(
            InventoryEntry.FromItem("asteria:berries"), 24);
        Assert.True(player.Inventory.TryInsert(stack));
        player.Health.Damage(20f, PlayerGameMode.Survival);
        var drops = Drops();
        var outcome = PlayerDeathConsequences.Resolve(
            player, new WorldGameRules(), drops, new Vector3(3, 5, 3));
        Assert.True(outcome.InventoryKept);
        Assert.Equal(0, outcome.DroppedStacks);
        Assert.Same(stack.Entry, player.Inventory.SlotAt(27)!.Entry);
        Assert.Empty(drops.ActiveBlocks);
    }

    [Fact]
    public void DeathDropsKeepFullStackQuantitiesAndWearMetadata()
    {
        var player = new PlayerSessionState();
        var stone = InventoryEntry.FromItem("asteria:berries",
            new Dictionary<string, string> { ["origin"] = "wild" });
        Assert.True(player.Inventory.TryInsert(new InventoryStack(stone, 37)));
        var helmet = InventoryEntry.FromItem("asteria:wayfarer_helmet",
            new Dictionary<string, string> { [EquipmentDurability.MetadataKey] = "9" },
            maxStackSize: 1);
        Assert.True(player.Inventory.TryCreativePick(helmet));
        Assert.True(player.Inventory.ClickEquipment(EquipmentSlot.Helmet,
            (_, slot) => slot == EquipmentSlot.Helmet));
        var cursor = InventoryEntry.FromItem("asteria:stick");
        Assert.True(player.Inventory.TryCreativePick(cursor));
        Assert.Equal(PlayerDamageResult.Killed,
            player.Health.Damage(30f, PlayerGameMode.Survival));

        var rule = new WorldGameRules(keepInventory: false);
        var drops = Drops();
        var outcome = PlayerDeathConsequences.Resolve(
            player, rule, drops, new Vector3(3, 5, 3));
        Assert.False(outcome.InventoryKept);
        Assert.Equal(3, outcome.DroppedStacks);
        Assert.All(Enumerable.Range(0, PlayerInventory.TotalSlots),
            i => Assert.Null(player.Inventory.SlotAt(i)));
        Assert.Null(player.Inventory.EquipmentAt(EquipmentSlot.Helmet));
        Assert.Null(player.Inventory.Cursor);
        Assert.Equal(37, drops.ActiveBlocks.Single(
            d => d.Stack.Id == "asteria:berries").Stack.Quantity);
        Assert.Equal("9", drops.ActiveBlocks.Single(
            d => d.Stack.Id == "asteria:wayfarer_helmet")
            .Stack.Entry.Metadata[EquipmentDurability.MetadataKey]);

        var restoredDrops = new DroppedBlockRuntime(new VoxelWorld(),
            new BlockRegistry([new BlockDefinition("asteria:stone")]),
            restore: drops.CaptureState());
        Assert.Equal(37, restoredDrops.ActiveBlocks.Single(
            d => d.Stack.Id == "asteria:berries").Stack.Quantity);
    }

    [Fact]
    public void SaturatedDropCapacityDoesNotConsumePlayerInventory()
    {
        var player = new PlayerSessionState();
        Assert.True(player.Inventory.TryInsert(new InventoryStack(
            InventoryEntry.FromItem("asteria:berries"), 64)));
        var drops = Drops(maximum: 1);
        drops.Spawn(new InventoryStack(
            InventoryEntry.FromItem("asteria:existing")), new Vector3(3, 5, 3));
        player.Health.Damage(20f, PlayerGameMode.Survival);
        var outcome = PlayerDeathConsequences.Resolve(
            player, new WorldGameRules(keepInventory: false), drops,
            new Vector3(3, 5, 3));
        Assert.True(outcome.InventoryKept);
        Assert.True(outcome.DropCapacityExceeded);
        Assert.Equal(1, drops.ActiveCount);
        Assert.Equal(64, player.Inventory.SlotAt(27)!.Quantity);
    }

    [Fact]
    public void DeathPolicyRejectsAlivePlayers()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PlayerDeathConsequences.Resolve(new PlayerSessionState(),
                new WorldGameRules(), Drops(), new Vector3(3, 5, 3)));
        var rules = new WorldGameRules();
        Assert.True(rules.KeepInventory);
        Assert.True(rules.SetKeepInventory(false));
        Assert.False(rules.SetKeepInventory(false));
    }
}
