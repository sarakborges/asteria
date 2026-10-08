# Create New World — parity contract (MineClone `main`)

**Confirmed 2026-10-08.** MineClone `main` at `a4e9d00dafd384ae0086e30f684c1ae85d1f64c7`; compare with `world-systems-rebuild` at `e8bc8562abc22eb4a54561c1e7afe8cc086e3dd9`. Asteria source reviewed at `d1c32f40a83c93c659d8a0a2ef02a7056dfc99a0`.

## Root-cause correction

**The previous audit was wrong.** MineClone `world-systems-rebuild` does not contain the newer creation settings in MineClone `main`. The UI was wrongly described as substantially ported. React source, Storybook builds, DOM smoke tests, and the existence of a left sidebar cannot prove parity against an incomplete reference.

For current **UI** contracts inspect `mineclone/main/src/screens/settings_screen/{layout.rs,new_world_section.rs,spawn_biome_section/*,biome_size_multiplier_section.rs,game_rules_section.rs,world_settings_section.rs}`. For generation behavior inspect `mineclone/main/src/world/new_world.rs` and actual generation consumers, and adapt to Asteria's engine-independent ownership.

## Exact Create New World structure on MineClone main

1. **World Settings**: Name → **Seed (inside World Settings)** → Game Mode (Survival, Creative, Spectator).
2. **World Generation**: World Type/Generation Mode (Normal, Flat, Void) → Spawn Biome → Biome Size Multiplier → Spawn Structures → Single Biome → Spawn Caves → Spawn Oceans.
3. **Game Rules**: Ticks Per Second → Spawn Creatures.

**Implemented in Asteria source in PR #61:** all three creation sections, including World Generation. This is not yet an in-game WRY screenshot parity claim. All settings cross the creation/controller/IPC/Core generator boundary.

| Setting | MineClone `main` behavior | Asteria state |
| --- | --- | --- |
| Normal / Flat / Void | Mode selector; Normal defaults true for Caves/Oceans, Flat and Void reset both to false | Implemented: native Core Flat/Void terrain fields and world mode selector; Void uses a deterministic starter platform. |
| Spawn Biome | Searchable dropdown with Random or selectable *surface* biomes from default dimension, sorted by localized name; excludes ocean | Implemented: scoped searchable dropdown and explicit Core spawn search. Option display names are currently humanized IDs, **not yet biome-localized names**. |
| Biome Size Multiplier | Range **0.5–5.0** in 0.1 increments, slider + numeric editor, default 1.0; **hidden when Single Biome** | Implemented in field rule region bounds and responsive slider/numeric editor. |
| Spawn Structures | Independent boolean, default true | Implemented: generator omits authored procedural surface structures, retaining separate manual structure APIs. |
| Single Biome | Independent boolean, default false, **requires explicit Spawn Biome** before Create; Random entry withheld while enabled | Implemented: validates selected Sphere biome and uses a single surface-biome pool. |
| Spawn Caves | Boolean, default true in Normal; disabled when Void, reset false on Flat or Void selection | Implemented through Core SurfaceTerrainField; Void disables cave generation. |
| Spawn Oceans | Boolean, default true in Normal; disabled when Void, reset false on Flat or Void selection | Implemented in GeneratedFluidField, ocean surface-pool selection, and Flat coast policy. |
| Ticks Per Second | Positive integer (Game Rules) | Wired |
| Spawn Creatures | Boolean (Game Rules) | Wired via `WorldGameRules` and `WorldCreationOptions` |
| Seed | Exact unsigned 64-bit decimal string (World Settings) | Wired and precision-safe |
| Game Mode | Survival/Creative/Spectator (World Settings) | Wired |

There are **four World Generation toggles**, plus **Spawn Creatures** in Game Rules. Game settings have additional unrelated HUD/client toggles and must be audited separately, not counted as generator options.

## Real engine implementation required for feature parity

**Do not add superficial toggles that leave terrain unchanged.** The source-of-truth path is:

`NewWorldPage` → `WorldCreationController` → semantic `ui.world.create` payload → Godot validation → immutable `WorldCreationOptions` → `DimensionSessionStateStore` / per-dimension session generation policy → `BiomeWorldGenerator` → responsible Core terrain/biome/structure/fluid owners.

The following acceptance work is implemented in source under PR #61, with further in-game verification pending:

1. Define immutable, validated per-world generation configuration (mode, flags, spawn biome, 0.1-resolution biome size); keep settings with the creation/session owner. Enforce Single Biome requires an actual surface biome, reject invalid IDs before creation. Do not copy MineClone's Bevy types directly.
2. Implement **Normal/Flat/Void** as genuine deterministic world-generation modes, keeping nonnegative Y and Sphere Shell floor/roof, valid loading/spawn, saved-session isolation and manual changes/persistence. Flat/Void are **not** cosmetic terrain overrides.
3. Apply Spawn Biome to initial generator destination selection; honor the authored per-dimension surface biome pool and ocean exclusion. Single Biome must change actual surface biome identity, not only spawn selection.
4. Make Biome Size Multiplier affect actual biome region sizes and transitions; ensure all relevant field rules and dependent caches/identities reflect it. Suppress the input in Single Biome.
5. Make structures, caves and oceans truly conditional in their owning Core generation systems. Keep explicit authored generated fluids/features and other Sphere behavior deterministic; do not create a generic hydrology subsystem.
6. Port the **World Generation** section into NewWorldPage with the order above, localized labels, searchable biome list, full enabled/disabled states and correct separate scroll section in left nav. Keep Winky Rough Variable and Atomic Design stories.
7. Add tests for every toggle changing deterministic world output, default parity, Flat/Void spawn readiness and empty/solid materialization, valid/invalid chosen biomes, multiplier bounds and region sizes, no caves/oceans/structures when toggled off; add real-React rendered tests for all **three** panels at 1920×1080 and 640 px and embedded WRY QA.

## Source and verification rules

- MineClone `main` code is the UI reference; `world-systems-rebuild` is still relevant for runtime internals **but does not include the above UI features**.
- Asteria's creation flow now applies generation settings to Core and renders all three sections. In-game WRY/visual comparison and localized biome display labels remain pending; CI tests cannot substitute for those.
- MineClone has **no active Spawn Rivers or Spawn Lakes controls** in `main` `new_world_generation_section()` despite Asteria localization keys existing. Do not invent UI toggles merely from localization strings.
- Seed location is fixed: **World Settings**, not World Generation and not Game Rules.
