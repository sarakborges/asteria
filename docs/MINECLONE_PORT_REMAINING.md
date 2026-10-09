# MineClone → Asteria: remaining port parity

**Reviewed:** 2026-10-09. **Asteria `main` inspected:** `0346901e15a66430a83b143b903005a28608d36c`. Historical UI comparison: MineClone `main` (`a4e9d00`) and `world-systems-rebuild` (`e8bc856`). This is a **code/content audit**, not a live Godot/WRY or side-by-side screenshot sign-off. Re-check both source heads when implementing any item; priority and status are a dated snapshot.

**Scope decisions:** World Recipes are excluded at the user's request. This does **not** exclude ordinary inventory Crafting. Biome and biome-generation work, including biome-specific structures, terrain/cavern connectivity and regional atmosphere, belongs to the separate biome backlog. It is **not** a port-parity milestone or blocker in this list.

## How to read the status

- **Integrated in code:** authoritative Core/runtime + Godot bridge + React path exists, with relevant automated tests; **not** equivalent to in-game functional parity.
- **Partial:** meaningful code/content exists, but at least one reference behavior, asset, native action, or presentation binding is missing.
- **Missing:** the requested feature-specific system/content was not identified in the reviewed Asteria paths.
- **Unverified:** requires the actual embedded Godot/WRY client, pack-asset rendering, input and/or side-by-side MineClone comparison. No CI/Storybook success alone closes this.

## P0 — remaining gameplay and world management

| Feature | Current evidence / status | Required work and acceptance |
| --- | --- | --- |
| **Physical magic portals between Spheres** | **Missing.** The `/warp`/dimension-travel lifecycle is integrated, but does not place/activate a portal. | Use **Asteria's own design**, not Minecraft-like frames: materials create a spell/scroll, the scroll is placed on the ground and activated by a native interaction, a portal connects the *same exact coordinates* in another Sphere, and safe carving/access structures lead to the surface when necessary. Reuse dimension destination and world mutation/placement owners; validate collisions, rollback, saved state and repeat activations. |
| **World Selection: delete** | **Partial.** `WorldLoadController`, `WorldCatalogScanController`, `ui.world.catalog.load` and `App.tsx` `onLoad` enable validated loading. `WorldSelectionPage.onDelete` has no mounted authoritative action. | Add safe directory-identity validation, explicit delete intent/confirmation, no deletion of an active or loading world, bounded filesystem operation, refreshed catalog and failure feedback; cover recovery/permissions and Godot/WRY interaction. |
| **Pause: save and leave world** | **Partial.** `Ctrl+S` and `ui.world.save` checkpoint via `DimensionSessionController`, and `PauseMenuPage` shows localized result feedback. `onLeaveWorld` is still not provided by `App.tsx`. | Implement a distinct native save-and-leave/return-to-menu lifecycle; finish publication, retire workers/presentation, release gameplay input, clear session-owned resources, restore pre-world UI and handle save failure without silently losing progress. Validate repeated enter/leave cycles. |

## P1 — player, inventory and visuals

| Feature | Current evidence / status | Required work and acceptance |
| --- | --- | --- |
| **Run and crouch** | **Implemented in code; native QA pending.** `PlayerGroundMovement` detects W ×2 within 12 ticks, with 1.5× run and 0.3× crouch; `FpsPlayer` applies real 1.5-block crouched capsule, camera drop, stand-up collision probe and ledge-support check. | Verify these transitions in Godot/WRY (low roof, edges, swimming, flight, third-person cameras). Player-model crouch animation belongs to the separate presentation task. |
| **Player model, equipment and portraits** | **Partially integrated in code, Godot/WRY QA pending.** Original GLB, skin atlases, locomotion animations, offscreen 3D portrait, semantic drag-orbit and authoritative four-slot equipment inventory are implemented. | Author real equippable content/effects, add held-equipment meshes, action/hurt/death animations and player-model outer skin layers. Verify GLB, portrait, camera visibility and target-creature portraits in Godot/WRY. |
| **Sky and celestial textures** | **Partial.** `DimensionEnvironmentPresentation` creates sun/moon orbital meshes and time-based lighting, but the MineClone celestial PNG/material presentation is not verified. | Compare original sun/moon assets and day/night material transitions; load selected-pack textures without Godot sidecars; verify eclipse/orbit/tint/fog behavior only if genuinely authored. |
| **Stars and clouds** | **Partial.** Day/night sky presentation, wind and ambient particles exist; visual parity for stars and clouds remains unverified. | Compare MineClone sky assets and day/night behavior, avoid unbounded frame work and verify the sky presentation in Godot/WRY. |
| **Held-item dynamic light** | **Missing.** RGB voxel lighting and static authored emissions exist; no equipped-item moving-light contract confirmed. | Define equipment-origin light source with authoritative emission strength, bounded incremental propagation/retraction and stale-worker protection; no permanent voxel mutations when player moves. Validate crossing chunks and dimension changes. |
| **Creative inventory categories** | **Partial end-to-end.** `packs/default/data/inventory_categories/` and original category PNGs/localizations are ported. `InventoryCategoryRegistry` and `Main.SendCreativeCatalog` publish order/icons. **React `InventoryController` currently ignores `categories` and `everythingIconUrl`; `InventoryGameplayPage` still infers/sorts IDs, and the panel renders text-only buttons.** | Parse/validate authored category descriptors in the controller/store; display localized labels and original icons, preserve authored order and per-category/search scroll; search localized content names and metadata; add stories, browser interaction tests and native QA. |
| **HUD icons, metadata variants and stack names** | **Partial.** Item/tool PNGs and authored water/lava bucket icon variants are integrated for held-item/creative visuals; target/block previews and metadata-sensitive hotbar/tooltip names still require parity. | Render actual target/block previews, consume metadata-aware icons and localized stack labels consistently across HUD/inventory and test invalid metadata fallbacks. |
| **Layers / portal layer** | **Partial.** Attached layers, dye, Brush, Shears, moss, rendering and persistence are implemented; the portal-specific visual layer and interaction are not. | Compare MineClone authored portal layer semantics and implement only as needed by Asteria's portal design; retain generic data-driven `AttachedLayerRegistry` and canonical voxel mutations. |
| **Creature effects / per-species behavior** | **Partial.** Creatures, natural spawning, hop locomotion, combat and loot exist; species effects/particle and AI parity are not fully audited. | Build an exact creature-by-creature matrix of behavior, particles, damage/death presentation and loot, then port verified gaps. Never assume visual parity from generic creature runtime tests. |

## P2 — final UX and verification

| Feature | Current evidence / status | Required work and acceptance |
| --- | --- | --- |
| **Inventory, equipment, controls and additional HUD details** | **Partial.** Inventory cursor, hotbar, Crafting and Storage Box overlay are wired, but inventory search, item-specific glyphs, empty equipment frames, creative scrolling, some control semantics and target preview differ from MineClone. | Close the specific rows in [UI parity audit](UI_PARITY_AUDIT.md); preserve Asteria's inventory-above-crafting layout and Winky Rough Variable. |
| **Create New World / general settings UI parity** | **Implemented foundation, unverified WRY.** Name/Seed in World Settings, Normal/Flat/Void mode, size multiplier and Game Rules are connected. | Compare MineClone `main` sidebar, navigation and non-biome controls, focus/keybind behavior, enabled/disabled states and runtime wiring in Godot/WRY. |
| **Save/Load hardening** | **Integrated in code.** Complete session serialization, recoverable generations, catalog validation and native Save/Load routing exist; checkpoint and native entry share the Core state. | Exercise save → exit → fresh process → load with restored player, containers, entities, chunks and active Sphere; corrupted generation rollback, pack mismatch, double-click and worker failure. Complete the delete/save-and-leave work above. |
| **Visual and behavioral parity pass** | **Unverified.** Godot builds, Core tests, React/Storybook and Chromium visual checks exist, but not a complete MineClone-vs-Asteria in-engine run. | Compare fixed viewports (1920×1080, 1280×720 and narrow), each screen/HUD/modal and interactive state, first/third-person input, controller focus, Escape, gamepad if supported, load/save/reentry, and actual content/asset rendering. Record screenshots and discrepancies. |

## Already implemented — do not restart from zero

- **Inventory Crafting:** `CraftingRecipeDefinition`, `InventoryCraftingRuntime`, Core transaction, Godot `ui.inventory.craft`, React `CraftingPanel` and feedback. WRY interaction QA remains open.
- **Save/Load + World Load:** `GameplaySessionFileCodec`, `SessionSaveStorage`, `WorldLoadController`, active-Sphere persistence, atomic generation recovery and catalog validation. The remaining work is lifecycle polish and live QA.
- **Storage Boxes:** 27-slot authoritative Core containers, native actions, `StorageBoxPage` mounted in `App.tsx`, drops and persistence; still verify play interactions.
- **Runtime foundation:** fluids, RGB lighting, streaming/meshing, day/night, wind and particles.
- **Gameplay basics:** mining progress and crack overlay, Artisan's Kit, Brush, Bucket, Shears, combat, creature spawning/loot and native chat commands.

These are **code-level findings**, not a declaration that gameplay polish or in-engine presentation is already correct.

## Recommended implementation sequence

1. Complete World Delete and Save and Leave, with native lifecycle checks and fresh-process Save/Load regression.
2. Finish creative category rendering, inventory/HUD metadata, equipment presentation, portraits and general settings/UI details.
3. Verify native run/crouch and third-person presentation; add remaining player/equipment action visuals.
4. Port celestial assets, stars/clouds and held-item dynamic lighting.
5. Implement magic scroll-based physical portals using the existing Sphere travel and destination capabilities.
6. Audit creature-specific effects and behavior, then close verified parity gaps.
7. Run side-by-side MineClone/Asteria Godot/WRY functional and visual QA, fixing only observed discrepancies.

**Do not count Storybook/CI green as in-game parity.** When closing a row, attach exact authored data paths, Core owner, Godot adapter, UI contract (if any), tests and a recorded Godot/WRY observation. No global WebUI gameplay input, engine resources inside packs, negative Y, or new duplicated world systems.

## Movement port progress — 2026-10-09

- The Core `PlayerGroundMovement` now owns transient W double-tap detection (12 world ticks), 1.5× run multiplier, 0.3× crouch multiplier and crouch-over-run behavior, tested independently of Godot.
- Godot `FpsPlayer` applies native keyboard movement, accelerates/decelerates ground velocity, lowers the collision capsule from 1.8 to 1.5 blocks, blends the camera down by 0.35 blocks and checks full-height collision before allowing the player to stand. While crouching on ground, support probes prevent horizontal travel beyond ledges; immersed/flight movement retains its separate authored rules.
- Existing Asteria base movement speed remains unchanged (7.5) to avoid an unrelated balance regression; MineClone's reference walking speed is 5.0. The control guide now includes W ×2 Run, and configurable Descend (Shift by default) also crouches on ground.
- **Still to verify in native Godot/WRY:** low-roof standing probe, diagonal edge support, fluid-to-ground state transitions, collision and camera blending in first/third person, and third-person crouch animation once the visual player model is added. Core tests cannot prove engine collision behavior.

## Player model port — 2026-10-09

- Imported the original MineClone GLB and 256/64 px skin atlases under
  `packs/default/resources/models/entities/player/` and
  `packs/default/resources/textures/player/`, alongside the pack-authored
  `data/entities/player.json` declaring its animation names.
- `PlayerVisualDefinition` validates asset paths and animation keys in Core;
  `PlayerSkinUvMapper` maps canonical cuboid faces into 64×64 skin UV
  coordinates independent of source texture resolution, with unit tests.
- Godot `PlayerModelPresentation` imports the **raw GLB** using
  `GLTFDocument`, maps the skin onto the imported cuboids and plays
  idle/walk/run/jump/fall from actual `FpsPlayer` state. The model is
  visible only in third-person mode and follows the same body transform.
  Crouch lowers original model pivots with a smooth transition.
- Character equipment remains unimplemented; do not display simulated
  equipped items. The character inventory panel still needs a real offscreen
  model portrait/render capture and UI interaction. The third-person held
  item model, player skin overlay layers and action/hurt/death animations
  also require separate native hookup and verification.
- Native Godot/WRY smoke tests remain necessary to verify GLB decoding,
  animation names, skin orientation, face UV seams and first/third-person
  visibility; build+Core tests alone do not verify GPU/render output.

## Player portrait — real offscreen rendering (2026-10-09)

- `PlayerVisualSceneFactory` is the shared pack GLB/skin importer for world and offscreen portrait; no duplicated UV rules or Godot import sidecars.
- `PlayerPortraitPresentation` uses a bounded 256×256 isolated `SubViewport` and captures PNG only when inventory opens or the user drags the character preview; not once per frame. The native bridge publishes a validated PNG data URI.
- React `CharacterInfoPanel` renders this actual 3D preview and forwards only focused pointer drag deltas via the inventory controller and semantic `ui.player.portrait.rotate` action. UI components own neither camera nor gameplay movement.
- No fake equipment state was added. Equippable slots, held-item visuals and gameplay animations beyond locomotion/crouch remain follow-up work.

## Equipment inventory integration — 2026-10-09

- Asteria extends the MineClone reference (which only renders four static empty armor rows): Core now owns four typed player equipment slots and transactions against the existing cursor, checked against optional `ItemDefinition.equipmentSlot` and `maxStackSize=1` instead of allowing arbitrary items. No defensive bonus is claimed without authored gameplay rules.
- The detached player inventory snapshot and complete on-disk session format v2 include equipment. The reader accepts v1 sessions as empty equipment and restores all slots atomically. Core tests cover equip, invalid slot/stack, no item loss and save/load roundtrip.
- Godot `ui.inventory.equipment` is the authoritative action, `game.inventory.state` publishes four slot snapshots; the React character panel renders real slots (including when empty) and forwards clicks only through the inventory controller. UI labels are localized.
- No default-pack equipment items have been authored yet. Equippable content, protection effects and equipment meshes must be designed explicitly before declaring the entire system feature-complete.

## Third-person held inventory selection — 2026-10-09

- Added a pure `HeldVisualResolver` in Core mapping the **real selected stack**
  to either a six-face textured cube for full authored cube blocks, or a
  transparent icon plane for items, tools, layers and non-cubic shapes.
  Metadata-specific item `iconVariants` are resolved from authored data,
  not from hard-coded item IDs; neither visual changes inventory authority.
- `HeldItemPresentation` attaches to the original GLB right-arm pivot and
  updates the selected appearance only when `PlayerInventory.Revision`
  changes, using selected-pack raw PNGs. No gameplay per-block nodes or WebUI
  mouse listeners are introduced.
- Third-person arm position follows GLB animation and crouch. First-person
  viewmodel, multi-layer block microgeometry, tool dye overlays, precise
  object GLB displays remain for a separate visual parity pass.

## Bucket held-item visual variants — 2026-10-09

- `ToolDefinition.iconVariants` now supports bounded, validated metadata selectors. The selected bucket uses `contained_fluid=asteria:water` or `asteria:lava` to choose the two original MineClone bucket icons, while empty buckets retain their original image. The same authored variants participate in creative catalog icons and third-person held sprites.
- Original bucket PNGs are in the default pack. No hard-coded bucket IDs or metadata behavior are added to the renderer; the existing `BucketGameplayRuntime` remains the mutation owner.
