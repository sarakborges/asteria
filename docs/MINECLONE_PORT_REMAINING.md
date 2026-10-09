# MineClone → Asteria: remaining port parity

**Reviewed:** 2026-10-09. **Asteria `main` inspected:** `0346901e15a66430a83b143b903005a28608d36c`. Historical UI comparison: MineClone `main` (`a4e9d00`) and `world-systems-rebuild` (`e8bc856`). This is a **code/content audit**, not a live Godot/WRY or side-by-side screenshot sign-off. Re-check both source heads when implementing any item; priority and status are a dated snapshot.

**Scope decision:** World Recipes are excluded at the user's request. They are **not** a blocker, planned milestone, or missing-parity ticket. This does **not** exclude ordinary inventory Crafting.

## How to read the status

- **Integrated in code:** authoritative Core/runtime + Godot bridge + React path exists, with relevant automated tests; **not** equivalent to in-game functional parity.
- **Partial:** meaningful code/content exists, but at least one reference behavior, asset, native action, or presentation binding is missing.
- **Missing:** the requested feature-specific system/content was not identified in the reviewed Asteria paths.
- **Unverified:** requires the actual embedded Godot/WRY client, pack-asset rendering, input and/or side-by-side MineClone comparison. No CI/Storybook success alone closes this.

## P0 — remaining gameplay and world content

| Feature | Current evidence / status | Required work and acceptance |
| --- | --- | --- |
| **Enchanted Heart / World Tree / enchanted sources** | **Missing as a complete feature.** The default pack has `enchanted_forest` and `tree_enchanted_01..04`, but ordinary biome/trees are not the World Tree + magical source feature. | Port/adapt authored structures, material/placement logic and interactions; make them discoverable under the intended biome rules; deterministic placement, collision and persistence tests. Do not conflate pre-existing enchanted trees with the main feature. |
| **Cavern entrances and connected tunnels** | **Missing as a specific feature.** Generic structures, structure sets, caves and `caverns` exist; there is no verified MineClone entrance/tunnel connector contract. | Inspect MineClone source, represent entrances and connected passages in the authored structure/connector model (or extend it cleanly), ensure terrain intersection and access to the surface, chunk-independent deterministic tests and in-game navigation. |
| **Physical magic portals between Spheres** | **Missing.** The `/warp`/dimension-travel lifecycle is integrated, but does not place/activate a portal. | Use **Asteria's own design**, not Minecraft-like frames: materials create a spell/scroll, the scroll is placed on the ground and activated by a native interaction, a portal connects the *same exact coordinates* in another Sphere, and safe carving/access structures lead to the surface when necessary. Reuse dimension destination and world mutation/placement owners; validate collisions, rollback, saved state and repeat activations. |
| **World Selection: delete** | **Partial.** `WorldLoadController`, `WorldCatalogScanController`, `ui.world.catalog.load` and `App.tsx` `onLoad` enable validated loading. `WorldSelectionPage.onDelete` has no mounted authoritative action. | Add safe directory-identity validation, explicit delete intent/confirmation, no deletion of an active or loading world, bounded filesystem operation, refreshed catalog and failure feedback; cover recovery/permissions and Godot/WRY interaction. |
| **Pause: save and leave world** | **Partial.** `Ctrl+S` and `ui.world.save` checkpoint via `DimensionSessionController`, and `PauseMenuPage` shows localized result feedback. `onLeaveWorld` is still not provided by `App.tsx`. | Implement a distinct native save-and-leave/return-to-menu lifecycle; finish publication, retire workers/presentation, release gameplay input, clear session-owned resources, restore pre-world UI and handle save failure without silently losing progress. Validate repeated enter/leave cycles. |

## P1 — player, inventory and visuals

| Feature | Current evidence / status | Required work and acceptance |
| --- | --- | --- |
| **Run and crouch** | **Missing reference-specific behavior.** `FpsPlayer` has movement, jump, fly and swim, but no verified run/sprint/crouch state, capsule change or animations. | Inspect MineClone key semantics (including double-tap W), acceleration and restrictions; implement run/crouch in native player control and collision-safe stand-up checks, adapt camera and animation, expose real keybinds/controls hints; test grounded, underwater, flight and constrained-height cases. |
| **Player model, equipment and portraits** | **Partial UI foundation.** Character/HUD cards exist; `CharacterInfoPanel` lacks a real render-texture/interactive 3D portrait, and `InventoryGameplayPage` does not expose authored equipped slots. | Port/adapt player visual assets and animation/perspective, explicit equipment state and its UI, in-engine portrait/drag orbit with correct render lifecycle; use snapshot presentation only in React. Verify local player and targeted creature portraits. |
| **Sky and celestial textures** | **Partial.** `DimensionEnvironmentPresentation` creates sun/moon orbital meshes and time-based lighting, but the MineClone celestial PNG/material presentation is not verified. | Compare original sun/moon assets and day/night material transitions; load selected-pack textures without Godot sidecars; verify eclipse/orbit/tint/fog behavior only if genuinely authored. |
| **Stars, clouds, biome atmosphere** | **Partial.** Day/night, color/fog, wind and ambient particles exist; no verified parity for stars/clouds and biome-specific atmosphere transitions. | Inspect MineClone atmospheric owners/assets; separate engine presentation from authored climate/biome parameters, avoid per-frame unbounded work; screenshot-test per Sphere/biome and day/night states. |
| **Held-item dynamic light** | **Missing.** RGB voxel lighting and static authored emissions exist; no equipped-item moving-light contract confirmed. | Define equipment-origin light source with authoritative emission strength, bounded incremental propagation/retraction and stale-worker protection; no permanent voxel mutations when player moves. Validate crossing chunks and dimension changes. |
| **Creative inventory categories** | **Partial end-to-end.** `packs/default/data/inventory_categories/` and original category PNGs/localizations are ported. `InventoryCategoryRegistry` and `Main.SendCreativeCatalog` publish order/icons. **React `InventoryController` currently ignores `categories` and `everythingIconUrl`; `InventoryGameplayPage` still infers/sorts IDs, and the panel renders text-only buttons.** | Parse/validate authored category descriptors in the controller/store; display localized labels and original icons, preserve authored order and per-category/search scroll; search localized content names and metadata; add stories, browser interaction tests and native QA. |
| **HUD icons, bucket variants and stack names** | **Partial.** Item/tool catalog PNGs exist; target/block preview and local portrait can still be fallback text. Buckets preserve `contained_fluid` metadata but lack fluid-specific displayed icons/labels. | Port pack-authored variant icons (including filled bucket), metadata-sensitive stack/tooltip/hotbar labels, real target/block render previews and tint where appropriate; verify invalid metadata fallbacks. |
| **Layers / portal layer** | **Partial.** Attached layers, dye, Brush, Shears, moss, rendering and persistence are implemented; the portal-specific visual layer and interaction are not. | Compare MineClone authored portal layer semantics and implement only as needed by Asteria's portal design; retain generic data-driven `AttachedLayerRegistry` and canonical voxel mutations. |
| **Creature effects / per-species behavior** | **Partial.** Creatures, natural spawning, hop locomotion, combat and loot exist; species effects/particle and AI parity are not fully audited. | Build an exact creature-by-creature matrix of behavior, particles, damage/death presentation and loot, then port verified gaps. Never assume visual parity from generic creature runtime tests. |

## P2 — final UX and verification

| Feature | Current evidence / status | Required work and acceptance |
| --- | --- | --- |
| **Inventory, equipment, controls and additional HUD details** | **Partial.** Inventory cursor, hotbar, Crafting and Storage Box overlay are wired, but inventory search, item-specific glyphs, empty equipment frames, creative scrolling, some control semantics and target preview differ from MineClone. | Close the specific rows in [UI parity audit](UI_PARITY_AUDIT.md); preserve Asteria's inventory-above-crafting layout and Winky Rough Variable. |
| **Create New World / settings parity** | **Implemented foundation, unverified WRY.** Name and **Seed in World Settings**, World Generation (Normal/Flat/Void, biome, multiplier, structures/single biome/caves/oceans) and Game Rules are connected. | Verify source accuracy against **MineClone `main`** (not rebuild alone), in-game left sidebar/toggles, biome name localization, focus/keybind behavior and visible output of each generator option. |
| **Save/Load hardening** | **Integrated in code.** Complete session serialization, recoverable generations, catalog validation and native Save/Load routing exist; checkpoint and native entry share the Core state. | Exercise save → exit → fresh process → load with restored player, containers, entities, chunks and active Sphere; corrupted generation rollback, pack mismatch, double-click and worker failure. Complete the delete/save-and-leave work above. |
| **Visual and behavioral parity pass** | **Unverified.** Godot builds, Core tests, React/Storybook and Chromium visual checks exist, but not a complete MineClone-vs-Asteria in-engine run. | Compare fixed viewports (1920×1080, 1280×720 and narrow), each screen/HUD/modal and interactive state, first/third-person input, controller focus, Escape, gamepad if supported, load/save/reentry, and actual content/asset rendering. Record screenshots and discrepancies. |

## Already implemented — do not restart from zero

- **Inventory Crafting:** `CraftingRecipeDefinition`, `InventoryCraftingRuntime`, Core transaction, Godot `ui.inventory.craft`, React `CraftingPanel` and feedback. WRY interaction QA remains open.
- **Save/Load + World Load:** `GameplaySessionFileCodec`, `SessionSaveStorage`, `WorldLoadController`, active-Sphere persistence, atomic generation recovery and catalog validation. The remaining work is lifecycle polish and live QA.
- **Storage Boxes:** 27-slot authoritative Core containers, native actions, `StorageBoxPage` mounted in `App.tsx`, drops and persistence; still verify play interactions.
- **Worldgen/runtime foundation:** surface and volume biomes, caves, blending, structure/structure sets, fluids, RGB lighting, streaming/meshing, spawn, day/night, wind/particles.
- **Gameplay basics:** mining progress and crack overlay, Artisan's Kit, Brush, Bucket, Shears, combat, creature spawning/loot and native chat commands.

These are **code-level findings**, not a declaration that visual worldgen, gameplay polish or engine behavior are already correct.

## Recommended implementation sequence

1. Native player run/crouch plus missing third-person/player presentation (small independently testable increments).
2. Cavern entrances/connectors and Enchanted Heart / World Tree content.
3. Celestial assets, clouds/stars/biome atmosphere, then held-item dynamic lighting.
4. Finish creative category rendering, inventory/HUD metadata, portraits and settings/UI details.
5. Implement magic scroll-based physical portals reusing the existing Sphere travel and generated destination capabilities.
6. Complete world delete, save-and-leave and full persistence regression across process restart.
7. Side-by-side MineClone/Asteria Godot/WRY functional and visual QA. Keep fixes scoped to the observed discrepancy.

**Do not count Storybook/CI green as in-game parity.** When closing a row, attach exact authored data paths, Core owner, Godot adapter, UI contract (if any), tests and a recorded Godot/WRY observation. No global WebUI gameplay input, engine resources inside packs, negative Y, or new duplicated world systems.
