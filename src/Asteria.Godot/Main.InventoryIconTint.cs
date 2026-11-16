using Asteria.Client.Rendering;
using Asteria.Core.World;

namespace Asteria.Client;

public partial class Main
{
    private readonly InventoryIconTintSampler _inventoryIconTints = new();
    private InventoryIconTintPalette? _lastPublishedCatalogTint;

    private BlockPreviewColor? PlayerInventoryIconTint(
        BlockTint tint, BlockPreviewColor fallback) =>
        _inventoryIconTints.Resolve(tint, fallback);

    private void ResetInventoryIconTint()
    {
        _inventoryIconTints.Reset();
        _lastPublishedCatalogTint = null;
    }

    private void MarkCreativeCatalogTintPublished() =>
        _lastPublishedCatalogTint = _inventoryIconTints.Palette;

    private bool CreativeCatalogTintChanged() =>
        _lastPublishedCatalogTint != _inventoryIconTints.Palette;

    private bool HotbarContainsBiomeTintedContent()
    {
        var inventory = _sessionStates.Player.Inventory;
        for (var i = 0; i < PlayerInventory.HotbarSlots; i++)
        {
            var stack = inventory.SlotAt(PlayerInventory.BackpackSlots + i);
            if (stack is null) continue;
            if (stack.Kind == InventoryEntryKind.Block &&
                _blocks.TryGetId(stack.Id, out var id) &&
                _blocks.GetDefinition(id).Tint != BlockTint.None)
                return true;
            if (stack.Kind == InventoryEntryKind.Object &&
                _objects.TryGet(stack.Id, out var obj) &&
                obj is { Tint: not BlockTint.None })
                return true;
            if (stack.Kind == InventoryEntryKind.Layer &&
                _layers.TryGet(stack.Id, out var layer) &&
                layer is { Tint: not BlockTint.None })
                return true;
        }
        return false;
    }

    private void SyncInventoryIconTint()
    {
        if (_player is null || _worldSeed is null) return;
        if (!_inventoryIconTints.Sync(
            _sessions.Active.Generator.Tints, _player.GlobalPosition)) return;
        if (HotbarContainsBiomeTintedContent())
            SendHotbarState();
        if (_inventoryOpen && CreativeCatalogTintChanged())
        {
            SendCreativeCatalog();
            SendInventoryState();
        }
    }
}
