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

    private void SyncInventoryIconTint()
    {
        if (_player is null || _worldSeed is null) return;
        if (!_inventoryIconTints.Sync(
            _sessions.Active.Generator.Tints, _player.GlobalPosition)) return;
        SendHotbarState();
        if (_inventoryOpen && CreativeCatalogTintChanged())
        {
            SendCreativeCatalog();
            SendInventoryState();
        }
    }
}
