# MineClone → Asteria: port reconciliation

**Reconciled:** 2026-10-14. **Asteria `main` baseline:** [`fd28b67b2`](https://github.com/sarakborges/asteria/commit/fd28b67b252bcb26195fcfa10c9cb5f46032ae09) (the preceding content commit changes tree textures, not port systems). **MineClone references:** [`main` `a4e9d00`](https://github.com/sarakborges/mineclone/commit/a4e9d00dafd384ae0086e30f684c1ae85d1f64c7) and [`world-systems-rebuild` `e8bc856`](https://github.com/sarakborges/mineclone/commit/e8bc8562abc22eb4a54561c1e7afe8cc086e3dd9).

**Current verdict: no specific missing MineClone port functionality has been substantiated from this review of the current source and commit history.** This is **not** a claim of bug-free play, perfect visual fidelity, or successful side-by-side Godot/WRY playtesting. Before describing any feature as missing, recheck the actual HEAD, subsequent fixes, implementation path, and reference behavior. A dated audit is not a work queue.

The previous version of this file was an October 9 status snapshot with chronological notes appended through October 14. Consequently, early `P0`/`P1` rows continued to say **partial/missing** after later commits finished their implementation. That version remains recoverable through Git history; its obsolete priority queue is intentionally not repeated here.

## Scope and status rules

- **Implemented in source:** the authoritative Core behavior, Godot integration, and/or WebUI path exists as applicable; commits and regression tests support this assessment. It does **not** mean every runtime scenario has been manually accepted.
- **Confirmed port gap:** an identified MineClone behavior is absent/different in current Asteria, is **not** a deliberate product/engine adaptation, and has a reproducible source-level or in-game example. **None is confirmed in this reconciliation.**
- **Runtime QA:** an existing implementation needs real embedded Godot/WRY exercise or side-by-side screenshots. This is an **acceptance activity**, never automatically another implementation batch.
- **Independent defect/performance/content request:** track separately from port parity; fixing an observed Asteria bug does not establish that the MineClone feature was never ported.

**Explicit exclusions:** World Recipes, sound effects, old-save backwards compatibility, and hydrology as a new subsystem. Biome authoring, vegetation, environmental polish and terrain-specific content have their own backlog. Ordinary inventory Crafting **is** in scope and already implemented.

## Current source/commit inventory — do not re-port

| Area | Implemented path / latest relevant evidence | Remaining classification |
| --- | --- | --- |
| **World selection and persistence** | Core session serialization, atomic checkpoint generations, world load/delete, validation/recovery, creation, WorldCard Recreate and PNG thumbnails. Save happens on leave/app exit; failed save can be explicitly acknowledged without preventing exit. `45d247b`, `af88e97`, `e0b6d00`, `eb6a16f`, `9399635`, `a16e1f6`. | Runtime QA only |
| **World creation and generation switches** | Three-panel New World, real Normal/Flat/Void terrain, Spawn Biome selection/localized labels, Single Biome, region size, structures/caves/oceans options, default spawn/entry, Flat ocean toggle and Void without Sphere Shell. `3d8f2a6`, `0bfabac`, `b98c5a4`, `925878c`, `1a80e68`. | Runtime QA only; independent biome content outside port |
| **Portals and Sphere travel** | Attuned Dimensional Slicer; bounded Glass frames; one destination-tagged portal identity; physical player-collider intersection; exact-coordinate arrival/return portal; authored connector staircase to surface; bounded vertical streaming, rollback and save/reload tests. `a0930bb`, `83f3295`, `9d1acb0`, `aff25b2`, `e03c59a`. | Runtime navigation/visual acceptance only |
| **Inventory, storage and crafting** | Backpack/hotbar/equipment, 27-slot Storage Box, sorting, metadata-safe cursor, right-click split/place one, Shift-transfer, Creative Shift-fill, cursor-to-world drop, Q drop without eviction, partial pickups, loot tables, death drops/Keep Inventory and real recipe transactions. `aaf24aa`, `8c15411`, `84ea9ce`, `5f38b9d`, `d8bd343`, `6c750bc`, `b062442`, `f85f8d7`. | Runtime QA only |
| **Tools and mining** | Artisan's Kit microblocks, Brush/dye, Bucket metadata and source fluids, Shears/attached Layers, Carpenter's Axe, Architect's Compass selection/export, top-Layer mining/tools/drops, crack surfaces, textured/tinted fragments and Pick Block/placement preview. `1410`/PR #41, `1401`/PR #43, `6e310cb`, `8abc341`, `86c7449`, `964ffe3`. | Runtime QA; do not infer missing behavior from an old note |
| **Player and creatures** | 64px skin resolution/fallback, UV/alpha and overlay parts, first/third-person viewmodels, held Objects, crouch pose, animations, armor appearance/durability, health/oxygen/death/respawn, slime landing attacks, pursuit, knockback, ranged projectiles and creature particles/portraits. `6c9a1f4`, `0b3dfbd`, `c97827f`, `a539bfe`, `1651349`, `ce7ed64`, `e1c66c0`, `6bd4aed`, `6360621`. | Native 3D visual/combat acceptance only |
| **Rendering and HUD** | Sun/Moon, stars/clouds, held RGB lighting with shadow occlusion, illuminated/shadowed Objects, fluid immersion, canonical 3D voxel/Layer previews, biome/dye tint, native-over-catalog icon precedence, fluid/Object HUD targeting and third-person-front compass bearing. `76e88c5`, `f6be927`, `0c578e9`, `0914883`, `d37c81e`, `dfedab1`, `f4cfc9d`. | Embedded Godot/WRY visual comparison only |
| **Objects, vegetation and structures** | Independent persistent Object occupancy, support/loot lifecycle, submerged and emissive flora, authored crossed/extruded/cuboid/GLB visuals, lighting/shadows, hitboxes, Structure Object payloads, manual StructureSet/connector placement and Volcano as an authored Structure. `ac422df`, `563db06`, `711dfc6`, `21ccdc8`, `b549e8e`, `aff29c8`, `6a9ccf6`, `535e917`. | Content-specific testing/QA, not missing systems |
| **Chat, input and WebUI** | Command handling, locate/warp/place/export links, autocomplete/history, T/ESC/focus/capture recovery, OS focus loss, WRY inventory key suppression, Creative scroll isolation, shared scrollbars, transitions, localized UI primitives and editable bindings. `21ca2e9`, `a177fa1`, `b74c594`, `bc32717`, `be85ab9`, `b6a782b`, `b0ef823`. | Native input/viewport acceptance only |
| **Packs and Mods (Asteria extension)** | Embedded vanilla pack, deterministic overlays/tombstones, persistent pack selection, opt-in C# ModAPI loader/hooks, Mods/Packs WebUI and documentation. `b0a31b4`, `4b8a81a`, `a7ab158`, `e8eb20a`, `a67d1f0`. | Separate extension QA; **not** a MineClone port gap |

Commit IDs are breadcrumbs to subsequent source changes, not proof that all listed runtime scenarios were exercised. For deeper system contracts see [portal activation](PORTAL_ACTIVATION.md), [inventory](INVENTORY_MIGRATION.md), [projectiles](PROJECTILES.md), [world creation](WORLD_CREATION_PARITY.md) and [WebUI](UI_PARITY_AUDIT.md).

## Specifically retired false positives

- **Portal arrival/carving/surface connection:** implemented after the early P0 note by `514ee9f`, `83f3295`, `9d1acb0`; collision-contact traversal later corrected in `e03c59a`. Never queue "build portal arrival" again.
- **Player animations, skins, equipment, combat abilities:** the old "health/knockback/viewmodel pending" statements predate `d1b4f2b`, `606bc30`, `c97827f`, `e1c66c0`, `6bd4aed`, `6c9a1f4` and `0b3dfbd`.
- **Dynamic-light occlusion:** `0c578e9` supersedes the old statement that held light does not respect shadows.
- **Creative categories, search and scrolling:** `0de8c35`, `6cf4738`, `be85ab9` supersede old UI implementation to-dos.
- **World generation, Save/Leave, Delete, Recreate, error/focus handling:** `b98c5a4`, `45d247b`, `e0b6d00`, `b74c594` supersede old "implement" tasks.
- **HUD native layered icons and biome tint:** `d37c81e`, `e4c34e6`, `dfedab1` and `f4cfc9d` supersede older static-preview and compass limitations.
- **Object visuals, loot, water support and volcano discovery:** `789c22f`, `711dfc6`, `aff29c8`, `4ac53af`, `535e917` supersede older partial status.
- **Native-test infrastructure:** `tests/godot/native_presentation_smoke.gd` and companion C# smoke suites already exist. Do **not** start a fresh native smoke framework merely because the old audit said "needs Godot QA."

## Verified differences which are not automatically port gaps

1. **Portal storage model:** MineClone [`targeting/portal_activation.rs`](https://github.com/sarakborges/mineclone/blob/main/src/targeting/portal_activation.rs) inserts a portal *Layer* on the face of an empty cell. Asteria [`PortalActivationRuntime.cs`](../src/Asteria.Core/World/PortalActivationRuntime.cs) deliberately uses a noncolliding, centered Layer-*shaped block*, because its AttachedBlockLayer storage requires a host voxel. Behavior and persistence are connected; do not convert to an attached Layer on appearance alone.
2. **Base movement speed:** MineClone `src/player/movement/config.rs` authors `WALK_SPEED = 5.0`; Asteria retained a 7.5 base speed as an explicit gameplay/balance choice. Sprint/crouch multipliers are ported. Only change this with an explicit gameplay decision.
3. **UI composition:** Asteria intentionally keeps Winky Rough Variable, separate Controls page, inventory above crafting, Sphere/biome HUD labels and a Godot-owned underwater overlay. These are not "missing" implementations.
4. **Saving policy:** Asteria deliberately saves on exit/leave, not via automatic saves or an independent Save World button. Do not reintroduce unsupported save actions to mimic stale UI audit notes.

## Remaining work — acceptance, not implementation backlog

No default "next port batch" is authorized by this document. If functional testing is desired, exercise **already implemented** flows rather than re-creating their controllers:

1. **Real save lifecycle:** create → play → leave/quit → restart → load; corrupted/locked generations and no-loss failure confirmation; delete/recreate and repeated session entry.
2. **Real WRY input:** T autofocus, ESC, inventory E, Q drop, Alt+Tab, modal key containment, mouse recapture, rapid Creative filter/scroll.
3. **In-engine gameplay:** three camera modes, 64px skin/equipment and animations, tools and mining, physical portal round-trip with exact landing/stairs, fluid/Object/creature targeting, combat/drops.
4. **Side-by-side visuals:** MineClone `main` and Asteria at fixed viewports and world seeds; player, HUD, creature material, lighting/shadows, icons with biome changes, modal focus and narrow-screen layouts.

Automated evidence exists in `src/Asteria.Core.Tests/`, `ui/tests/browser/` and `tests/godot/`. Those tests may establish a regression, **not** a successful interactive Godot/WRY playtest. This reconciliation did not execute either game or claim such results.

### Before opening a new port task

Record: (a) exact MineClone `main`/rebuild file and observed behavior; (b) Asteria HEAD plus all intervening fixes; (c) current authoritative implementation path; (d) reproducible discrepancy and expected outcome; (e) whether this is a deliberate Asteria deviation, a rendering QA concern or a genuine missing feature. Only the last case enters a feature-port implementation batch. If no concrete discrepancy survives, the correct next action is **validation or a separately scoped product task**, not speculative coding.
