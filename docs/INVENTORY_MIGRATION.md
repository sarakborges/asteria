# Inventory migration contract

Reference: MineClone `world-systems-rebuild` branch, `src/player/{hotbar,item_stack,inventory}.rs`, `src/gameplay/modal.rs`, `src/hud/inventory.rs`.

## Implemented

Asteria has a Core-owned inventory with 27 backpack slots, nine selected-hotbar slots, validated stack limits, a carried cursor, deterministic merge/insert ordering and block-drop consumption. The inventory is owned by `PlayerSessionState` across Spheres, not by the UI or the selected dimension. Block identity uses `BlockStateSnapshot`, so orientation, facing, voxel state, texture rotation and carried microblock mask remain part of stack compatibility.

Physical drops are accepted transactionally through `DroppedBlockRuntime`: a rejected pickup never removes an entity. Native Godot input handles hotbar selection (1–9 and wheel), inventory (configurable, E default) and dropping (configurable, Q default). Survival consumes on placement; Creative has nonconsuming placement and authored block catalog, with loot suppressed on breaking. Spectator cannot interact or open inventory.

WebUI observes `game.inventory.state` and `game.inventory.catalog` through `InventoryController`/UiStore, including typed item/tool identities, metadata and authored icon previews; it does not intercept global keys. `InventoryGameplayPage` uses existing inventory panels and slot components without simulated character stats, crafting recipes or station state.

## Item and tool stacks (2026-10-08)

- Core `InventoryEntry` unifies **block**, **item**, and **tool** identity. Entries carry validated namespaced IDs, complete portable block state when applicable, immutable sorted string metadata, and their authored stack limit. Equality/stacking includes all identity fields; metadata variants cannot silently merge. Tools default to 1 per stack; items and blocks default to 64. Optional `maxStackSize` in item/tool data files is validated in the range 1–64.
- `PlayerInventory` remains the single owner of slots, cursor, creative picks, insertion, atomic capacity rejection, consumption, and deterministic sorting/compaction across metadata-compatible stacks.
- Core `InventoryContentCatalog` resolves real definitions from the loaded block/item/tool registries and authenticates Creative picks (including declared `iconVariants` metadata). The WebUI cannot submit arbitrary item variants, IDs or stack sizes to create content.
- Item and tool textures are loaded from validated pack resource paths in Godot, encoded as bounded PNG data URLs and transmitted in the Creative catalog, not in repeated per-slot snapshots. The WebUI reuses those icons for the inventory and hotbar, including metadata variants such as the Umbral Dimensional Slicer.
- Items and tools can occupy player inventory slots, be moved, sorted, returned or discarded as cursor entries, and be obtained from the Creative catalog. Only **blocks** currently have physical world drop rendering/pickup and placement mechanics. Pressing the drop key with an item/tool leaves it intact and displays a localized notice.

## Not yet parity

- Gameplay behaviors/mining effects for tools, item usage, equipment and crafting/recipe validation (holding a tool is not evidence its authored behavior has been executed)
- World-entity simulation/presentation, dropping and pickup for non-block items/tools
- Cursor splitting/shift-click, equipped slots, authored rewards/loot tables for non-block drops
- Re-placing microblock geometry (portable snapshot retains the mask; invalid placement is rejected instead of corrupting geometry)
- Player/world disk saves, catalog, inventory snapshot versioning and migrations
- Automated GUI interaction inside the Godot WebView beyond CI compilation and Storybook

## Invariants

- Exactly one authoritative player inventory; UI state is an immutable presentation snapshot
- No negative world Y and no Godot resource imports inside packs
- No picked-up drop disappears before successful capacity acceptance
- No invalid/unowned item is fabricated to make the inventory screen look populated
- On inventory close, carried cursor must be reintegrated; reject closing if no capacity remains
- World/Sphere transitions share the player inventory but each dimension retains its own physical dropped entities
