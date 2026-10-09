using Asteria.Core.World;
using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class PlayerEquipmentTests
{
    private static InventoryEntry Helmet() =>
        InventoryEntry.FromItem("asteria:helm_test", maxStackSize: 1);
    private static readonly Func<InventoryEntry, EquipmentSlot, bool> Rules =
        (entry, slot) => entry.Equals(Helmet()) && slot == EquipmentSlot.Helmet;

    [Fact]
    public void EquippingSwapsWithCursorAndIsLossless()
    {
        var player = new PlayerInventory();
        Assert.True(player.TryCreativePick(Helmet()));
        Assert.True(player.ClickEquipment(EquipmentSlot.Helmet, Rules));
        Assert.Null(player.Cursor);
        Assert.Equal(Helmet(), player.EquipmentAt(EquipmentSlot.Helmet)!.Entry);
        Assert.True(player.ClickEquipment(EquipmentSlot.Helmet, Rules));
        Assert.Null(player.EquipmentAt(EquipmentSlot.Helmet));
        Assert.Equal(Helmet(), player.Cursor!.Entry);
    }

    [Fact]
    public void InvalidEquipDoesNotConsumeCursorOrChangeRevision()
    {
        var inventory = new PlayerInventory();
        var invalid = InventoryEntry.FromItem("asteria:stone", maxStackSize: 64);
        Assert.True(inventory.TryCreativePick(invalid, 2));
        var version = inventory.Revision;
        Assert.False(inventory.ClickEquipment(EquipmentSlot.Helmet, Rules));
        Assert.False(inventory.ClickEquipment((EquipmentSlot)255, Rules));
        Assert.Equal(version, inventory.Revision);
        Assert.Equal(2, inventory.Cursor!.Quantity);
        Assert.Null(inventory.EquipmentAt(EquipmentSlot.Helmet));
    }

    [Fact]
    public void SnapshotDefendsAgainstInvalidShapesAndRestoresAllSlots()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryCreativePick(Helmet()));
        Assert.True(inventory.ClickEquipment(EquipmentSlot.Helmet, Rules));
        var saved = inventory.Capture();
        var other = new PlayerInventory();
        other.Restore(saved);
        Assert.Equal(Helmet(), other.EquipmentAt(EquipmentSlot.Helmet)!.Entry);

        var bad = saved with
        {
            Equipment = [new InventoryStack(
                InventoryEntry.FromItem("asteria:stack", maxStackSize: 64), 2), null, null, null]
        };
        Assert.Throws<InvalidDataException>(() => other.Restore(bad));
        Assert.Equal(Helmet(), other.EquipmentAt(EquipmentSlot.Helmet)!.Entry);
    }

    [Fact]
    public void LegacySnapshotHasEmptyEquipment()
    {
        var inventory = new PlayerInventory();
        inventory.Restore(new PlayerInventorySnapshot(
            0, new InventoryStack?[PlayerInventory.BackpackSlots],
            new InventoryStack?[PlayerInventory.HotbarSlots], null));
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            Assert.Null(inventory.EquipmentAt(slot));
    }

    [Fact]
    public void ItemDefinitionRejectsInvalidEquipSlotAndNonUnitaryGear()
    {
        var valid = ItemDefinition.Parse("""
            {"id":"asteria:cap","category":"tools","icon":"textures/items/cap.png",
             "maxStackSize":1,"equipmentSlot":"helmet"}
            """);
        Assert.Equal(EquipmentSlot.Helmet, valid.EquipmentSlot);
        Assert.Throws<FormatException>(() => ItemDefinition.Parse("""
            {"id":"asteria:cap","category":"tools","icon":"textures/items/cap.png",
             "maxStackSize":2,"equipmentSlot":"helmet"}
            """));
        Assert.Throws<FormatException>(() => ItemDefinition.Parse("""
            {"id":"asteria:cap","category":"tools","icon":"textures/items/cap.png",
             "maxStackSize":1,"equipmentSlot":"helmet../"}
            """));
    }
    private const string WearableJson = """
        {
          "id":"asteria:wayfarer_helmet",
          "category":"tools",
          "icon":"textures/items/wayfarer_helmet.png",
          "maxStackSize":1,
          "equipmentSlot":"helmet",
          "equipmentVisual":{
            "parts":[
              {"mesh":"HeadMesh","size":[1.12,0.58,1.13],
               "offset":[0,0.25,0],"color":"#5B6F7A"}
            ]
          }
        }
        """;

    [Fact]
    public void AuthoredWearableHasImmutableBodyAttachedAppearance()
    {
        var item = ItemDefinition.Parse(WearableJson);
        Assert.Equal(EquipmentSlot.Helmet, item.EquipmentSlot);
        var appearance = Assert.IsType<EquipmentVisualDefinition>(item.EquipmentVisual);
        var part = Assert.Single(appearance.Parts);
        Assert.Equal("HeadMesh", part.Mesh);
        Assert.Equal(new System.Numerics.Vector3(1.12f, 0.58f, 1.13f), part.Size);
        Assert.Equal(new System.Numerics.Vector3(0, 0.25f, 0), part.Offset);
        Assert.Equal(0x5B6F7Au, part.Rgb);
        Assert.False(appearance.Parts is EquipmentVisualPart[]);
    }

    [Theory]
    [InlineData("\"equipmentSlot\":\"helmet\"", "\"equipmentSlot\":\"boots\"")]
    [InlineData("\"size\":[1.12,0.58,1.13]", "\"size\":[0,0.58,1.13]")]
    [InlineData("\"offset\":[0,0.25,0]", "\"offset\":[0,0.99,0]")]
    [InlineData("\"color\":\"#5B6F7A\"", "\"color\":\"magenta\"")]
    [InlineData("\"mesh\":\"HeadMesh\"", "\"mesh\":\"HandMesh\"")]
    public void InvalidEquipmentAppearanceIsRejected(
        string before, string after)
    {
        Assert.Throws<FormatException>(() => ItemDefinition.Parse(
            WearableJson.Replace(before, after, StringComparison.Ordinal)));
    }

    [Fact]
    public void AppearanceRequiresRealEquippableItem()
    {
        var notEquippable = WearableJson.Replace(
            "\"equipmentSlot\":\"helmet\",", "", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => ItemDefinition.Parse(notEquippable));
        var stacked = WearableJson.Replace(
            "\"maxStackSize\":1", "\"maxStackSize\":64",
            StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => ItemDefinition.Parse(stacked));
    }

    [Fact]
    public void OtherSlotsMayUseOnlyTheirAnimatedBodyMeshes()
    {
        const string legs = """
            {"id":"asteria:greaves","category":"tools","icon":"textures/items/greaves.png",
             "maxStackSize":1,"equipmentSlot":"legs",
             "equipmentVisual":{"parts":[
              {"mesh":"LeftLegMesh","size":[1.1,0.5,1.1],
               "offset":[0,0.2,0],"color":"#8899AA"},
              {"mesh":"RightLegMesh","size":[1.1,0.5,1.1],
               "offset":[0,0.2,0],"color":"#8899AA"}
             ]}}
            """;
        Assert.Equal(2, ItemDefinition.Parse(legs).EquipmentVisual!.Parts.Count);
        Assert.Throws<FormatException>(() => ItemDefinition.Parse(
            legs.Replace("RightLegMesh", "RightArmMesh", StringComparison.Ordinal)));
    }

}
