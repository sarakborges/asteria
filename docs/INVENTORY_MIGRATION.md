# Inventory migration contract

Reference: MineClone `world-systems-rebuild` branch, `src/player/{hotbar,item_stack,inventory}.rs`, `src/gameplay/modal.rs`, `src/hud/inventory.rs`.

## Implemented

Asteria has a Core-owned inventory with 27 backpack slots, nine selected-hotbar slots, validated stack limits, a carried cursor, deterministic merge/insert ordering and block-drop consumption. The inventory is owned by `PlayerSessionState` across Spheres, not by the UI or the selected dimension. Block identity uses `BlockStateSnapshot`, so orientation, facing, voxel state, texture rotation and carried microblock mask remain part of stack compatibility.

Physical drops are accepted transactionally through `DroppedBlockRuntime`: a rejected pickup never removes an entity, and item/tool drops retain their metadata and authored stack limits. Native Godot input handles hotbar selection (1–9 and wheel), inventory (configurable, E default) and dropping (configurable, Q default). Survival consumes on placement; Creative has nonconsuming placement and authored block catalog, with loot suppressed on breaking. Spectator cannot interact or open inventory.

WebUI observes `game.inventory.state` and `game.inventory.catalog` through `InventoryController`/UiStore, including typed item/tool identities, metadata and authored icon previews; it does not intercept global keys. `InventoryGameplayPage` uses existing inventory panels and slot components without simulated character stats, crafting recipes or station state.

## Item and tool stacks (2026-10-08)

- Core `InventoryEntry` unifies **block**, **item**, and **tool** identity. Entries carry validated namespaced IDs, complete portable block state when applicable, immutable sorted string metadata, and their authored stack limit. Equality/stacking includes all identity fields; metadata variants cannot silently merge. Tools default to 1 per stack; items and blocks default to 64. Optional `maxStackSize` in item/tool data files is validated in the range 1–64.
- `PlayerInventory` remains the single owner of slots, cursor, creative picks, insertion, atomic capacity rejection, consumption, and deterministic sorting/compaction across metadata-compatible stacks.
- Core `InventoryContentCatalog` resolves real definitions from the loaded block/item/tool registries and authenticates Creative picks (including declared `iconVariants` metadata). The WebUI cannot submit arbitrary item variants, IDs or stack sizes to create content.
- Item and tool textures are loaded from validated pack resource paths in Godot, encoded as bounded PNG data URLs and transmitted in the Creative catalog, not in repeated per-slot snapshots. The WebUI reuses those icons for the inventory and hotbar, including metadata variants such as the Umbral Dimensional Slicer.
- Items and tools occupy real inventory slots, can be sorted, moved and obtained from the Creative catalog. All three inventory kinds now support physical drop rendering and pickup. Blocks use existing voxel mesh presentation; item/tool drops use authored pack sprites. Q drops exactly one entry and consumes it only after the world accepts the spawn.

## Not yet parity

- Additional authored behaviors for brush, bucket, artisans kit, shears and structure tool; item usage, equipment and crafting/recipe validation
- World-space crack overlays and held-tool swing animation during mining
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

## Physical item/tool drops and initial tool behavior (2026-10-08)

- Expanded the existing Core world-drop simulation to carry a one-item `InventoryStack`, rather than starting parallel physics for non-block objects. Gravity, world collision, settle/wake, contact separation, 0.4-second pickup grace, bounded active count, lifetime, Sphere archival and deterministic drop IDs remain shared for blocks/items/tools.
- Godot publishes block drops using existing 3D block meshes and non-block drops as spinning authored PNG sprites. One validated pack resource path owns each cached texture; no Godot import artifacts are written into packs. The inventory only consumes after a successful spawn; pickup uses the Core inventory acceptance boundary to preserve metadata and prevent capacity loss.
- `ToolGameplayRuntime` uses authored `leftBehavior`/`rightBehavior` and category requirements in Core. The Carpenter's Axe supports `asteria:log/hollow` on left and `asteria:log/strip` on right, with logged variant transitions authored through block registries and mutations published through `VoxelMutationRuntime`. In Survival, `requiredTools` now rejects the wrong mining category; Creative can break mineable blocks independently of held tool.
- `preferredTools`, hardness and speed are now active through `BlockMiningRuntime` as described in [Mining migration](MINING_MIGRATION.md). No invented item use, recipe, generic tool effect, or unsupported catalog action is considered functional.
