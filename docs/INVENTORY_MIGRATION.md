# Inventory migration contract

Reference: MineClone `world-systems-rebuild` branch, `src/player/{hotbar,item_stack,inventory}.rs`, `src/gameplay/modal.rs`, `src/hud/inventory.rs`.

## Implemented

Asteria has a Core-owned inventory with 27 backpack slots, nine selected-hotbar slots, validated stack limits, a carried cursor, deterministic merge/insert ordering and block-drop consumption. The inventory is owned by `PlayerSessionState` across Spheres, not by the UI or the selected dimension. Block identity uses `BlockStateSnapshot`, so orientation, facing, voxel state, texture rotation and carried microblock mask remain part of stack compatibility.

Physical drops are accepted transactionally through `DroppedBlockRuntime`: a rejected pickup never removes an entity, and item/tool drops retain their metadata and authored stack limits. Native Godot input handles hotbar selection (1–9 and wheel), inventory (configurable, E default) and dropping (configurable, Q default). Survival consumes on placement; Creative has nonconsuming placement and authored block catalog, with loot suppressed on breaking. Spectator cannot interact or open inventory.

WebUI observes `game.inventory.state` and `game.inventory.catalog` through `InventoryController`/UiStore, including typed item/tool identities, metadata and authored icon previews; it does not intercept global keys. `InventoryGameplayPage` uses existing inventory panels and real runtime-backed crafting recipes; player equipment/portrait and specialized stations still need their own verified gameplay/presentation contracts.

## Item and tool stacks (2026-10-08)

- Core `InventoryEntry` unifies **block**, **item**, and **tool** identity. Entries carry validated namespaced IDs, complete portable block state when applicable, immutable sorted string metadata, and their authored stack limit. Equality/stacking includes all identity fields; metadata variants cannot silently merge. Tools default to 1 per stack; items and blocks default to 64. Optional `maxStackSize` in item/tool data files is validated in the range 1–64.
- `PlayerInventory` remains the single owner of slots, cursor, creative picks, insertion, atomic capacity rejection, consumption, and deterministic sorting/compaction across metadata-compatible stacks.
- Core `InventoryContentCatalog` resolves real definitions from the loaded block/item/tool registries and authenticates Creative picks (including declared `iconVariants` metadata). The WebUI cannot submit arbitrary item variants, IDs or stack sizes to create content.
- Item and tool textures are loaded from validated pack resource paths in Godot, encoded as bounded PNG data URLs and transmitted in the Creative catalog, not in repeated per-slot snapshots. The WebUI reuses those icons for the inventory and hotbar, including metadata variants such as the Umbral Dimensional Slicer.
- Items and tools occupy real inventory slots, can be sorted, moved and obtained from the Creative catalog. All three inventory kinds now support physical drop rendering and pickup. Blocks use existing voxel mesh presentation; item/tool drops use authored pack sprites. Q drops exactly one entry and consumes it only after the world accepts the spawn.

## Still not full parity (updated 2026-10-09)

- Brush, Bucket, Artisan's Kit, Shears, Structure Tool and the world-space mining crack overlay have **already** been integrated. Do not treat them as unimplemented.
- Cursor splitting/shift-click, equipped slots, held-tool swing animation, metadata-specific bucket visuals and non-block rewards/loot policy still require comparison/implementation.
- The portable microblock snapshot exists; unsupported re-placement must be reviewed rather than silently changing geometry.
- Complete world/session disk snapshots, catalog validation and world loading **exist**. Unfinished World Delete, Save and Leave, historical migration policy and live process-restart QA are tracked in [remaining port roadmap](MINECLONE_PORT_REMAINING.md).
- Creative category definitions, original PNGs, authored order and localization are integrated through Godot and React. Godot/WRY native clicks, image decoding, localized search and scroll restoration still need in-game verification.
- End-to-end gameplay interactions in the Godot/WRY WebView remain unverified by Storybook and CI.

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

## Bucket inventory metadata (2026-10-08)

- The authored bucket's right-click behavior is now operational through `BucketGameplayRuntime`. Only one selected bucket may transition from empty to `contained_fluid` metadata (and back); stale selections or stacked bucket quantities are rejected without duplicating fluids/items. Source collection and adjacent placement are validated in the Core and passed to `VoxelMutationRuntime`; Godot forwards the native input ray only.
