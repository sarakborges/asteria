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
| **Physical magic portals between Spheres** | **Partial; unified identity 2026-10-11; native QA pending.** Closed Glass frames activated by the metadata-attuned Dimensional Slicer produce one `asteria:portal` centered Layer block, with `target_dimension` meta tags stored per voxel in authoritative sparse chunk state. Direction-specific blocks were removed. Exact-coordinate Sphere travel, safe rollback and Glass-break cleanup remain wired. | Pending: arrival structure/carve/connector to surface; real Godot/WRY rendering/input QA, and save/load plus return-trip testing. |
| **World Selection: delete** | **Integrated in code, native QA pending (2026-10-09).** A local confirmation on `WorldSelectionPage` submits only a canonical world ID. Godot `WorldDeleteController` rejects active/loading/scanning sessions and runs a single worker; Core `WorldSaveDeletion` checks directory identity, manifest, symlinks, recursion depth and entry budget before deletion. The catalog is refreshed and failures reported. | Verify actual Godot/WRY confirmation, deleting a compatible and corrupted save, permissions, partial filesystem failure, repeated deletes and load/delete races. No WebUI paths or active-world removal. |
| **Pause: save and leave world** | **Integrated in code, native QA pending (2026-10-09).** `ui.world.leave` uses `DimensionSessionController.RequestSaveAndLeave`, reuses quiescent retirement and atomic session publication, skips Sphere reactivation **only on committed save**, clears Godot session presentation, restores WebUI pre-world state and refreshes the catalog. Save failure re-enters the archived Sphere and reports error. | Verify save → menu → reload, save failure recovery, repeated enter/leave, no orphan workers/cameras/sprites, focus restoration, and corrupted/locked save recovery in Godot/WRY. |

## P1 — player, inventory and visuals

| Feature | Current evidence / status | Required work and acceptance |
| --- | --- | --- |
| **Run and crouch** | **Implemented in code; native QA pending.** `PlayerGroundMovement` detects W ×2 within 12 ticks, with 1.5× run and 0.3× crouch; `FpsPlayer` applies real 1.5-block crouched capsule, camera drop, stand-up collision probe and ledge-support check. | Verify these transitions in Godot/WRY (low roof, edges, swimming, flight, third-person cameras). Player-model crouch animation belongs to the separate presentation task. |
| **Player model, equipment and portraits** | **Core health, manual death/respawn, oxygen, creature attacks and durable Wayfarer armor integrated in code; native QA pending.** A death overlay now waits for `ui.player.respawn`; the Godot adapter validates that intent against a dead player before safe same-Sphere reentry. World gamerule `keepInventory` is authored by world creation/settings and saved in session v4. On death the Core retains belongings or atomically emits bounded full-stack world drops with original metadata, equipment, hotbar and cursor, without evicting existing drops. | Native Godot/WRY verification: death modal input/focus, respawn position/collision, item pickup at the death location and cross-Sphere save/reload. Specialized creature abilities and attack knockback remain pending. |
| **Sky and celestial textures** | **Integrated in code; native QA pending (2026-10-10).** The original MineClone sun/moon PNGs are stored under selected-pack resources and are rendered as transparent, camera-facing unshaded quads. Day/night definition texture paths are validated, with no ResourceLoader sidecars. | Verify Godot/WRY transparency, orbit, sprite tint, dimension switching and image framing; do not claim eclipse behavior unless authored. |
| **Stars and clouds** | **Integrated in code; native QA pending (2026-10-10).** Authored per-Sphere star/cloud density and color feed bounded Godot MultiMesh batches. Core deterministic sky layout and tile recycling follow the MineClone reference; night star fading and wind-drifted world-space clouds are implemented without a biome dependency. | Verify actual embedded Godot/WRY culling, alpha, camera travel, layout and day/night visuals against MineClone, including third-person and Sphere switching. |
| **Held-item dynamic light** | **Ported at rendering level; native QA pending (2026-10-11).** A Core resolver reads only the actual selected block's authored RGB emission. One bounded Godot light follows the camera and updates the six terrain shader variants through shared include/uniforms; changes are revision-gated and disabled on death, deselection, world leave and Sphere transitions. Lighting never writes voxel data, runs workers or remeshes terrain. | Compare GPU-lit faces and held objects against MineClone, including cross-chunk movement, third person and Sphere switching. **Limitation:** this GPU moving light does not voxel-raycast/occlude through walls; true occluded, transient RGB voxel propagation remains a separate upgrade, not claimed as complete. |
| **Creative inventory categories** | **Integrated in WebUI code; native QA pending (2026-10-09).** Authored `categories`, `order`, category `iconUrl` and `everythingIconUrl` are atomically validated by `creativeCatalogPayload` and stored in `UiStore`. React renders original icon/category order, pack-localized labels, localized content/metadata search and bounded per-category/search scroll restoration. | Run Storybook/browser checks once independent CI localization failures are resolved, then verify original PNGs, category interactions, metadata variants and scroll with the actual Godot/WRY client. |
| **HUD icons, metadata variants and stack names** | **Implemented in code, Godot/WRY QA pending (2026-10-09).** Asteria now publishes selected-pack block-face/sprite textures as validated catalog previews; the same catalog supplies target HUD, hotbar and inventory thumbnails. A shared UI formatting/search contract resolves localized metadata-sensitive stack names (e.g. bucket fluid or Dimensional Slicer destination), tooltips and inventory search. | Run browser stories and test actual Godot/WRY textures, special noncubic/multi-layer and dyed/tinted variants, fluid/object targets, 64×64 assets and metadata fallbacks against MineClone. The CSS cuboid preview is not yet a full native 3D voxel icon. |
| **Layers / portal layer** | **Partial.** Attached layers, dye, Brush, Shears, moss, rendering and persistence are implemented; the portal-specific visual layer and interaction are not. | Compare MineClone authored portal layer semantics and implement only as needed by Asteria's portal design; retain generic data-driven `AttachedLayerRegistry` and canonical voxel mutations. |
| **Creature effects / per-species behavior** | **Particles and authored phases integrated in code (2026-10-11); native Godot QA pending.** All 20 authored creatures match MineClone rebuild's phase keys; Asteria additionally has pursuit and contact damage. | Playtest per-species idle, anticipate, airborne, land, hurt and death particle bursts and combat feedback; verify GLB material output, alpha and frame-time behavior. Do not claim full in-engine parity from Core tests. |

## P2 — final UX and verification

| Feature | Current evidence / status | Required work and acceptance |
| --- | --- | --- |
| **Inventory, equipment, controls and additional HUD details** | **Advanced stack interactions integrated in code (2026-10-11); native QA pending.** Right-click split/place-one, Shift-click quick transfers between backpack, hotbar and Storage Box, and Creative Shift-fill now use the authoritative Core cursor/slots with semantic React/Godot actions. Search, glyph fidelity, equipment visuals, scrolling, and controller interaction still require verification. | Test left/right/Shift semantics, invalid/full/partial storage, search and equipment actions in embedded Godot/WRY; close observed visual gaps in [UI parity audit](UI_PARITY_AUDIT.md). Preserve Asteria's inventory-above-crafting layout and Winky Rough Variable. |
| **Create New World / general settings UI parity** | **Implemented foundation, unverified WRY.** Name/Seed in World Settings, Normal/Flat/Void mode, size multiplier and Game Rules are connected. | Compare MineClone `main` sidebar, navigation and non-biome controls, focus/keybind behavior, enabled/disabled states and runtime wiring in Godot/WRY. |
| **Save/Load hardening** | **Integrated in code.** Complete session serialization, recoverable generations, catalog validation and native Save/Load routing exist; checkpoint and native entry share the Core state. | Exercise save → exit → fresh process → load with restored player, containers, entities, chunks and active Sphere; corrupted generation rollback, pack mismatch, double-click and worker failure. Complete the delete/save-and-leave work above. |
| **Visual and behavioral parity pass** | **Unverified.** Godot builds, Core tests, React/Storybook and Chromium visual checks exist, but not a complete MineClone-vs-Asteria in-engine run. | Compare fixed viewports (1920×1080, 1280×720 and narrow), each screen/HUD/modal and interactive state, first/third-person input, controller focus, Escape, gamepad if supported, load/save/reentry, and actual content/asset rendering. Record screenshots and discrepancies. |

## Already implemented — do not restart from zero

- **Inventory Crafting:** `CraftingRecipeDefinition`, `InventoryCraftingRuntime`, Core transaction, Godot `ui.inventory.craft`, React `CraftingPanel` and feedback. WRY interaction QA remains open.
- **Save/Load + World Load:** `GameplaySessionFileCodec`, `SessionSaveStorage`, `WorldLoadController`, active-Sphere persistence, atomic generation recovery and catalog validation. The remaining work is lifecycle polish and live QA.
- **Storage Boxes:** 27-slot authoritative Core containers, native actions, `StorageBoxPage` mounted in `App.tsx`, drops and persistence; still verify play interactions.
- **Runtime foundation:** fluids, RGB lighting, streaming/meshing, day/night, wind and particles.
- **Gameplay basics:** mining progress and crack overlay, Artisan's Kit, Brush, Bucket, Shears, combat, creature spawning/loot, block loot tables and native chat commands.

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

## Oxygen HUD and portable equipment durability — 2026-10-09

- The native `game.hud.vitals` bridge publishes `oxygen` from the Core-owned `PlayerHazardRuntime.BreathSeconds` and `BreathDepleting`, not a browser timer. Oxygen appears only while a Survival player has the eye under a breath-depleting fluid; the bridge sends only changed whole-second values or visibility transitions. The existing `HudEntityCard` uses its second meter with an oxygen tone and localized name in English, Portuguese and Spanish.
- `ItemDefinition.maxDurability` is optional for unitary equipment, bounded to 1..65535; authored protective equipment must provide it. The default Wayfarer set has 160 helmet, 240 chest, 200 legs and 180 boots uses. Each accepted creature-contact hit wears the corresponding equipped protective items by one use; harmless proximity, invalid hits and fall/fluid damage do not consume durability.
- `PlayerInventory.WearEquipment` is the only mutator. The remaining uses are stored as `asteria:durability` in portable immutable `InventoryEntry.Metadata`, so they survive equipment swaps, cursor transfers, Sphere changes and session save/load without a second state store. Absence of this metadata means pristine equipment, including previously saved items. On the final use, the worn equipment disappears and no longer protects the player.
- The Core validates ranges and prevalidates all equipped pieces before a multiple-slot hit; the Godot integration refreshes authoritative inventory snapshots and the existing offscreen character portrait. The WebUI displays a wear bar and localized tooltip, filters the internal wear metadata from appearance/icon lookup, and keeps the original authored icon.
- Tests cover wear, breaking, invalid metadata, swapped stacks, snapshots, full session file restoration and negative authored capacities. Storybook documents drowning and damaged gear views.
- Godot/WRY interaction, appearance and collision verification remains pending; no local Godot runtime build was available in this session. Do not infer a successful native QA from the code review.

## Slime attacks, underwater exposure and equipment mitigation — 2026-10-09

- MineClone's entity health contracts were inspected; Asteria's player damage owner remains `PlayerHealth`, while `PlayerHazardRuntime` owns oxygen depletion, bounded exposure pulses and the per-player 0.8 s creature-contact hit interval. Godot reports its existing physical `FluidBodyContact` sample; it never simulates HP or builds a second environmental-fluid field.
- `CreatureDefinition.contactDamage` is optional, finite and non-negative. The `CreatureRuntime.ContactDamageAt` query uses the real creature collider against the player bounds, filters NO_AI/dying creatures, and chooses the first overlap in stable instance-ID order. All twenty default-pack slime definitions now author damage (2 HP for regular, 4 HP for large). Creatures with no authored contact damage are nonattacking.
- `FluidDefinition.contactDamagePerSecond` (0–100) and `depletesBreath` are selected-pack-authored. Water depletes 15 s of Core-owned breath while the eye is underwater and then causes 2 HP/s, resetting on surfacing. Default-pack lava causes 8 HP/s on immersion but does not consume breath. Creative/Spectator are immune to these hazards.
- `ItemDefinition.damageReduction` is optional (0–0.5), valid only for a unitary equippable item. `PlayerEquipmentProtection` reads the four authoritative equipped slots, rejects mismatched content and caps cumulative protection at 75%. The four Wayfarer items now author 8% helmet, 20% chest, 12% leggings and 5% boots resistance to creature-contact damage (45% full-set). Fall, drowning and lava are not reduced by this melee-defense stat. No armor durability was invented.
- Tests cover oxygen reset, drowning/lethal damage, lava exposure, contact cooldown, equipment mitigation, authored fluid and creature definitions, NO_AI/dead filtering, and mode immunity. End-to-end Godot/WRY playtesting and native timing remain pending; the unrelated worldgen/localization CI failures still block full validation.

### Death-screen recovery and restored saves — 2026-10-10

- The death modal now exposes both manual **Respawn** and the existing **Leave World** action (save-and-leave checkpoint), so a dead player can exit safely without bypassing the save/retirement lifecycle. The page keeps its modal and reports a localized save error if checkpointing fails; it resets only on authoritative `game.world.left` or `game.player.respawned`.
- A death freshly observed by the native runtime includes `outcomeKnown: true` with its authoritative item-drop outcome. When a saved dead player re-enters or the UI reconnects, `outcomeKnown: false` uses an explicitly neutral restored-session explanation instead of guessing whether the inventory was kept, dropped or protected by capacity fallback.
- Browser regression tests verify Escape/mouse-capture cannot dismiss death, save-and-leave message intent, save-error persistence, restored-death text, and explicit respawn completion. Core session save tests already exercise death-drop persistence and full-stack re-pickup.

## Manual death screen, inventory drops and respawn policy — 2026-10-09

- World rules now explicitly own `keepInventory`, configured in both New World → Game Rules and World Settings → Game Rules. Defaults to `true` for compatibility with previously created worlds; session file v4 persists the mutable rule, and v1–v3 readers safely default to `true`.
- A lethal event triggers the Core `PlayerDeathConsequences` capability. The keep rule leaves all stacks untouched; the drop rule gathers all backpack/hotbar/equipment/cursor stacks in deterministic slot order. `DroppedBlockRuntime.TrySpawnBatch` preflights its bounded entity capacity and preserves complete stack quantities, block states and portable metadata, including Wayfarer remaining durability. Only after successful registration does `PlayerInventory.ClearForDeath` clear possessions. A full drop budget falls back to inventory preservation, avoiding accidental item loss.
- The WebUI's localized `DeathScreenPage` is the exclusive modal. Native `game.player.death` includes the actual outcome. Escape/mouse-capture changes cannot dismiss death; the explicit `ui.player.respawn` request is the only entry point for the existing safe generated-spawn transition. The Core restores HP only after the player arrives, when Godot publishes `game.player.respawned`. Dead saved sessions show the modal on load instead of automatically respawning.
- Death, drop-capacity rollback, persisted portable drop stacks, v4 gamerules and UI intent/state are regression-tested. A restored death-drop session can be respawned and collected exactly once, and v3 saves retain health while defaulting the new gamerule safely. The Sphere snapshot validator permits valid full portable stacks (not only quantity 1). Dead players cannot collect nearby drops or mutate crafting, inventory or Storage Boxes through stale WebUI actions. End-to-end Godot/WRY input and visual comparison still require native validation.

## Authoritative player health and safe respawn — 2026-10-09

- MineClone `world-systems-rebuild/src/entity.rs` owns health as finite current/maximum values; `src/player/save.rs` persists current HP; `src/player/model.rs` drives one-shot hurt/death by observing health changes. Asteria ports these invariants with `PlayerHealth` owned by the shared `PlayerSessionState`, not a Godot or React mirror.
- Starting health is 20 HP. Damage, healing, death and respawn are validated Core capabilities. Creative and Spectator modes are immune to normal damage. `CanInteract` is false while dead.
- The native `FpsPlayer` reports the real descent distance when it lands (reset on flight or fluid immersion). Core applies integer-point fall damage beyond 3m, only in Survival. No fake hits, HUD vital timers or browser input ownership.
- Godot publishes `game.hud.vitals` from authoritative health revisions; existing React HUD renders it. A nonlethal change triggers the authored `hurt` clip, lethal damage the authored `death` clip. The dead player cannot interact or move. Only a user-confirmed respawn request initiates same-Sphere relocation to the generated safe spawn and the existing chunk-residency/loading lifecycle. Life is restored only after the new player is placed in the destination session.
- `GameplaySessionFileCodec` v4 persists player HP and world `keepInventory` within the same all-in-one session generation; v1/v2 readers default to full health. `PlayerSessionSnapshot` validates ranges (including legitimate 0 HP). Tests cover death, mode immunity, fall policy, save/restore and legacy decoding.
- **Pending:** Native Godot/WRY smoke tests for contact, drowning, armor wear and the new breath meter; specialized attacks, player damage sources and armor repair remain open. A complete green CI is blocked by pre-existing biome compilation and localization errors on the concurrently modified `main`.

## Authoritative equipment visuals and Wayfarer outfit — 2026-10-09

- `ItemDefinition.equipmentVisual.parts` is a validated optional pack-authored array of colored cuboids (up to eight), each bound to an allowed animated player mesh for that item's four body slots. It cannot define equipment for a non-equippable or stackable item, reference an unrelated body part, or supply unbounded geometry/color data.
- `PlayerEquipmentPresentation` reads only `PlayerInventory.EquipmentAt`, resolves appearance through `PackContentRegistry<ItemDefinition>`, and publishes/removes cuboids on the exact imported animated mesh parts. A revision gate avoids extra work. The same renderer is used by the third-person model and the offscreen inventory portrait, which refreshes on successful equipment transfer.
- The **Wayfarer** outfit is authored in `packs/default/data/items/wayfarer_{helmet,chestplate,leggings,boots}.json`, with four validated 64×64 original icon PNGs under `resources/textures/items/` and English, Brazilian Portuguese and Spanish names. These are actual Creative item choices and can be equipped with existing native inventory actions.
- These equipment models remain presentation-only: the inventory's protection and durability are owned by the Core, and the renderer never computes damage. The health lifecycle now exists independently and can trigger the authored `hurt`/`death` animations from real player damage.
- Godot/WRY QA remains open. Core/pack tests cover schema and slot restrictions; CI may be blocked by unrelated concurrent worldgen/localization changes.

## Player action animations and outer skin layers — 2026-10-09

- `PlayerModelPresentation.TryPlayAction` now uses the selected pack's authored `hit`, `break`, `place`, `hurt` and `death` clip mappings. Real Godot interaction events trigger hit for creature attacks, break for targeted mining, and place for right-click actions; the model renderer owns only one-shot blending/timing and returns to the existing locomotion state.
- `PlayerSkinUvMapper.TryMapOuter` maps all six standard outer-skin UV regions. The shared `PlayerVisualSceneFactory` builds transparent alpha-scissored jacket/sleeve/trouser overlays for both the third-person model and inventory portrait. Existing GLB hair shell stays authoritative, avoiding a duplicated head overlay.
- **Current status:** Wayfarer equipment models/items/icons are authored and wired to live slots and portrait. Player health, falling damage, hurt/death playback and safe generated-spawn respawn are implemented in code; native QA and other damage sources remain open.
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
- Four authoritative equipment slots, a shared model/portrait renderer,
  authored Wayfarer outfit and third-person held item visuals are now wired.
  Native clipping/action checks, creature portrait parity, first-person
  viewmodel, and authoritative player hurt/death events remain separate tasks.
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
- Four Wayfarer armor items are now authored in the default pack, with unique 64×64 icons and animated-body cuboid visuals. Protection/durability effects do not exist and must not be inferred from the appearance.

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


## First-person arm/viewmodel progress — 2026-10-11

- `PlayerFirstPersonViewModel` now mounts under the existing authoritative `FpsPlayer.Camera`, separately from third-person world presentation. It reuses the selected-pack player GLB, real `RightArmPivot`, shared skin UV remapping and generated sleeve overlay; other imported player meshes are hidden in the first-person scene.
- Selected blocks, items, tools, layers and metadata-dependent sprites reuse the same `HeldVisualResolver` and `HeldItemPresentation` as the third-person arm. Inventory revision changes rebuild only when the resolved appearance changes; there is no independent hotbar state or browser gameplay input.
- The viewmodel animates Hit, Break and Place using the MineClone reference's 17/22/19 world-tick windows and the 12-tick item switch wave. Actions are forwarded through the existing `PlayerModelPresentation.TryPlayAction` path; first-person visibility follows perspective, death, Spectator and mouse capture. Camera/session retirement owns cleanup.
- **Remaining:** inspect actual Godot perspective, orientation/skin and hand grip, near-camera/world collision occlusion, and exact layering of authored block textures/biome tint and Brush dye overlay. These are not established by a compilation or Core test. Keep first-person parity **Partial / native QA pending** until the visual pass and held-content variants are complete.



## Creature effects parity — 2026-10-11

- Compared `sarakborges/mineclone` `world-systems-rebuild` data for the entire set of **20 creature JSONs** against Asteria's default pack. All authored particle phase keys are present; the two non-elemental legacy/basic variants follow their source definitions. No material-tint field was found in the 20 source JSONs.
- Added bounded Core `CreatureParticleRuntime`, shared `CreatureVisualState` for the existing GLB animation and particle phases, and a single per-Sphere Godot `MultiMesh` publisher using per-particle RGBA and scales. Phases trigger bursts on entry and event-revision changes, idle/other periodic effects use authored intervals. Existing JSON count/color/speed/lifetime fields now validate their supported ranges.
- Presentation-only particles are not persisted or fed back to AI, damage, loot or collision. All particle and emitter state retires with the Sphere session. Core regression tests exercise the event revisions, bounds, deterministic order, JSON validation and default pack.
- **Still verify in Godot**: on-screen per-species colors, transparency, burst timing, replaying Hurt/Attack/Death and camera frustum culling, particularly in crowded scenes. This is code integration, not live visual parity.

## Chat interactions, localized links and input history — 2026-10-11

- Compared MineClone `main` and `world-systems-rebuild` chat module, autocomplete, command grammar and link rendering. Existing Asteria chat commands and Tab/↑/↓ argument autocomplete already covered the source's core command entry. No new commands or gameplay key handlers were invented in WebUI.
- `/locate` results now append a **Core-owned typed warp target** to their transcript entry. The React chat renders localized `[Warp to]`, submits only the live message ID, and Godot resolves the associated position/Sphere from the authoritative history before dispatching the existing validated `/warp` command. Unknown, expired and reset IDs are rejected.
- Architect's Compass exports now append an actionable transcript entry for their generated JSON. The Core accepts only a strictly bounded canonical `structure_<32hex>.json` name; clicking its localized link resolves a path **only** under the native `user://exports/structures` directory and opens that directory through Godot. The browser never submits file system paths or URIs.
- Added WebUI-only bounded last-32-submission input recall (↑ previous, ↓ next/draft restoration when suggestions are not active). When autocomplete is active, arrows still select suggestions and Tab completes the token. Escape first dismisses suggestions and then closes chat. These are focused-field interactions, not new global keyboard owners; the MineClone source does not itself persist a separate input recall list.
- Core and Playwright coverage checks action identity, stale invalidation, filename safety, submitted command recall, autocomplete/Escape, and both transcript link types.
- **Native Godot/WRY verification still required**: focus/keyboard repeat, clickable links while chat is open/temporarily visible, successful in-world teleport and OS folder launch (Windows/Linux/macOS), and post-transition history lifecycle. Existing tests do not prove those platform behaviors.

## Block and layer loot parity — 2026-10-11

- Core supports independently rolled, bounded `lootTable` entries on ordinary authored blocks; block/item/layer/tool rewards resolve through `InventoryContentCatalog` before world edits. The existing `dropsSelf` and layer drop contracts remain valid when no block loot table exists.
- All nine explicit MineClone `main` default `data/blocks` loot recipes were ported (stone→4 pebbles; grass block→dirt; four original leaf families→60% stick; dirt/gravel/mud→self). The eight authored metal ore layers already remove only their own layer and drop raw material; no additional ore block identity was created.
- Creative suppresses loot, Survival's current authoritative world tick seeds reproducible rolls, stack quantities obey authored limits, and capacity failures are rejected before a normal voxel/interactive layer is removed. Focused Core and pack tests cover these boundaries. Spike formations still use their bespoke segment-detachment loot handling.
- Native Godot/WRY pickup/mesh presentation, creative/survival switch and full drop-pool behavior remain to be observed manually. This is **code-level parity**, not in-engine QA.
- Sound effects are outside scope.

## Creative Pick Block and Placement Preview — 2026-10-11

- **Creative Pick Block integrated in code:** the native FpsPlayer middle-mouse action requests an authoritative target. Main gates it by game readiness, pointer capture, Creative mode and absence of modal input. Only InventoryContentCatalog-resolved block/items can replace the selected hotbar stack. Pickup-only decorative sprites select their actual pickup item instead of an unplaceable voxel identity. The Core inventory mutates exactly one hotbar slot without touching the cursor or other slots, and no-op picks do not bump inventory revision. The native hotbar, inventory and target HUD snapshots are republished.
- **Placement Preview integrated in code:** a single reusable Godot mesh displays the selected block at the location returned by the existing Core BlockInteractionResolver.ResolvePlacement. Invalid, occupied, unloaded, player-intersecting, unsupported, hidden-modal, dead, Spectator and transition states hide the preview. The presentation uses BlockStateMeshBuilder's canonical terrain geometry/encoded texture layers with the existing translucent terrain shader plus a preview-only opacity parameter (0.82); no alternate world mutator, voxel/block presentation nodes or UI input handler was added. Rebuilds happen only when the selected authored cell changes. Microblock-carved snapshots remain excluded because normal held-block placement already rejects their re-placement.
- **Controls guide:** native MMB appears as a Creative-only action, using existing EN/PT-BR/ES localization keys. Regression coverage checks creative hotbar replacement and no-op revision behavior; normal Core placement tests already guard the underlying placement eligibility decisions.
- **Still needs native Godot/WRY QA:** verify ghost geometry/alpha/tints at different biome positions, translucent materials, fine shapes, selected rotation, occlusion and live camera transitions; verify MMB priority with hit entities/decorations and behavior during opening/closing inventories. A green .NET/WebUI CI does not establish screenshot-level parity.

## World Thumbnails — 2026-10-11

- **Gameplay capture:** the native Godot world viewport is captured before the authoritative save transition retires the player camera. Its center is cropped to 16:9 and resized to 320×180 using Lanczos. External WRY overlays are not part of the captured viewport.
- **Durability boundary:** `thumbnail.png` is published atomically only after the session save generation commits; capture/write errors cannot veto a save, block exit, or corrupt the current/previous committed session. A failed save discards the pending preview.
- **Catalog:** after completed restore validation, compatible world cards receive optional previews from a bounded single-flight background PNG reader; one image travels per WebUI IPC message. Empty/invalid/oversized thumbnails are omitted. Catalog generations reject stale results.
- **Presentation:** WorldCatalogController validates incoming image data URIs and patches only the corresponding compatible world, while WorldCard already renders the optional thumbnail.
- **Tests:** Core storage verifies bounded 320×180 PNG headers, optional/missing previews, atomic replacement, malformed and oversized rejection. Live Godot/WRY QA is still needed to confirm actual viewport capture, screenshot framing, and visual output across platforms.

## Special Held Tool Viewmodels — 2026-10-11

- Brush and Artisan's Kit opt into the authored `heldPresentation: "grippedSprite"` visual mode. Core resolves gripped sprites with optional tint masks, while unrelated tools retain their standard presentation.
- First-person Godot anchors the tool handle using MineClone's (-0.36, -0.36) normalized grip, 0.52 sprite size and 0.30-radian rotation. Brush tint uses a depth-offset blended mask over the alpha-tested base and remains invisible without a selected dye.
- The current Sphere's authoritative Brush palette resolves to a read-only color; the held presentation changes material only when the tint changes, keeping existing player hand/arm and item-switch animations.
- Regression tests verify authored gripped sprites, the Brush mask, non-gripped fallback and invalid display settings. Live Godot/WRY QA of grip alignment, tint blend, hand animations and both perspectives remains pending.

## Objects architecture audit and Core occupancy foundation — 2026-10-11

Reference: MineClone `world-systems-rebuild`, `src/content/object.rs`, `src/voxel/object.rs`, `src/voxel/chunk.rs`, `src/voxel/world.rs`, `src/world_objects.rs` and `src/world_objects/batch.rs`.

**Verified divergence:** MineClone has `ObjectRegistry` authored independently from blocks/layers, sparse `ObjectCell` lists on supporting block coordinates (maximum eight per support), individual placement face, rotation, quantized offset/scale, separate object revisions, batched object materialization and object targeting/loot. Asteria has numerous separate *object textures* but their decorations are authored mostly as noncollidable/sprite blocks; their occupancy, targeting and save representation are block-based. Asteria's attached texture layers are a different, correctly separate feature and must not be repurposed as Objects.

**Implemented in this incremental Core phase:**

- `ObjectCell` and `ObjectTransform`: immutable namespaced identity, face, orientation and fixed 1/1024 offset/scale. Both reject invalid finite bounds; default transform is identity.
- `Chunk`: sparse object groups keyed by supporting block voxel, cap eight, deterministic visits, independent `ObjectRevision`, detach on support destruction/replacement, deep-copy on worker clone; block/fluid palette occupancy remains unchanged.
- `VoxelWorld` + `VoxelMutationRuntime`: support-validated add/remove and snapshot queries, authoritative dirty-chunk/world revision, dedicated object-change notifications. Object-only edits do not spuriously enqueue block/fluid mesh work.
- `ChunkSaveCodec` format v3: deterministic, bounded, checksummed group serialization with strict support/duplicate/transform validation. v1/v2 chunks remain readable; restore goes through the same isolated decode contract. Regression tests cover occupancy, cap, clone, detachment, dirty tracking, serialization ordering, old v2 decode, invalid transforms and checksum recomputation.

**Not yet ported:** authored `data/objects` definitions and loader; inventory/tool and placement face policy; rendering + instanced batched object meshing; mouse target/hitbox, selection & loot; worldgen decorators emitting ObjectCells; migration of existing decorative *blocks* (start with kelp/seagrass, cattail, lily pad, grass, flowers, mushrooms) to Objects. Do **not** migrate block JSONs until every path (generation, save, interaction, collision, tint and underwater cases) has a tested corresponding Object implementation. In-engine Godot/WRY QA remains required.


## Object definitions, batched rendering and Core targeting — 2026-10-12

- **Authored registry:** Objects load from optional `packs/{selected}/data/objects/` through `ObjectContentLoader` and a sorted, unique-ID `PackContentRegistry<ObjectDefinition>`. Each definition validates namespaced identity, resource paths, crossed/extruded-sprite geometry, placement faces, independent target bounds, jitter, and display/drop flags. Default-pack reference definitions for grass and red mushroom are present, without changing existing block definitions or worldgen.
- **Core spatial model:** `WorldObjectGeometry` computes one canonical support-face pose, authored 90-degree rotation, quantized instance transform and deterministic jitter; target intersection derives from the same pose. `WorldObjectRaycaster` searches only a derived resident-object-chunk index and tests actual oriented per-object hitboxes, not entire voxel boxes.
- **Rendering:** `WorldObjectVisualCatalog` constructs cached meshes/materials by authored object definition using selected-pack resource loading; `ChunkPresentation` renders all same-ID objects in a chunk using one Godot MultiMesh, with no nodes per individual Object. Updates are driven by `ObjectRevision` and a bounded deduplicated chunk queue in `ChunkPresentationController`; detach/removal/residency retirement invalidates the matching batch. Only 4 object chunks are integrated per frame, respecting the existing frame-work budget.
- **Still pending:** connecting the independent Object hit to gameplay target selection, Object inventory/placement/break/drop policy, worldgen emitting ObjectCells and migrating decorative *blocks* such as kelp and seagrass; no migration before these gameplay paths can preserve fluid source, support rules, drops and saves. Only crossedSprite is currently accepted; extrudedSprite is explicitly rejected until actual alpha-silhouette extrusion exists, rather than rendering a visually incorrect planar substitute. Visual Godot/WRY QA and screenshots are pending.

## Object interaction, inventory and HUD — 2026-10-12

- **Portable identity:** Object now has a distinct `InventoryEntryKind.Object` (appended after the prior enum values for old-save readability), canonical `FromObject` stacks and portable item/drop serialization. Objects appear in Creative by authored category and reuse the shared inventory/drop icon catalog and held-sprite viewmodel; the WebUI inventory decoder accepts the new kind.
- **Authoritative gameplay:** `WorldObjectInteractionRuntime` validates loaded supporting block, authored face, empty neighboring block and unchanged fluid source before adding an Object through `VoxelMutationRuntime`. Survival consumes one Object stack only after placement succeeds. RMB pickup checks inventory capacity before removing a `pickup` Object; LMB break respects `dropSelf`, survival/creative rules and active detached-drop capacity. Break drops use the existing physics, pickup and save ownership. Duplicate edits are rejected.
- **Selection + HUD:** Core's oriented Object raycast is integrated into Main's target path with a blocker check against the existing block raycast (never select through closer blocks), preserving creature precedence. Mouse-break and held survival mining do not simultaneously target the support block underneath an Object; right-click intercepts an Object before block/tool handling; Creative middle-click picks an Object stack. The HUD displays the correct Object ID, icon, face/coordinates, and localized action prompt. Object names and new prompts exist in EN, PT-BR and ES and localization validation now includes `data/objects` and resources.
- **Regression tests:** Object placement and unloaded/bad support, water-source preservation, duplicate rejection, break/drop policies, pickup and portable stack identity.

**Remaining:** Mining support-block destruction currently detaches its Objects without spawning their `dropSelf` loot: support-removal/drop consequences require an atomic extraction event in the mutation lifecycle. Also pending: object-specific loot tables, generation emitting ObjectCells, adapting decorative block usage (kelp/seagrass etc.), validation of real Godot/WRY targeting/rendering and animation, and true extruded/cuboid/model visuals.
- **Follow-up hardening:** the block/Object foreground occlusion decision moved from Godot `Main` into Core `WorldObjectTargetResolver` so gameplay raycasting follows the single authoritative geometry owner; tests verify nearer opaque blocks win. Pickup capacity regression now includes a completely full 36-slot player inventory and verifies no world/inventory mutation on rejection.

## Attached Object drops on support mutation — 2026-10-12

MineClone `world-systems-rebuild` reference: `world_objects.rs::detached_object_drop_request` distinguishes removing an Object directly from detaching one when its backing voxel disappears; `drop_self` and the same world-position calculation apply.

- Introduced `ObjectDetachmentRuntime` as a narrow Core consequence owner for support replacement/clearing: it resolves authored `dropSelf`, creates deterministic object-typed drop stacks and positions from the shared Object pose, and validates remaining physical drop capacity before allowing the mutation.
- A single `VoxelMutationRuntime.ConfigureObjectDetachment` binding per Sphere makes `SetCellAt` and `SetBlockStateAt` prepare consequences **before** the voxel is changed and publish them only after the authoritative change succeeds. Default procedural/tool/physics mutations drop Objects when their supporting block changes; explicit Creative block break suppresses drops while still detaching Objects. Block-state/rotation-only changes leave attached Objects untouched.
- The `BlockInteractionRuntime` and `SpikeFormationRuntime` compound removal preflights now include Object drops in addition to block and dependent spike drops. Structure batches preflight the sum of all attached Object drops before starting any edits. A saturated dropped-item pool must reject the change rather than silently delete objects or evict an existing physical item.
- Added Core regression tests for normal detachment, same-block replacement, explicit Creative suppression, capacity rejection/unchanged world, atomic structure batch preflight, and combined block + Object preflight.

**Remaining:** Object authoring/migration in world generation, beginning with kelp and seagrass, including explicit underwater placement/support/loot behavior; natural support changes to neighboring Objects beyond the changed support voxel, true extruded Object geometry, and live Godot/WRY visual verification. This change does not modify existing biome/worldgen definitions.

## First authored worldgen Objects: kelp and seagrass — 2026-10-12

- **Data-driven decorator identity:** `decorations[].object` joins the existing exclusive `block`/`layer` contract. `BiomeDecorationDefinition`, JSON parsing, `BiomeRegistry.ValidateObjects` and `SurfaceDecorationField` share the original chance, habitat, cluster, placement and fluid-condition selection. There is no second decoration randomizer. Only floor-attached Objects are currently accepted; unsupported Object faces fail content validation.
- **Generator:** `BiomeWorldGenerator` takes the pack's optional Object registry, and `DimensionRuntimeSession` supplies the loaded registry. `SurfaceChunkMaterializer` accumulates Object decisions for the support chunk and attaches them after the last terrain, fluid, structure and concealed-shell writer. The visible plant voxel stays air with its authored source fluid; an Object never substitutes the source. Vertical support-chunk boundaries are handled even when the plant voxel lies in the next chunk.
- **Authored migration:** `packs/default/data/biomes/ocean.json` now uses `object` for kelp and seagrass. Their crossed sprites became `data/objects/kelp.json` and `seagrass.json` with per-Object hitboxes, jitter, placement face, wind flag and Object drops; names are localized in all three supported languages. The former kelp and seagrass block definitions have been deleted; only the authored Object definitions remain. Creative exposes each plant as an Object, without a block counterpart.
- **Tests:** ocean decoration selection and content validation now cover Object rules. Procedural chunk tests cover seabed support ownership, determinism, generated source-fluid co-occupancy, dry rejection and vertical chunk seams.

**Still pending:** running the Godot client for visual QA; extending the data-driven Object decorator path beyond surface/floor to cave walls and ceilings, fluid-dependent reconciliation when an existing Object loses an adjacent water source, and validating the Object-only pack contract in the client (without old-world compatibility). Kelp here is a single anchored plant Object; authored multi-block kelp stalk/frond generation remains separate from this conversion.


### Object-only content cleanup — 2026-10-12

- The pre-release project does not preserve old content implementations or old save identities. Deleted `data/blocks/kelp.json` and `seagrass.json` after the worldgen/interaction path switched to `data/objects`; no compatibility loader, remapping or duplicate Creative identities are maintained for these plants.
- Removed their `blocks` localization entries (Object entries remain). The existing `kelp_stem`/`kelp_frond` blocks are distinct solid geometry used by the authored kelp grove Structures, **not** deprecated aliases of the Kelp Object.
- Converted ocean pack-content tests to assert Object-only identity, and updated fluid coexistence regression coverage to use actual Object occupancy rather than a seagrass voxel-block fixture.

## Plains wildflowers as Objects — 2026-10-12

- Converted Blue and Yellow Wildflower from voxel blocks to floor-attached Objects in the Plains biome using the existing data-driven `decorations[].object` path. Both retain their authored crossed-sprite texture, dimensions, wind sway, no self-drop, and EN/PT-BR/ES localization.
- Deleted the two superseded `data/blocks` definitions and block-name entries. No compatibility aliases or old-save migration.
- Mixed block/Object decoration selection now filters by requested kind, so a successful unrelated block rule cannot suppress an Object decision, nor an Object rule suppress a block decision. The materializer continues to require empty target space after generation.
- Added default-pack assertions for object-only identities and focused regressions for mixed selectors in both deterministic rule orders.
- **Next:** migrate other decorations after checking structure and vertical-biome references. Pink/Blue/Red Enchanted Forest mushrooms are still used as voxel payloads inside authored Structures; removing their block definitions breaks StructureRegistry validation. Generic Structure-owned Object payloads must be designed before migrating these mushroom identities. Native Godot/WRY visual QA also remains pending.

## Structure-authored Objects — 2026-10-12

- Structures now accept `{"object": "asteria:id", "face": "top"}` as a palette payload at the Object's adjacent occupancy cell. The supporting block remains the sole authority for Object storage, including rotations, chunk boundaries and Structure expansion/conflicts.
- Core validates bounded Object payloads and their registered identities/rotated faces. The world generator materializes after structures, caves and Sphere Shell; canonical manual Structure batches preflight all Objects together with voxel mutations, refusing unsupported, duplicate, occupied or over-capacity insertions atomically.
- Enchanted Forest surface mushrooms (red/blue/pink) and their eight fungal canopy/ring/stump Structure templates now use Objects. Their old block definitions and block localization were removed, while Object localization and sprite models are authored.
- Object visual validation in the actual Godot renderer and world load/save is still manual QA; cave-volume mushroom conversion, non-top supports and additional Object kinds remain separate audit items.

## Cave and additive-volume Objects — 2026-10-12

- `SurfaceDecorationField` resolves authored Objects separately for floor, ceiling, and wall supports, with validation of required faces. The materializer stages Object placement against the supporting solid block and publishes it after cave/Structure/Sphere Shell finalization; ceiling/floor candidates include adjacent vertical chunk seams.
- Ported `mushroom_brown`, `mushroom_purple`, `hanging_roots`, `aether_vines`, `wall_shelf_fungus`, and `cloud_reed` to attached Objects. Migrated Caverns, Floating Islands, Enchanted Forest, Swamp and Wraith Grove, plus seven Enchanted Forest and ten oak/stump/swamp Structure palettes, including EN/PT-BR/ES object localization. No duplicate voxel identities or save aliases.
- The authored luminescent `glowcap`, `glow_fern`, and `aether_bloom` deliberately remain blocks until the Object definition/render-light contract supports equivalent light emission; their current lighting may not regress.
- Native Godot rendering/targeting across chunk seams, wall-face orientation and world-save round trips still need in-game validation.
 
## Emissive attached Objects — 2026-10-12

- Object definitions accept the same validated RGB `lightEmission` values (0..15/channel) as blocks.
- A sparse, immutable lighting view derives source voxels from support-owned Objects and their attachment face. Initial chunk lighting uses the same view; loaded-world reconciliation seeds sources crossing chunk boundaries. RGB propagates in the adjacent target medium, including loaded chunk seams, through the authoritative lighting solver; it does not illuminate from inside the opaque support.
- Object placement/removal through canonical mutations enqueue a lighting update for the affected target voxel. Worker snapshots include Object chunk revisions and reject stale publication through existing content stamps.
- Converted glowcap, glow_fern and aether_bloom from blocks to Objects, preserving authored RGB emission and biomes; moved EN/PT-BR/ES localization and removed duplicate block identities.
- QA: automated emission/validation/removal tests. Real Godot visual verification and performance profiling remain outstanding.

## ExtrudedSprite Objects — 2026-10-12

- Added portable `extrudedSprite` authoring with independent X/Z size, bounded vertical extrusion thickness, base offset, alpha cutoff, and a target AABB that includes the actual sprite geometry by default.
- Core `ExtrudedSpriteMesh` generates the textured top and bottom and deterministic alpha-contour side faces from a bounded RGBA alpha plane; internal opaque-neighbor edges do not create hidden walls. The Godot adapter decodes selected-pack textures and publishes one cached mesh per Object definition, reused by existing MultiMesh instance batches.
- Canonical `WorldObjectGeometry` pose (attachment face, yaw, scale, stable offset) remains shared by rendering, raycast hitbox, and an Object placement ghost. Object placement preview and interaction share the same Core placement validation.
- Migrated seashell and starfish from Ocean and clover from Plains, preserving their chances, submerged source requirements and original image textures. Removed redundant block identities and transferred EN/PT-BR/ES localization. Pebble's item-pickup semantics and Lily Pad's fluid-top support are separate migrations, not silently changed here.
- Native Godot visual inspection remains required for contour normals and preview opacity; code and headless tests validate geometry and authored-content contracts.

## Pebble item pickup and Lily Pad fluid-support Objects — 2026-10-12

- Pebble is now an `extrudedSprite` Object for procedural ground placement, while pickup and support-removal rewards retain the existing `asteria:pebble` **item** inventory identity; it is not a second placeable Object stack.
- Lily Pad is now an `extrudedSprite` Object attached to a **full source-fluid support voxel**, with no block or water-source replacement. Worldgen resolves the authored `fluidRequirement: below` Object at the water surface and publishes only after fluids are finalized. Runtime placement uses an opt-in occlusion-aware fluid-top ray and the existing preview/gameplay Object placement decision; unrelated raycasts are unchanged.
- Fluid-support ownership remains in `Chunk`: when its source disappears or ceases to be a full source, attached Objects are detached and the authoritative world publishes Object revision/visibility invalidation. The pack validates that the Object's authored substrate matches the biome's fluid rule.
- Removed redundant pebble/lily-pad blocks and transferred EN/PT-BR/ES localizations. Fluid simulation, world-save and renderer in-game acceptance still require native Godot QA.

## Remaining decorative blocks to Objects audit — 2026-10-12

- Ported **stick** (flat `extrudedSprite`, `pickupItem=asteria:stick` retaining its existing item kind), **mushroom_green**, **mushroom_yellow**, **daisy**, **purple_aster**, and **iris** (crossed sprites) to Objects. Maintained their authored visual geometry, support face, chance, biome habitat/near-water rules, pickup and drops. Removed the redundant block definitions; moved all three localization dictionaries.
- Straightforward next conversions, subject to authoritative Structure/biome reference audits and in-engine QA: **tall_grass**, **marsh_orchid**, **bulrush**, **cattail**, **pass_grass**, **violet_fern**, **meadow_seedheads**, **sagebrush**, and other small crossed-sprite decorative plants. These should be migrated in coherent verified batches, not by indiscriminately converting all non-cubic blocks.
- **grass** has an authored foliage tint and a tintable texture. The existing Object crossed-sprite visual catalog does not reproduce this tint, and `grass.json` is already present in the Object catalog; unify those identities only after implementing equivalent pack tinting in the renderer, otherwise flora changes appearance.
- **sea_anemone** is deliberately deferred pending a fluid-substrate/lifecycle audit; lily pads already exercise full source-water support while submerged ocean decorations have distinct fluid rules.
- **Saplings** and vegetation with growth/interaction lifecycle require species-specific mutation/lifecycle parity, not just geometry. **Coral**, attached face textures, cliffs, and layered overlays require checking whether they should remain true voxel shapes/layers rather than be blindly migrated to Objects.
- Generated and placed Object face attachment, manual targeting, item rewards, world-save round-trip and authored tint still require in-engine acceptance testing; passing headless tests does not prove visual parity. No old-save compatibility is intended.

## Swamp reed and tall grass Object migration — 2026-10-12

- Converted `tall_grass`, `marsh_orchid`, `bulrush`, and `cattail` to support-owned crossed-sprite Objects; original dimensions, alpha cutout, wind sway, no-self-drop, visual textures, and fixed authored biome probabilities/habitat constraints remain intact.
- Preserved marsh near-water requirements (orchid max 3 voxels, bulrush/cattail max 2) without letting dry-ground plants replace or occupy water cells.
- Migrated the three `swamp_reed_bed` Structure palettes to Objects, preserving their variants, Y offsets, restrictions, proximity to water, and density. No duplicate authored blocks or backward-compatibility aliases.
- Transferred EN/PT-BR/ES labels and added regression tests for content shapes, biome fluid rules, and all Structure variants. In-game visual acceptance remains pending.

## Biome-tinted Objects: Grass — 2026-10-12

- Reused canonical `BlockTintParser` and `BiomeTintField.SampleChunkGrid` for the authored `ObjectDefinition.tint` channel and `previewColor` fallback. No parallel tint generator or biome color ownership.
- World Object MultiMesh batches now carry per-instance RGB colors resolved at the attached support X/Z. Un-tinted Objects remain unmodified (no color stream); tinted sprites use the same single cached base mesh/material and a white vertex color, rather than per-instance materials or nodes.
- The placement preview uses the same active Sphere tint palette and a privately duplicated material, refreshed only when the selected Object or target column changes. No shared material mutation or per-frame biome resampling.
- Canonicalized `asteria:grass` to its already-authored crossed-sprite Object across all nine referenced surface/volume biomes, preserving four planes, size, wind sway, no self-drop, foliage tint and `6f9f50` fallback, with lighting enabled to match the original block. Removed duplicate voxel definition and EN/PT-BR/ES block-localization entries; retained Object localized names.
- Headless tests cover tint parsing and two divergent authored foliage palettes (Plains/Swamp), while CI covers core and Godot build. Native visual inspection still required for shader/device color and biome-boundary appearance.

## Remaining small-flora Objects — 2026-10-12

- Converted `pass_grass` (Mountain Belt), `violet_fern` (Enchanted Forest), `meadow_seedheads` (Plains) and `sagebrush` (Gorge) from crossed-sprite voxel blocks to canonical support-owned Objects.
- Kept one unchanged ID and source PNG per plant, original dimensions, four planes, no self-drop, wind sway, alpha cutoff, dry top support, biome ground whitelist, authored chances, clusters, elevation/slope limits and habitat weights. Generated decorations now use `object` rather than `block` in their original biome rules. Objects have matching hitboxes and deterministic position jitter.
- Removed their obsolete block definitions and moved localized names to the Object catalog in EN/PT-BR/ES. No old-save compatibility aliases.
- Updated biome-specific Core tests and added four-way object-identity/geometry/habitat validation. Native Godot/WRY texture, target, generation and save/restore QA remains pending.

## Sea anemone Object migration — 2026-10-12

- Converted `asteria:sea_anemone` from a crossed-sprite voxel block to the canonical top-attached Object. Kept the texture, 0.8×0.88 dimensions, four planes, fixed positioning, no wind sway, unlit=false, and `dropSelf=false`, with original localized names transferred in EN/PT-BR/ES and no duplicate block identity.
- Ocean `decorations` now selects `object` for the existing 0.05-chance, rocky-reef-weighted submerged rule without changing its material whitelist, cluster, height, or habitat data. Generation attaches the Object to the solid seafloor while the target voxel remains air with its source water; water is never displaced by a decorative block.
- Extended submerged/dry generation regressions for both seagrass and sea anemone, plus default-pack validation of Object-only identity, geometry and no self-drop. No migration compatibility aliases.
- Limitations: water-loss reconciliation for solid-supported submerged Objects (including kelp and seagrass) and water-only *manual* placement restrictions are not currently part of the generic Object lifecycle; follow up separately rather than introducing an anemone-specific second fluid owner. Actual Godot/WRY alpha, shadowing, hitbox and water rendering require native QA.

## Submerged Object lifecycle batch — 2026-10-12

- Added `requiredFluidAbove` to the canonical Object schema for kelp, seagrass, sea anemone, seashell and starfish. Unlike floating `supportFluid` Objects, these attach to solid terrain and require water in the target cell directly above their support.
- Placement and preview share authoritative target-water validation and preserve water source cells. Fluid identity changes, water removal and solid replacement preflight Object detachment/drops through the existing mutation owner, including across chunk seams and aggregate fluid-simulation batches. Receding water of the same identity retains submerged Objects.
- No new fluid owner, save aliases, or redundant biome data. Regression tests cover manual placement, dry/mismatched fluids, water-source preservation, valid/invalid lifecycle changes, cross-chunk detachment, and atomic rejection when the drop pool cannot hold detached rewards.
- Headless CI and native Godot acceptance remain pending.

## Sapling Object migration — 2026-10-12

- Migrated all 11 saplings — charwood, enchanted_tree, frost_wood, juniper, oak, rime_deadwood, skywood, thornwood, willow, windwood and world_tree — to support-attached Objects, preserving species assets, crossed-sprite geometry, top-face placement, wind, noncollision and drop-self semantics.
- Removed duplicate block definitions, transferred EN/PT-BR/ES localization keys to Object catalogs, and changed the existing regression test to exercise actual Object placement, survival break, replanting, detached drop and portable chunk save/load for every species.
- Automatic tree growth remains a separate missing capability; neither MineClone rebuild nor Asteria has a species-growth runtime, and not all species have authored mature tree Structures. Avoid generic fallback trees or unsaved transient timers.

## Species-accurate sapling growth batch — 2026-10-12

- All 11 sapling Objects now declare an authored `growthStructure` reference. Existing native tree-variant groups are reused for oak, willow, enchanted_tree, skywood and juniper. Six new purpose-built adult Structures cover charwood, frost_wood, rime_deadwood, thornwood, windwood and world_tree using their existing family log/leaf blocks and distinct silhouettes.
- `SaplingGrowthRuntime` owns deterministic world-tick-based checks while gameplay is active, using only resident Object chunks. One Object-bearing chunk and at most 16 candidates are processed per check; the check runs every 200 world ticks, with a 1/8 deterministic chance per eligible sapling. Root position, authored Structure variant, resident target voxels, water, other top-attached Objects and current player collision are preflighted before any edit. Existing ground is not replaced.
- Success consumes the attached sapling and places the mature Structure through the existing `VoxelMutationRuntime.ApplyStructureChanges` single preflight/commit path; no Object drop or second Structure publisher. World tick already survives Sphere retirement/save, and grown blocks persist in normal chunks, without redundant growth timers or save aliases.
- Added content and end-to-end Core regression tests; in-engine visual QA remains necessary.

## Player skin atlas parity batch — 2026-10-12

- Compared the MineClone \`world-systems-rebuild\` canonical \`src/player/skin_uv.rs\` layouts (all six faces of 7 named body cuboids and their 6 outer regions) to Asteria. The default MineClone and Asteria GLB files are identical and embed canonical base UV coordinates, but Godot previously trusted the imported mesh UV stream without validating or rebuilding it.
- Asteria's player scene factory now remaps **both base and outer skins** from each cuboid's mesh-local vertex position and normal through the shared \`PlayerSkinUvMapper\`, matching MineClone's skin face rectangles regardless of GLB's preauthored UV stream. HairLayer remains the one modeled hair shell rather than a duplicate overlay.
- Added atlas aspect/resolution validation, back-face culling to prevent alpha-scissored body interiors from rendering, and Core tests for six faces of every base and outer atlas region.
- Native Godot visual acceptance is still required on third-person, inventory portrait, first-person hand, animation poses and alpha rendering. Source atlas geometry is tested, but cannot prove all runtime import/platform rendering paths.

## Player hand and preview presentation parity batch — 2026-10-12

- Compared third-person held block/sprite positioning and first-person action framing to MineClone. Moved immutable camera/arm layout constants to engine-agnostic `PlayerHeldVisualLayout` and reused a single Godot adapter for Bevy-style XYZ Euler rotations.
- Fixed third-person hand rotation using explicit XYZ quaternions (previously Godot YXZ), and corrected generic sprites to stay upright while gripped tools use the authored 0.30 **radian** rotation, not 0.30π for every sprite. First-person positions and cube sizes still match MineClone.
- The inventory character portrait now attaches the **same HeldItemPresentation and HeldVisualResolver** to the authored right-arm pivot as third-person gameplay. Portrait snapshots update the held item and optional tint when inventory/hotbar selection changes; no polling or parallel inventory state.
- Third-person locomotion, head pitch, and crouch advance even while the physical mesh is hidden for first-person mode; switching camera perspective no longer resurrects an obsolete visual state. Hurt action preflight now runs before the first-person swing is triggered.
- Added Core regression coverage for first/third-person pose dimensions and sprite/tool rotation conventions. Native Godot camera, clipping and animated-hand acceptance remains necessary.

## Creature attack knockback — 2026-10-12

- The Core now supplies a finite, deterministic, horizontal impact direction and bounded vertical lift from a real creature landing or projectile impact, rather than relying on camera orientation or forcing the player position.
- Landing-hit events carry their actual attacker position. Projectile impacts reuse the collision point already returned by the existing swept projectile simulation; no new hit queue or creature snapshot lookup.
- The Godot character applies physical impulse velocity only for accepted Survival creature attacks, using the existing MoveAndSlide, ledge and terrain collision behavior. Invulnerable hits, Peaceful mode, and lethal hits never cause stray pushback.
- Creature projectiles now pass through the same authoritative PlayerHazardRuntime invulnerability and equipment wear path as melee impacts, rather than bypassing it and damaging health directly; accepted zero-net-damage armor hits can still shove the player.
- Added Core tests for world-space direction, projectile side, exact-overlap fallback, and non-finite validation. In-game knockback magnitude/collision/feel must still be verified in Godot.

## Attached Object held visual parity — 2026-10-12

- The current Asteria Object schema and all 48 default-pack Objects use crossed/extruded sprite geometry, not glTF/GLB models. No unsupported, speculative model importer was added. MineClone rebuild's held model-object route was inspected; its stable maximum-target-extent framing is shared in portable Core layout.
- Resolved Object inventory stacks to the authored Object visual, rather than only an icon sprite. Godot reuses `WorldObjectVisualCatalog.GetMesh` for the same crossed planes and alpha-contour extrusion seen in-world. Material and tint fallback remain selected-pack based; horizontal extrusions face the camera while held.
- Third-person, first-person, and inventory portrait share one per-player bounded Object mesh catalog and identical portable centering/scaling. The normal sprite/cube/tool paths remain unchanged.
- Core regression tests cover both authored Object visual kinds and non-uniform target bounds. Native Godot preview, camera framing, contour transparency and authored foliage coloring still need in-game acceptance.

## Object icon tint parity — 2026-10-12

- Asteria already composites authored Block texture layers and dyable tint in `InventoryBlockPreviewRenderer`. The remaining Object path bypassed this and published raw pack PNGs even when `ObjectDefinition.tint` was authored (notably grass).
- Reuse the same renderer to create bounded, cached, alpha-preserving tinted Object icon PNGs. Inventory/Creative/hotbar use the authored fallback color because they have no world location. The in-world Object target HUD samples the existing `Generator.Tints` field at the object's support X/Z and publishes the corresponding tinted thumbnail.
- Untinted Objects continue using the unmodified selected-pack icon; no extra identity, GameUI tint computation, global palette state, or per-Object node is introduced. Core RGB math has focused tests; actual Godot/WRY screenshots are still required for icon parity.

## Target and inventory preview block orientation — 2026-10-12

- The old `BlockPreviewForInventory` always published default **Top/Front/Right** textures regardless of an inventory block's saved orientation/facing. Target HUD also always consumed a catalog preview by Block ID, so a horizontally placed log or a rotated directional block displayed the wrong faces.
- Both inventory and target previews now use `BlockFaceTransform.SourceFaceForWorldFace` (the authoritative Core face mapping) to choose the texture source for each displayed face, respecting existing per-stack `VoxelCell` state. Microblock stacks remain sprite previews.
- The Core-owned target HUD snapshot carries the actual cell alongside the block and surface state, so changing orientation/facing invalidates the presentation revision. A target-specific `blockPreview` overrides the generic Creative catalog entry; WebUI validates it using the **same parser** as Creative instead of trusting arbitrary strings.
- The preview still uses the existing CSS three-face approximation and does not yet implement per-face UV quarter-turn rotation or voxel geometry. In-game WRY visual acceptance remains pending.

## Preview UV quarter-turn parity — 2026-10-12

- Finishes the target/inventory preview orientation work by applying **the existing `BlockUvRotation.ForWorldFace` policy**, including orientation compensation and authored `rotateTexture` flags on each source face. The PNG-facing presentation now reflects the same 0°, 90°, 180° and 270° texture sampling used by `ChunkMeshDataBuilder`.
- A pure Core RGBA image transform preserves alpha and swaps dimensions for nonsquare assets; Godot performs selected-pack image decoding/PNG encoding only. Rotated data URIs are cached by block ID, source face and quarter-turn. No new content format, terrain updates or browser shader state.
- Tests cover pixel order, alpha preservation, invalid sizes/rotation and composition with existing world mesh UV rotation. True voxel-mesh icon geometry, microblock cutout, attached layers and native WRY screenshot acceptance remain separate work.

## Attached layer texture parity in the target HUD — 2026-10-12

- The inventory, hotbar and oriented block previews were already present on current main and were not reimplemented. The missing path was the live target block's per-face `BlockSurfaceState.Layers`: labels identified them, but the preview used only host textures.
- A portable `BlockPreviewLayerPlan` reads the authoritative face/rotation/stack order from `AttachedLayerRegistry` and `BlockSurfaceState`. `PreviewLayerBlend` implements tested bounded source-over RGBA composition with tint and alpha cutoff.
- The existing Godot `InventoryBlockPreviewRenderer` now overlays each authored layer over its oriented host face using that planner. Biome tint comes from the active Sphere's existing generator tint samples. The overlay path is not cached so mutable world surfaces and biome context cannot become stale.
- A hit on a layered back, left or bottom face shows that actual textured face as a flat preview rather than a 3-face cube that hides the selected interactive layer. Otherwise the 3-face cube is retained.
- UI contracts are unchanged (`game.hud.target.blockPreview` already exists); ordinary inventory previews remain portable, untinted, unmodified host graphics. In-engine Godot/WRY acceptance is still required for alpha, color, rotation, and stacked-layer screenshots.

## Biome-tinted host block previews — 2026-10-12

- The live target preview already resolves actual oriented/UV-rotated host faces and composes the voxel's attached layers, but still tinted the **host** with its inventory fallback even when a biome palette authored a different color for that block. The world preview now samples the existing `Generator.Tints` at the target X/Z and applies the same resolved sRGB tint to every dyable host face, including faces without attached layers.
- `InventoryBlockPreviewRenderer.FaceDataUri` accepts an optional spatial tint and reuses the normal ordered host texture layers/UV rotation. Authored inventory/Creative/hotbar previews still use cached fallback colors; world-dependent host variants never enter that cache. Attached-layer overlays retain their own tint channel and cutoff semantics.
- `BlockPreviewColor.FromNormalized` centralizes finite normalized-to-byte conversion for both Object and Block world target previews, with regression tests for rounding, clamping and invalid values. Godot/WRY screenshot verification is still required.


## Shared Object loot-table parity — 2026-10-12

- Objects accept optional `lootTable` with shared bounded item/chance/quantity validation. Common `LootTable` and `LootTableRoller` serve Blocks and Objects; empty tables suppress `dropSelf`, and absent ones preserve existing Object behavior.
- Survival break, support destruction/replacement and submerged Object detachment use seeded deterministic rolls with catalog-authored stack sizes. Every physical reward is capacity-preflighted before mutation; Creative breaks suppress loot.
- Pack load rejects unknown Object loot IDs. Regression tests cover stack splitting, overrides, repeatability, empty tables, Creative and atomic capacity rejection. Native Godot/WRY acceptance is pending.

## CuboidSet Object geometry — 2026-10-12

- Added bounded `visual.type: "cuboidSet"` definitions (1–64 parts, finite from/to extents and UV rectangles) with the MineClone rebuild six-face primitive convention. Validated part geometry and automatically derived hitbox bounds are owned by Core.
- `ObjectCuboidMesh` provides deterministic triangle positions, UVs and normals; the existing selected-pack `WorldObjectVisualCatalog`, one-MultiMesh-per-ID world batches, placement preview, and shared first-/third-person/portrait held meshes use the same geometry without per-Object nodes or Godot pack import metadata.
- Corrected Object placement ghost material handling: lit `ShaderMaterial` and unlit `StandardMaterial3D` both accept location-dependent tint without mutating world material, and preview-only lighting no longer depends on unavailable MultiMesh instance custom data.
- Added Core tests for authored UVs, all faces, rotated/scaled hitbox enclosure, deterministic geometry and malformed/unbounded content. Native Godot visual acceptance remains open.
- Still missing separately: GLB-backed `visual.type: "model"` Objects, with native selected-pack loading and renderer integration. Do not report model visuals as ported from this cuboidSet work.

## Object GLB models — 2026-10-12

- Added the authored `visual.type: "model"` contract with safe selected-pack `visual.path` under `resources/` and required explicit `target` to keep target collision independent of engine meshes. Only bounded `.glb` files are permitted; sprite geometry must not be mixed into the Model variant.
- `WorldObjectModelMesh` decodes pack-owned GLB bytes with Godot's in-memory GLTF decoder (no Godot importer metadata). It bakes static child-node transforms into one multi-surface mesh with per-surface embedded GLB textures/material colors, enforces finite target extents and resource/mesh caps, and publishes through the existing per-definition/Chunk `MultiMesh` batches.
- Model surfaces share the same world voxel lighting, authored biome tint, object pose, hand/first-person/portrait rendering and placement ghost. `WorldObjectDisplayMaterial` gives preview/held meshes independent per-surface material clones so a GLB with distinct materials remains distinct without changing cached world materials.
- Core validation covers safe paths, the Model variant, explicit target policy and invalid mixed geometry. The native runtime GLB import/render path still requires in-game acceptance testing, including authored models with multiple materials and unusual transforms. Animated/skeletal GLB Objects are **not** claimed supported; the world renderer intentionally handles static models.

## Object shadow casting and receiving policies — 2026-10-12

- Compared against MineClone `world-systems-rebuild` (`src/content/object.rs`, `src/world_objects/batch.rs`): `castsShadow` and `receivesShadow` are independent authored booleans, both defaulting to true.
- Core `ObjectDefinition` validates both optional JSON fields, preserves their independence from `unlit`, and exposes immutable shadow policy. Dense default-pack grass opts out of casting to preserve the previous cheap presentation for high-frequency foliage.
- Godot world batches now set `MultiMeshInstance3D.CastShadow` from the authored caster policy. The shared `WorldObjectMaterials` adapter applies material-level reception policy to crossed/extruded/cuboid and imported GLB surfaces: selecting the `shadows_disabled` shader variant for lit non-receivers and `StandardMaterial3D.DisableReceiveShadows` for unlit materials. The two object shaders share one include body to prevent shader logic drift.
- Hand-held visual and placement preview retain `CastShadow = Off` intentionally: these are presentation-only display meshes, never shadow casters inside the authoritative world.
- Core tests cover defaults, every caster/receiver combination with both `unlit` states, malformed booleans, and the dense-grass authoring exception.
- **Updated 2026-10-13:** Shadow reception now uses a Godot solar shadow-mask NextPass over the unchanged voxel-lit Object color. The old unshaded receiver limitation is removed in source; GPU and actual WRY visuals still require acceptance.

## Isometric inventory previews from canonical terrain meshes — 2026-10-12

- MineClone `world-systems-rebuild` uses material-backed 3-face block icons in `src/hud/block_icon.rs`; Asteria already provided CSS 3-face cubes but collapsed noncubes and authored microblocks into flat sprites. That lost the true shape of microblock geometry, hollow blocks, layers, and spikes.
- `BlockStateMeshBuilder.BuildData` now returns the same Core `ChunkMeshDataBuilder` triangles consumed by the placement ghost and terrain rendering. The bounded `InventoryVoxelIconRasterizer` projects those triangles isometrically into a transparent 96x96 PNG, sampling the already decoded terrain texture-array pixels with correct base/overlay UV mapping, nearest sampling, cutout and authored fallback tint. No second mesh generator, per-slot Godot nodes or WebGL runtime.
- Creative catalog and live block stacks use the same preview source. The hotbar and Inventory message readers now preserve live stack-specific block previews (including orientation, attached layers and microblock mask) over the catalog fallback. Other item/tool/object PNG icons also propagate to the hotbar rather than showing abbreviated IDs.
- Dynamic world target previews use this geometry for noncube/microblock blocks, preserving the existing face-specific 2D treatment of occluded attached layers. Immutable icon variants are bounded/cached, and excessive polygon counts fall back to the existing face-preview path.
- Native in-game validation remains pending; browser screenshot tests cover the existing UI rendering, but cannot exercise the Godot mesh-to-PNG adapter. GPU-backed 3D model thumbnails are not part of this particular Block/microblock port.

## WebUI measured scrollbars and screen transition parity — 2026-10-13

- Compared MineClone `main` and `world-systems-rebuild` `src/ui/scrollbar.rs` and `src/ui/transition.rs`. Both use an 8px scrollbar track with a 28px minimum thumb, reveal only when computed content height exceeds viewport by 0.5px, and transition to black for 160ms and back for 160ms using an eased curve.
- Asteria already had `ScrollArea` and `ScreenTransition`. Extended the existing measured, browser-native scrolling adapter rather than adding a second scrollbar implementation. World selection and the Creative category/catalog lists now share it with Settings; Creative scroll-memory refs and browser-native wheel/keyboard scrolling remain attached to the same viewport.
- Refined the track/thumb theme to match the reference and preserved a native horizontal scrollbar for small-screen Settings navigation. Added a live-content Storybook story and browser tests for measured auto-hiding, keyboard/track scrolling and resize-triggered visibility.
- The existing React transition now uses a persistent CSS opacity veil to smoothly reverse an in-progress fade, coalesces rapid navigation requests while fading out and swaps only to the latest requested screen. An immediately reversed navigation gracefully fades back in; reduced-motion requests swap immediately. The controller/store remains authoritative over which screen is selected and only UI presentation uses the timers. Browser tests verify rapid A→B→C transitions and reduced-motion completion.
- The WRY/Godot integrated runtime still requires manual visual acceptance, especially scrollbar pointer capture and transition feel.

## Spawn biome choice audit — 2026-10-13

- Compared MineClone `main` Spawn Biome searchable dropdown and `world-systems-rebuild` new-world flow. Asteria already had `SpawnBiomeSelect`, Core `WorldGenerationOptions.SpawnBiome`, a validated `ui.world.create` request, selected-pack Sphere candidate filtering and generator-backed `BiomeWorldGenerator.FindGeneratedSpawn`; this was an incomplete parity audit, **not** a missing data system.
- Fixed the visible gap: the new-world page now resolves the existing localized `dimensions.json` `/name` for each biome in the user's active UI language and orders choices by the translated label/ID, instead of displaying Godot's English name derived from the identifier. Search normalizes accents (e.g. searching `pantano` finds `Pântano`). The authoritative biome ID still crosses the bridge unchanged.
- The native startup route searches up to 4096 blocks for an explicitly selected initial biome, versus 512 blocks for random selection. The existing bounded generated-world query remains the sole source of biome/destination decisions; the search does not materialize chunks, save anything, or use camera proximity.
- Tests verify selected-biome deterministic and safe target selection in Core and localized UI dropdown/Random/Single Biome selection in Storybook browser testing.
- If no safe site exists in the chosen biome inside the configured search bounds, the current safety-first destination resolver can still fall back to an alternate safe spawn. A hard guarantee for pathological authored worlds would require an explicit world-creation failure contract, not an unsafe teleport. Native Godot launch and broad-seed in-game verification remain pending.

## Native Godot presentation smoke and port regression fixes — 2026-10-13

- Previous validation checked .NET compilation and browser Storybook parity but did not open the authored project in the Godot runtime. `validate.yml` now pins the official Godot **4.7.2 .NET** Linux binary by SHA-256, performs a headless project import and executes `tests/godot/native_presentation_smoke.gd`. That smoke loads voxel/object shaders (including their shared include), verifies GLB-related shader parameters, round-trips a generated static GLB via Godot's `GLTFDocument`, and instantiates the authored main scene without starting a WebView.
- Fixed the Object placement ghost retaining stale per-surface material overrides when changing from one Object/GLB to another: all previous surface clones are cleared **before** setting the new mesh. This prevents the next Object from accidentally inheriting old embedded textures and tint parameters.
- Fixed lit GLB material color parity: the `model_albedo_color` shader uniform carries RGBA, not just RGB; the imported alpha now multiplies the texture alpha as it already does in unlit materials.
- Native smoke is intentionally bounded and does **not** claim to validate the windowed WRY plugin, cursor/focus handoff, GPU shadow appearance, actual frame presentation or a full world startup. Those still require a graphical Godot/WRY acceptance run. Missing native smoke functionality fails CI rather than silently passing the prior C#-only checks.

- First native CI run exposed previously invisible Godot import/runtime errors: Godot attempted to import a corrupt PNG kept only as documentation in `docs/texture-audit-2026-10-11`, and debug file logging targeted the non-writable/untracked `res://logs` path. The documentation tree now has a root `docs/.gdignore`; Godot debug logging writes to `user://logs/godot.log` instead. The smoke GDScript also uses an explicit `Node` annotation for PackedScene instantiation as required by Godot's typed parser.

## Focus/capture audit for Godot/WRY — 2026-10-13

- The gameplay player previously captured the OS cursor unconditionally on `_Ready` and after any `ResumeGameplay` request, including a late `ui.game.resume`, chat submit/close or inventory close processed while the OS window had lost focus. `FpsPlayer` now gates cursor capture on the player being in-tree **and** the Godot window owning OS focus. Both startup and explicit resume use that single entry point. Losing focus still releases held input through the existing `PauseGameplay` flow; returning focus does not auto-recapture without an explicit resume.
- `WebUiHost.set_mouse_captured` used to evaluate a script iterating over every DOM element on every capture toggle, writing inline `cursor` styles. The existing native `game.mouse_capture` bridge event already sets `html.mouse-captured`, whose global CSS hides descendant cursors with `!important`. Removed the redundant per-element browser mutation; native focus handoff logic and the single source of truth for mouse capture remain unchanged.
- Added a native smoke regression: constructing an out-of-tree FpsPlayer and invoking `ResumeGameplay` must **not** capture the OS cursor. The headless test is not a replacement for live WRY window-focus testing.

## Native chat focus restoration and modal-close pause handoff — 2026-10-13

- Corrected a focus-loss lifecycle hole left after preventing cursor recapture in an inactive OS window: if the player closed/submitted chat (or closed inventory, Storage Box or Brush) while Alt+Tabbed away, gameplay could remain mouse-uncaptured with no pause or modal visible. The Godot composition root now routes modal-closing resume through a single guarded method: the native player tries to recapture only if the window is focused; otherwise Main explicitly publishes `game.ui.pause` **after** the modal's closed state. The existing WebUI navigation controller accepts this only when no other modal, loading or death state is visible.
- Re-entering the OS window while chat remains open now retries `focus_chat_input` through the existing deferred WRY handoff. It does not reopen the chat or bypass Godot-owned key routing; it only synchronizes the mounted React input after an inactive-window period.
- Added controller-level regression coverage for closed-chat and closed-inventory focus-loss transitions, for preservation of an open modal during focus loss, and for avoiding an extra pause after a successful native resume.
- **Native WRY acceptance still required:** these tests exercise browser/transport contracts, not an actual Windows child WebView. Verify Alt+Tab during an open chat and modal, chat input autofocus on return, Escape closing pause without clicking, and no orphaned unlocked cursor.

- Additional Escape-state correction: `UiNavigationController.escape` now treats a gameplay world with no open overlay and an already-unlocked cursor as a request to open Pause. Before this, Godot's `game.ui.escape` (emitted intentionally only when the pointer was *already* uncaptured) was a no-op, leaving no route back into an interactive pause menu. Escape inside chat or before world creation cannot open gameplay pause, and Escape when already paused still sends only `ui.game.resume`. Controller tests assert all three boundaries.

## Physical portal arrival surface-connector audit — 2026-10-13

- The authored `portal_arrival_platform.json` (Glass frame and two return-portal voxels) and repeatable `surface_spiral_stair_segment.json` were already wired through `SurfaceStructureField.TryPrepareExactPlacement`, Core `ManualStructurePlacementRuntime.TryPlaceExact` and native `DimensionRuntimeSession`. The runtime must not silently shift the portal entry: it places at identical source X/Y/Z and rolls back a failed arrival.
- Found a genuine correctness issue in `SurfaceStructureField.HasReachedSurface`: it considered a connector complete when **the maximum height of any voxel in the entire piece** passed the terrain surface. A taller decorative block could therefore prematurely stop the connector chain even though the actual passage mouth was still underground.
- The surface stop criterion now checks the *connector's exact world X/Y/Z* and the authored `surfaceClearance` against the generated surface elevation at that connector. A staircase only terminates after its walkable exit reaches the required clearance; otherwise the existing bounded connector expansion continues. The resolution stays in Core and the normal authoritative Structure preflight/mutation transaction.
- Added regressions for unrelated tall voxels, an above-ground entry that needs no staircase, and a deep entry with all affected chunks resident that commits the portal and every authored stair through the canonical atomic mutation runtime. Existing tests still cover aborted/unloaded/protected returns and incomplete connectors.
- Remaining native WRY verification: real biomes with steep/heterogeneous terrain, multi-Sphere round trips, a saved arrival return portal and chunk streaming/presentation on arrival. Structure placement intentionally does not overwrite protected Sphere Shell or fabricate an unsafe entry.

## Celestial orbit parity with MineClone — 2026-10-13

- Audit found that Sun/Moon sprites, validated selected-pack sky textures and DayNight clock/phase data already existed in `DimensionEnvironmentPresentation` and `DayNightCycle`; no second celestial state or new asset loader was needed. The remaining disparity was in rendering the authored orbital direction.
- Previously Godot interpolated azimuth naively via `Lerp(rise,set)` and used positive Z for azimuth zero. The MineClone `world-systems-rebuild` reference (`src/rendering/celestial_path.rs`) takes the shortest azimuth turn across 0/360 degrees and uses **negative Z** as azimuth zero. Both facts matter for custom Spheres and the actual facing direction of the Sun/Moon; a time-only visual test cannot detect them.
- Added engine-agnostic `CelestialPath.Direction` in Core, deriving the normalized offset from authored phases and the existing `DayNightCycleDefinition.ProgressBetweenPhases`. Godot's environment adapter now consumes that result for both bodies and the native Sun directional light, retaining the existing pack art and Sphere presentation lifecycle.
- Tests cover rise, zenith, daytime/nighttime visibility, exact normalized world direction, 350-to-10-degree zero crossing, and restored clock tick parity. Native graphical sky and light-direction review remains pending; this port does not modify skylight simulation or the already ported held dynamic lighting.

## Object solar shadow reception — 2026-10-13

- MineClone rebuild's `NotShadowCaster`/`NotShadowReceiver` policies remain independent in Asteria. `castsShadow` still controls only the world batch's cast setting; `receivesShadow` now selects a cutout-aware second pass on both primitive and static GLB materials, independent of `unlit`.
- The base texture and voxel sky/RGB face lighting stay unchanged. The existing sun provides directional shadow maps and the environment owner updates one global render-only shadow strength from the authored Sphere clock; the overlay is suppressed at night, without a sun, and on world retirement. Held Objects, placement ghosts and inventory portraits strip this pass from their display-specific material clones. No Core lighting, remeshing, or pack format changes.
- Native headless smoke verifies the shader, uniforms, global and NextPass contract. **Pending graphical Godot/WRY QA:** shadow casters across terrain and foliage, no-sun and night Spheres, cast/receive combinations, multi-material GLB transparency, shadow acne and performance in dense foliage.

## Held dynamic light: native shadow occlusion — 2026-10-13

- Compared the previous transient camera-light presentation with MineClone rebuild and retained the single, selected-stack-owned emitter. The prior distance-only shader could brighten terrain across solid walls.
- The existing camera-held `OmniLight3D` now casts bounded native shadows. Six terrain variants preserve the same voxel RGB/sky/AO base as emission and add held RGB through a common Godot `light()` callback with GPU `ATTENUATION`, excluding the sun's directional contribution. Removed unused moving-light uniforms and obsolete shader code.
- No voxel light mutations, remeshing, workers, queues or save format changes. Godot/WRY GPU appearance, fine shadow-map bias, translucent boundaries and frame-time profiling remain to verify; GPU occlusion is not full transient voxel flood propagation.

## Portal frame/interior size parity — 2026-10-13

- Compared MineClone rebuild `src/targeting/portal_activation.rs`: Asteria previously rejected portal frames after 1,024 boundary voxels or 512 interior voxels; MineClone permits 4,096 of each. Asteria now matches both bounded limits while preserving its independent 64-block geometric span, selected-pack metadata target and atomic voxel mutation owner.
- Core regressions cover activation and full cleanup of a 3,364-cell cross-chunk portal, and rejection of a region larger than 4,096 with no partial changes. This deliberately does not change portal representation to standalone AttachedLayers; Asteria's current portal is a noncollidable centered-layer voxel with per-cell destination metadata.
- Godot/WRY visual navigation, return-trip, persistence and dense-portal frame-time QA are still necessary.

## Dyed voxel icon fidelity — 2026-10-13

- Found a real preview/runtime disparity: `InventoryVoxelIconRasterizer` reconstructed the canonical block mesh for inventory and HUD icons without passing the pack-owned `DyeRegistry`. A dyed block caused the mesher to reject it, and the bounded image cache keyed only on block/cell/microblock geometry, so different dyes could share a stale PNG.
- The existing icon rasterizer now passes the canonical registry through `BlockStateMeshBuilder.BuildData` into the Core mesh-data builder, uses the resolved vertex tint for dyed and authored-fallback geometry, and keys cached icons by dye ID as well as geometry. Target-only biome tint stays uncached and takes precedence over the fallback only when there is no explicit dye. Attached layers remain uncached and retain their prior mesh geometry and blend order.
- No new preview renderer, Godot node per block, content schema, or save format. Native Godot/WRY visual verification for dyed inventory stacks, dyed non-cube blocks, and picked targets remains required; the existing headless CI covers compilation, not actual PNG appearance.

## World Selection thumbnail integrity — 2026-10-13

- The existing selected-world gallery and native 320×180 screenshot capture were already integrated; no alternative thumbnail system or browser rendering path was needed. The actual gap was `WorldThumbnailStorage.IsValidPng`: its former 33-byte signature/IHDR check accepted nonexistent or truncated pixel streams as valid cards.
- The bounded Core validator now checks IHDR color/size/encoding, every chunk's CRC and structure, contiguous image-data chunks and terminal IEND, and safely decompresses only the required 320×180 RGB(A) scanlines (PNG filter bytes 0–4). It rejects malformed compressed payloads, missing data, extra pixels and trailing bytes, while allowing normal ancillary PNG metadata. Decoding validation runs on the existing background thumbnail worker, not during gameplay rendering.
- Tests now generate real zlib-compressed PNGs with checksums and cover replacement, RGB with metadata, truncation, invalid filters, bad CRC, missing IEND, extra bytes, dimensions and oversized images. A bad thumbnail remains optional and cannot invalidate the saved world; its path is not a new authoritative save channel.
- A graphical Godot/WRY check of screenshot composition and viewport timing is still needed.

## Controls: MineClone Chat shortcut group — 2026-10-13

- Compared MineClone `world-systems-rebuild/src/screens/controls_screen.rs` against the actual Asteria `buildControlGroups` and `ChatDock` keyboard handler. Movement, Interface and Actions controls existed, but the fourth authored **Chat** group was absent even though Enter submits, Up/Down recalls command history and Tab accepts autocomplete in the current React chat input.
- The controls presenter now renders the fourth localized Chat group with the existing rebindable Chat action and fixed Enter, Up/Down and Tab shortcuts. Change Perspective is shown with Interface as in the reference rather than Movement. The global Godot keybind/input owner is unchanged; these shortcuts work only in the focused chat field.
- Existing EN/PT-BR/ES catalog entries are reused without introducing duplicate translations. Storybook now includes the true production presenter with Chat intentionally rebound from T to Y; Chromium tests assert all four groups and the correct language-specific shortcut labels in three locales, including no duplicate stale Chat label under Interface.
- Visual verification in embedded Godot/WRY remains pending; browser controls and Storybook do not establish native focus ownership correctness.

## Pack texture references after unified texture art update — 2026-10-13

- A parallel texture art commit replaced/renamed pack images without migrating authored block JSON paths. CI `check-localizations.mjs` caught the first failure: the oak family was missing canonical `leaf.png` because the new art had been committed as `leaves.png`. Keep the replacement art bytes but use the shared tree-family `top/side/leaf/stripped/sapling` contract.
- Updated affected block definitions to reference existing shared textures for crystals, basalt, cactus, colored coral, bone, ice cores, mushrooms, legacy kelp geometry and volcanic glass. This restores the invariant that every pack-authored texture path is resolvable without adding redundant duplicate assets or suppressing validation.
- These mappings intentionally reuse consolidated art; formerly distinct specialized sprites (such as sulfur, glowing mushroom caps and volcanic glass) now share an existing generic base. Their final palette/material appearance requires real visual review before claiming exact artistic parity. This repair does not change any block identity, collision, drops, voxel save content, or runtime ownership.
- Existing WebUI localization/content path audit is the regression gate: it enumerates all block, layer and object texture references, validates the five tree roles and rejects missing files.

## Chat autocomplete localized names — 2026-10-13

- MineClone `world-systems-rebuild/src/hud/chat/autocomplete.rs` keeps canonical command tokens but displays the selected language's authored names as descriptions for creature, biome and dimension IDs. Asteria already offered matching IDs and numeric positions, but rendered a generic "Creature", "Biome" or "Sphere" category for every completion.
- `chatAutocomplete.ts` now uses the existing `LocalizationProvider.contentName` callback for `/spawn` creature, `/locate biome` and `/warp` Sphere suggestions. This is presentation only: IDs, matching rules, token replacement, command grammar, permission checks, Godot routing and content packs remain authoritative/unchanged.
- The Storybook ChatDock fixture uses real authored biome IDs; Chromium tests check the unchanged completion token plus its correctly translated description in English, Brazilian Portuguese and Spanish. Structure completions still use the generic descriptor until Asteria authors structure-name localization; no duplicate translation storage or speculative ID mapping was added.
- Native WRY chat focus, keyboard capture and real gameplay selection still require in-engine QA.

## Locate completion eligibility — 2026-10-13

- Verified against MineClone `world-systems-rebuild/src/hud/chat/autocomplete.rs`: `/place structure` may suggest all authored structure references, but `/locate structure` must not advertise references whose authoritative `locatable` flag is false. Previously Asteria exposed the identical unfiltered list in both commands, leading to immediate native command rejection.
- `ChatStructureCompletionCatalog` in Core now derives both sorted, bounded candidate lists from the existing authored Structure and StructureSet registries, using the active Sphere generator's `HasGeneratedSurfaceStructure` capability for locate eligibility. This also avoids offering valid but not currently generated roots. It does not query chunk residency, regenerate terrain or introduce a second structure index. No active Sphere means no locate candidates.
- The Godot chat-state publisher transports `locatableStructures` separately; the WebUI bridge validates it as a bounded subset of the full `structures` list, and only the `/locate structure` completion branch uses it. `/place` and its group variation suggestions remain unchanged. Actual command execution continues validating intent authoritatively in Godot.
- Core tests cover locatable flags, grouped structures, Structure Sets, active/generated eligibility, no-world behavior, deterministic sorting and a 512-candidate cap. Chromium tests cover distinct `/place` vs `/locate` suggestions and rejection of fabricated bridge entries. Godot/WRY input and in-game rendering QA remain separate.

## Inventory cursor: drop into the world on empty backdrop — 2026-10-13

- Compared MineClone `world-systems-rebuild/src/hud/inventory/interaction.rs::handle_empty_inventory_click` with Asteria's `InventoryGameplayPage`, inventory controller, player cursor and world drops. MineClone generates a `PlayerDropRequest` when the user left-clicks outside controls while carrying an item. Asteria had a destructive Trash action but no physical cursor drop.
- Asteria now routes a left-click on the empty inventory backdrop through `ui.inventory.drop_cursor`. Slots, panels, the character portrait, scrolling and Close remain independent. The UI controller ignores the intent unless an inventory with a cursor stack is open; the native adapter revalidates active player and inventory state.
- New Core transaction `PlayerInventoryCursorDrop` commits the entire cursor stack (including quantity, block state and metadata) using the existing authoritative `DroppedBlockRuntime.TrySpawnBatch` with a forward launch velocity; **only after successful world publication** is the player cursor cleared. This path uses the existing no-eviction capacity check and rejects unsafe positions/velocities without touching inventory state. The normal `DropSelectedItem` hotbar shortcut retains its prior one-item behavior and now shares the launch calculation. Death drops still use the zero-velocity batch default.
- When drop capacity is exhausted, the cursor remains held and WebUI shows a localized capacity error (EN/PT-BR/ES). Core tests cover exact stack/velocity transfer, no duplicate submission, full-capacity rejection and invalid input. Browser tests cover the real blank-backdrop interaction versus buttons/slots, and the semantic bridge dispatch.
- Embedded Godot/WRY interaction, pointer hit testing and actual physical item pickup require final native QA.

## Player crouch pose parity — 2026-10-13

- Compared MineClone `world-systems-rebuild/src/player/movement/crouching.rs` against Asteria `PlayerModelPresentation`. The former lowers five independently animated pivot groups (body 0.18, head 0.28, arms 0.24, legs 0.02 world units); Asteria previously compressed the entire third-person character to 1.5/1.8 of its original height. This visibly deformed the player's skin and equipment proportions instead of expressing a crouching posture.
- Replaced whole-model vertical scaling with `PlayerCrouchPose`-derived joint translation offsets. The Godot adapter caches each imported pivot's original position and updates only while crouch blend changes; authored rotational animations and head-look rotation remain independent. Authoritative crouched collision capsule, player coordinates and camera height remain untouched.
- Engine-agnostic tests cover all six pivots, symmetrical limbs, halfway blending, standing restoration, unknown nodes and out-of-range/nonfinite blend input. Native Godot/WRY QA for low roofs, skin/equipment appearance, animated crouch transitions and third-person perspective remains pending.

## Target creature portrait camera parity — 2026-10-13

- Compared MineClone `world-systems-rebuild/src/hud/entity_targeting/portrait.rs` against the active Asteria `CreaturePortraitPresentation` pipeline. The source HUD uses a 128×128 portrait rendered from an offset three-quarter camera; Asteria already loaded real species GLBs and published cached portraits, but used a straight-on camera and assumed the collider center was `height/2`.
- Extracted a deterministic `CreaturePortraitFraming` calculation in Core. Camera framing uses the largest authored collider dimension (`distance = max(size) × 2.4 + 0.45`), offsets by `(0.4 × distance, 0.25 × distance, distance)` and looks at the full authored collider `centerOffset`, including possible horizontal displacements. The native Godot portrait adapts this pose with the reference's 45° default camera FOV.
- Tests exercise normal and larger collider definitions, offset center, lack of gameplay collider mutations and null contract. Native Godot/WRY 128×128 portrait screenshot and presentation checks are still required.

## Shared menu transitions — 2026-10-13

- Compared MineClone `main/src/ui/transition.rs`: 160 ms fade-out followed by 160 ms fade-in also covers Pause, Settings and Controls state changes. The existing Asteria `ScreenTransition` matched that timing but was mounted only within the pre-world screen tree.
- Reused **one** transition instance in `ui/src/App.tsx` for pre-world and navigable menu overlays (Pause / Game Settings / World Settings / Controls), with a key for each actual destination. The world-creation screens are suppressed when a settings/controls overlay is active, instead of remaining mounted behind the overlay. The same existing transition cancels/reverses pending navigation; no second transition implementation or native gameplay state was introduced.
- Preserved direct resume/mouse capture, death modal, chat, inventory and storage semantics outside the menu transition. ESC still invokes the existing navigation controller immediately, without deferring gameplay decisions behind the visual fade.
- Browser regressions cover pause → game settings → pause → gameplay and pause → controls → pause including outgoing-menu preservation through fade-out. Godot/WRY native focus and visual comparison remain to be checked in the live client.

## Controls scroll parity — 2026-10-13

- Rechecked the current native input, controls presenter and browser tests: W ×2 Run, MMB Pick Block, and localized Enter/history/Tab chat shortcuts are already implemented. The earlier UI parity table was stale and has been corrected, along with the chat-action and menu-transition notes.
- Migrated `ControlsPage` from a one-off CSS scrollbar to the existing shared measured `ScrollArea` used by Settings and World Selection. The same native viewport owns keyboard, wheel and touch scrolling; the rendered track/thumb mirrors measured overflow. Controls cards, keybind capture and screen-level footer remain unchanged.
- Added a short-controls Storybook state plus Playwright tests for overflow detection, absence of scrollbars when not needed, PageDown, wheel, track clicks and the fixed footer position on a constrained viewport.
- Native Godot/WRY visual scroll, focus and input interaction still require in-game QA. Chat and Creative lists remain separate scroll parity follow-ups.

## UI port batch: Chat + Creative Inventory scrolling — 2026-10-13

- Audited both widgets against current Asteria `main` and the MineClone reference. Creative category and item catalog already used the shared measured `ScrollArea` with bounded per-category/search restoration; no duplicate scrolling implementation was added. The Chat transcript still had an independent browser-native scrollbar.
- Migrated Chat history to the shared scroll component, retaining its 330 px maximum transcript height, 64-message presentation cap, localized action links, input autofocus and ARIA live log. New messages scroll to the end when the transcript was previously at the bottom or has just been shown, but do not interrupt users browsing earlier messages. The shared viewport handles wheel/keyboard/pointer scrollbar interaction.
- Added Storybook dynamic transcript/Creative content and Playwright regressions for chat overflow, bounded history, bottom following/reading older messages; Creative category and catalog overflow, keyboard scrolling, per-category offsets, per-search offsets and tab/component remount restoration. These cover multiple related UI port gaps together.
- Pending: actual Godot/WRY screenshot, focus and input checks. Avoid marking a visual parity pass complete solely from browser tests.
