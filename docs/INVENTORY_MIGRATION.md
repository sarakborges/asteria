# Inventory migration contract

Reference: MineClone `world-systems-rebuild` branch, `src/player/{hotbar,item_stack,inventory}.rs`, `src/gameplay/modal.rs`, `src/hud/inventory.rs`.

## Implemented

Asteria has a Core-owned **block-only inventory** with 27 backpack slots, nine selected-hotbar slots, 64 blocks per stack, a carried cursor, deterministic merge/insert ordering and drop consumption. The inventory is owned by `PlayerSessionState` across Spheres, not by the UI or the selected dimension. Block identity uses `BlockStateSnapshot`, so orientation, facing, voxel state, texture rotation and carried microblock mask remain part of stack compatibility.

Physical drops are accepted transactionally through `DroppedBlockRuntime`: a rejected pickup never removes an entity. Native Godot input handles hotbar selection (1–9 and wheel), inventory (configurable, E default) and dropping (configurable, Q default). Survival consumes on placement; Creative has nonconsuming placement and authored block catalog, with loot suppressed on breaking. Spectator cannot interact or open inventory.

WebUI observes `game.inventory.state` and `game.inventory.catalog` through `InventoryController`/UiStore, and sends semantic actions; it does not intercept global keys. `InventoryGameplayPage` uses existing inventory panels and slot components without simulated character stats, crafting recipes or station state.

## Not yet parity

- Non-block item/tool stacks and item metadata policies; their type/model must be added to Core rather than fabricated by the Godot adapter
- Item-specific stack limits, creative item/tool catalogs, full crafting, equipment, cursor split/shift-click and recipe validation
- Restoring microblock geometry upon re-placement (pickup preserves mask, placement is currently rejected for masks rather than corrupting authored geometry)
- Player and world disk saves/catalog, inventory save versioning and load migration
- Automated GUI interaction testing inside the Godot WebView beyond CI compilation/Storybook

## Invariants

- Exactly one authoritative player inventory; UI state is an immutable presentation snapshot
- No negative world Y and no Godot resource imports inside packs
- No picked-up drop disappears before successful capacity acceptance
- No invalid/unowned item is fabricated to make the inventory screen look populated
- On inventory close, carried cursor must be reintegrated; reject closing if no capacity remains
- World/Sphere transitions share the player inventory but each dimension retains its own physical dropped entities
