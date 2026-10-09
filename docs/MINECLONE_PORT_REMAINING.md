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
| **World Selection: delete** | **Integrated in code, native QA pending (2026-10-09).** A local confirmation on `WorldSelectionPage` submits only a canonical world ID. Godot `WorldDeleteController` rejects active/loading/scanning sessions and runs a single worker; Core `WorldSaveDeletion` checks directory identity, manifest, symlinks, recursion depth and entry budget before deletion. The catalog is refreshed and failures reported. | Verify actual Godot/WRY confirmation, deleting a compatible and corrupted save, permissions, partial filesystem failure, repeated deletes and load/delete races. No WebUI paths or active-world removal. |
| **Pause: save and leave world** | **Integrated in code, native QA pending (2026-10-09).** `ui.world.leave` uses `DimensionSessionController.RequestSaveAndLeave`, reuses quiescent retirement and atomic session publication, skips Sphere reactivation **only on committed save**, clears Godot session presentation, restores WebUI pre-world state and refreshes the catalog. Save failure re-enters the archived Sphere and reports error. | Verify save → menu → reload, save failure recovery, repeated enter/leave, no orphan workers/cameras/sprites, focus restoration, and corrupted/locked save recovery in Godot/WRY. |

## P1 — player, inventory and visuals

| Feature | Current evidence / status | Required work and acceptance |
| --- | --- | --- |
| **Run and crouch** | **Implemented in code; native QA pending.** `PlayerGroundMovement` detects W ×2 within 12 ticks, with 1.5× run and 0.3× crouch; `FpsPlayer` applies real 1.5-block crouched capsule, camera drop, stand-up collision probe and ledge-support check. | Verify these transitions in Godot/WRY (low roof, edges, swimming, flight, third-person cameras). Player-model crouch animation belongs to the separate presentation task. |
| **Player model, equipment and portraits** | **Partially integrated; Godot/WRY QA pending.** Original GLB, skins, locomotion, crouch, real 3D portrait and four Core equipment slots exist. **2026-10-09:** Gameplay now triggers the authored one-shot `hit`, `break` and `place` animations; outer jacket/sleeve/trouser skins are reconstructed from alpha atlas regions for both model and portrait. `hurt`/`death` playback API exists but there is no authoritative player-health event wiring yet. | Add actual authored equippable items/effects, equipment meshes, authoritative hurt/death event delivery and native side-by-side visuals. Verify hair-layer alpha, UV seams and third-person action clip timing. No default-pack armor items are defined; do not claim full equipment parity. |
| **Sky and celestial textures** | **Partial.** `DimensionEnvironmentPresentation` creates sun/moon orbital meshes and time-based lighting, but the MineClone celestial PNG/material presentation is not verified. | Compare original sun/moon assets and day/night material transitions; load selected-pack textures without Godot sidecars; verify eclipse/orbit/tint/fog behavior only if genuinely authored. |
| **Stars and clouds** | **Partial.** Day/night sky presentation, wind and ambient particles exist; visual parity for stars and clouds remains unverified. | Compare MineClone sky assets and day/night behavior, avoid unbounded frame work and verify the sky presentation in Godot/WRY. |
| **Held-item dynamic light** | **Missing.** RGB voxel lighting and static authored emissions exist; no equipped-item moving-light contract confirmed. | Define equipment-origin light source with authoritative emission strength, bounded incremental propagation/retraction and stale-worker protection; no permanent voxel mutations when player moves. Validate crossing chunks and dimension changes. |
| **Creative inventory categories** | **Integrated in WebUI code; native QA pending (2026-10-09).** Authored `categories`, `order`, category `iconUrl` and `everythingIconUrl` are atomically validated by `creativeCatalogPayload` and stored in `UiStore`. React renders original icon/category order, pack-localized labels, localized content/metadata search and bounded per-category/search scroll restoration. | Run Storybook/browser checks once independent CI localization failures are resolved, then verify original PNGs, category interactions, metadata variants and scroll with the actual Godot/WRY client. |
| **HUD icons, metadata variants and stack names** | **Implemented in code, Godot/WRY QA pending (2026-10-09).** Asteria now publishes selected-pack block-face/sprite textures as validated catalog previews; the same catalog supplies target HUD, hotbar and inventory thumbnails. A shared UI formatting/search contract resolves localized metadata-sensitive stack names (e.g. bucket fluid or Dimensional Slicer destination), tooltips and inventory search. | Run browser stories and test actual Godot/WRY textures, special noncubic/multi-layer and dyed/tinted variants, fluid/object targets, 64×64 assets and metadata fallbacks against MineClone. The CSS cuboid preview is not yet a full native 3D voxel icon. |
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

1. **Native QA for World Delete and Save and Leave** (both now integrated in code): selection confirmation, worker isolation, process restart, failure feedback, and repeated world entry/exit.
2. Verify Creative inventory rendering/search/scroll in Godot/WRY; finish remaining inventory/HUD metadata, equipment presentation, portraits and general settings/UI details.
3. Verify native run/crouch and third-person presentation; add remaining player/equipment action visuals.
4. Port celestial assets, stars/clouds and held-item dynamic lighting.
5. Implement magic scroll-based physical portals using existing Sphere travel and destination capabilities.
6. Audit creature-specific effects/behaviors and close verified parity gaps.
7. Run side-by-side MineClone/Asteria Godot/WRY functional and visual QA, scoped to observed discrepancies.

**Do not count Storybook/CI green as in-game parity.** When closing a row, attach exact authored data paths, Core owner, Godot adapter, UI contract (if any), tests and a recorded Godot/WRY observation. No global WebUI gameplay input, engine resources inside packs, negative Y, or new duplicated world systems.

## World management actions — 2026-10-09

- World Delete: confirmation happens in React. Only a canonical save ID reaches the native controller, which refuses deletion while world, load or catalog work is active; the Core delete operation rejects missing manifests and filesystem links, checks bounded traversal and leaves other saves untouched. Its errors are surfaced and the catalog rescanned. See `WorldSaveDeletionTests` for filesystem and ID isolation cases.
- Save and Leave: successful atomic checkpoint publication retires the runtime without resurrecting the Sphere, releases sky/celestial/player presentation, resets navigation/authoritative session references and scans the saves catalog. Failed checkpoints reenter the saved in-memory session and retain the user in the game.
- Automated CI currently has unrelated blockers in biome worldgen (`VerticalBiomeContentTests.OceanRockTemplatesDisplaceOnlyOccupiedFluidVoxels`) and localization (`asteria:coral_blue`); this change is **not** claimed fully green or engine-playtested.

## Item and HUD preview parity — 2026-10-09

- The native Creative catalog publishes `blockPreview` for blocks from authored texture faces/sprite visuals (PNG data URIs, through the existing selected-pack resource path validator). WebUI validates the preview and routes it through `InventoryCatalogEntry`, `ItemGlyph`, target HUD and hotbar without inventing item identities.
- Cubic faces use a lightweight three-face pixel-art approximation; crossed/ground sprites use their authored PNG directly. This is not a complete parity substitute for MineClone's real 3D, layer-composited, tint-aware block icon shader.
- Shared `inventoryLabels.ts` now formats localized metadata-aware names (e.g. Bucket (Water), Dimensional Slicer (Umbral)) and handles accent-insensitive inventory/Creative search. Tooltips, ordinary inventory slots, hotbar names and the Creative panel consume the same routine; labels are presentation-only.
- Added focused label/catalog and Storybook/browser preview tests. Native Godot/WRY review remains mandatory; unrelated coral localization/worldgen failures currently prevent a fully green baseline.

## Creative inventory UI parity — 2026-10-09

- WebUI no longer infers/sorts category IDs from inventory choices; it consumes the Core/Godot-authored category order, icons and Everything icon as a single validated catalog snapshot.
- Category labels reuse the already ported `inventory_categories` translations. Search includes localized item names, authored IDs, metadata keys/values and their localized names (accent-insensitive). Search cannot fabricate new Creative identities; clicking still routes through the authoritative catalog choice.
- Category-list offset and up to 64 recent category/search grid offsets survive Creative/Inventory tab switches within a mounted inventory screen; state remains presentation-only.
- Added original-icon Storybook fixtures and focused bridge/browser tests. **Engine QA pending; CI can currently fail from independent coral localization/worldgen changes.**

## Movement port progress — 2026-10-09

- The Core `PlayerGroundMovement` now owns transient W double-tap detection (12 world ticks), 1.5× run multiplier, 0.3× crouch multiplier and crouch-over-run behavior, tested independently of Godot.
- Godot `FpsPlayer` applies native keyboard movement, accelerates/decelerates ground velocity, lowers the collision capsule from 1.8 to 1.5 blocks, blends the camera down by 0.35 blocks and checks full-height collision before allowing the player to stand. While crouching on ground, support probes prevent horizontal travel beyond ledges; immersed/flight movement retains its separate authored rules.
- Existing Asteria base movement speed remains unchanged (7.5) to avoid an unrelated balance regression; MineClone's reference walking speed is 5.0. The control guide now includes W ×2 Run, and configurable Descend (Shift by default) also crouches on ground.
- **Still to verify in native Godot/WRY:** low-roof standing probe, diagonal edge support, fluid-to-ground state transitions, collision and camera blending in first/third person, and third-person crouch animation once the visual player model is added. Core tests cannot prove engine collision behavior.

## Player action animations and outer skin layers — 2026-10-09

- `PlayerModelPresentation.TryPlayAction` now uses the selected pack's authored `hit`, `break`, `place`, `hurt` and `death` clip mappings. Real Godot interaction events trigger hit for creature attacks, break for targeted mining, and place for right-click actions; the model renderer owns only one-shot blending/timing and returns to the existing locomotion state.
- `PlayerSkinUvMapper.TryMapOuter` maps all six standard outer-skin UV regions. The shared `PlayerVisualSceneFactory` builds transparent alpha-scissored jacket/sleeve/trouser overlays for both the third-person model and inventory portrait. Existing GLB hair shell stays authoritative, avoiding a duplicated head overlay.
- **Not yet implemented:** actual armor models/items/content and game-authoritative health/hurt/death events; only the corresponding optional visual clips are accepted for future gameplay integration. Native Godot/WRY render review is still needed; no artificial equipment appearance or invented gameplay health was added.
- CI may still be red for unrelated concurrent biome worldgen/localization changes; inspect the exact job logs before classifying a failure as a player regression.

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
