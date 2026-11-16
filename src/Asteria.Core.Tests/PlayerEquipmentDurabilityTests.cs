using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerEquipmentDurabilityTests
{
    private static PackContentRegistry<ItemDefinition> Gear() =>
        PackContentRegistry<ItemDefinition>.FromJson(
        [
            """{"id":"asteria:helm","category":"tools","icon":"textures/helm.png","maxStackSize":1,"equipmentSlot":"helmet","damageReduction":0.25,"maxDurability":2}""",
            """{"id":"asteria:chest","category":"tools","icon":"textures/chest.png","maxStackSize":1,"equipmentSlot":"chest","damageReduction":0.3,"maxDurability":3}"""
        ], ItemDefinition.Parse, x => x.Id);

    private static void Equip(
        PlayerInventory inventory, EquipmentSlot slot, string id,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Assert.True(inventory.TryCreativePick(
            InventoryEntry.FromItem(id, metadata, maxStackSize: 1)));
        Assert.True(inventory.ClickEquipment(slot,
            (entry, chosen) => entry.Id == id && chosen == slot));
    }

    [Fact]
    public void DamageWearsEquippedArmorUntilItBreaks()
    {
        var registry = Gear();
        var inventory = new PlayerInventory();
        Equip(inventory, EquipmentSlot.Helmet, "asteria:helm");
        Assert.Equal(0.25f, PlayerEquipmentProtection.Calculate(inventory, registry));

        var revision = inventory.Revision;
        Assert.True(PlayerEquipmentWear.ApplyContactHit(inventory, registry));
        Assert.Equal(revision + 1, inventory.Revision);
        var worn = inventory.EquipmentAt(EquipmentSlot.Helmet)!.Entry;
        Assert.Equal(1, EquipmentDurability.Remaining(worn, 2));
        Assert.Equal("1", worn.Metadata[EquipmentDurability.MetadataKey]);

        Assert.True(PlayerEquipmentWear.ApplyContactHit(inventory, registry));
        Assert.Null(inventory.EquipmentAt(EquipmentSlot.Helmet));
        Assert.Equal(0f, PlayerEquipmentProtection.Calculate(inventory, registry));
        Assert.False(PlayerEquipmentWear.ApplyContactHit(inventory, registry));
    }

    [Fact]
    public void WornItemsRetainRemainingUsesAcrossCursorAndSnapshot()
    {
        var inventory = new PlayerInventory();
        Equip(inventory, EquipmentSlot.Chest, "asteria:chest");
        Assert.True(PlayerEquipmentWear.ApplyContactHit(inventory, Gear()));

        Assert.True(inventory.ClickEquipment(EquipmentSlot.Chest, (_, _) => true));
        Assert.Equal(2, EquipmentDurability.Remaining(inventory.Cursor!.Entry, 3));
        Assert.True(inventory.ClickEquipment(EquipmentSlot.Chest, (_, _) => true));

        var restored = new PlayerInventory();
        restored.Restore(inventory.Capture());
        Assert.Equal(2, EquipmentDurability.Remaining(
            restored.EquipmentAt(EquipmentSlot.Chest)!.Entry, 3));
        Assert.True(PlayerEquipmentWear.ApplyContactHit(restored, Gear()));
        Assert.Equal(1, EquipmentDurability.Remaining(
            restored.EquipmentAt(EquipmentSlot.Chest)!.Entry, 3));
    }

    [Fact]
    public void InvalidWearMetadataCannotCausePartialEquipmentChanges()
    {
        var inventory = new PlayerInventory();
        Equip(inventory, EquipmentSlot.Helmet, "asteria:helm");
        Equip(inventory, EquipmentSlot.Chest, "asteria:chest",
            new Dictionary<string, string> { [EquipmentDurability.MetadataKey] = "999" });
        var revision = inventory.Revision;
        Assert.Throws<InvalidDataException>(() =>
            PlayerEquipmentWear.ApplyContactHit(inventory, Gear()));
        Assert.Equal(revision, inventory.Revision);
        Assert.False(inventory.EquipmentAt(EquipmentSlot.Helmet)!.Entry.Metadata
            .ContainsKey(EquipmentDurability.MetadataKey));
    }

    [Fact]
    public void InvalidAuthoredDurabilityIsRejected()
    {
        const string baseJson =
            """{"id":"asteria:helm","category":"tools","icon":"textures/helm.png","maxStackSize":1,"equipmentSlot":"helmet","damageReduction":0.2,"maxDurability":25}""";
        Assert.Equal(25, ItemDefinition.Parse(baseJson).MaxDurability);
        Assert.Throws<FormatException>(() => ItemDefinition.Parse(
            baseJson.Replace("\"maxDurability\":25", "\"maxDurability\":0")));
        Assert.Throws<FormatException>(() => ItemDefinition.Parse(
            baseJson.Replace("\"maxDurability\":25", "\"maxDurability\":70000")));
        Assert.Throws<FormatException>(() => ItemDefinition.Parse(
            baseJson.Replace(",\"maxDurability\":25", "")));
    }
}
