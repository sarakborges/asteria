# MineClone settings and gamerules migration

Source of truth for this audit: `sarakborges/mineclone` branch `world-systems-rebuild`, compared with Asteria `main` on 2026-10-07. This is an **implementation inventory**, not a claim that runtime behavior was ported.

## Scope and authoritative owners

MineClone separates three lifetimes. Asteria must preserve them without copying Bevy resources or bypassing the Godot/Core/WebUI boundary.

| Scope | MineClone reference | Asteria target | Current Asteria status |
| --- | --- | --- | --- |
| Per-world gamerules | `src/world/game_rules.rs` | Core world/session settings; Godot runtime consumer | WorldGameRules runtime owner implemented; WebUI editing pending |
| Per-world creation | `src/world/new_world.rs`, `src/screens/settings_screen/new_world_section.rs` | Core validated creation request, active world identity | Validated WorldCreationOptions exists; runtime UI still sends seed only |
| Per-player mode | `src/player/game_mode.rs`, `src/screens/settings_screen/world_settings_section.rs` | Core player state and Godot movement/interaction adapter | Mode enum and creation selection modeled; gameplay behavior pending |
| Client graphics | `src/world/render_distance.rs`, `src/app/game_config.rs` | Godot client config/streaming selection | Hardcoded at 4 chunks in `Main.cs` |
| Client HUD | `src/hud/mod.rs`, `src/screens/settings_screen/hud_section.rs` | Client preferences; derived WebUI HUD state | HUD view exists; preferences not wired |
| Client keybinds | `src/app/keybinds.rs`, `src/screens/settings_screen/keybinds_section.rs` | Godot input action/keymap owner | Hardcoded player keys; ControlsPage is display-only |
| Language | `src/screens/settings_screen/languages_section.rs` | React localization provider | EN/PT-BR/ES and persistence already implemented |
| Settings navigation | `src/screens/settings_screen/layout.rs` | React SettingsPage + UI navigation controller | Presentation-only; not mounted in App runtime |

## Complete authored MineClone settings inventory

### World creation and world settings

- **World name**: authored text, world catalog identity and validation. Asteria has no disk catalog or user-editable world name yet.
- **World seed**: unsigned 64-bit decimal value, with random generation. **Already supported in Asteria**; keep Core validation.
- **Game mode**: **Survival** (default), **Creative**, **Spectator**. This is player state, even when initially selected during world creation. Respect inventory, flight, collision/targeting, and spectator visibility semantics rather than storing a cosmetic string.
- **Gamerule `ticksPerSecond`**: default **40**, integer **greater than zero**, editable on new and active worlds. MineClone increments/decrements with numeric input and saves a changed value in the world state. Asteria's world clock, fluid simulation and residency scheduling now read a shared per-world `WorldGameRules` rate; WebUI editing remains pending.
- **Gamerule `spawnCreatures`**: default **true**; editable on new and active worlds and saved with world state. Asteria currently has no compatible creature-spawning owner. Introduce the stored rule with a tested consumer when spawn behavior exists; do not claim turning it off changes a nonexistent system.
- The rebuild branch's `NewWorldConfig` explicitly does **not** own extra generator knobs. Do not infer additional worldgen controls from old MineClone code or automatically expose unrequested biome/terrain sliders.

### Global game settings

- **Graphics**: render distance, **4–24 chunks**, MineClone default **12**. Asteria's current value is fixed at **4**. Adapt Asteria streaming limits and work budgets; do not blindly duplicate Bevy's visibility-ring math.
- **HUD**: `hideHints` (default false), `targetBlockPosition` (**Center**, **TopRight**, **Hidden**; Center default), plus **10 individual hint preferences** (all enabled by default): open inventory, close inventory, rotate block, break block, break/place block, brush paint, brush clear, Artisan's Kit, shears, structure tool. For functionality Asteria has not yet ported, keep absent controls absent until an authoritative action/HUD contract exists.
- **Keybinds**: **7 actions**: Jump (Space), Descend (Left Shift), Inventory (E), Chat (T), Tool Action (R), Drop Item (Q), Change Perspective (F5). MineClone rejects conflicting bindings and reserves WASD and hotbar digits 1–9. In Asteria, Godot owns global key/mouse input; WebUI never listens globally for gameplay keypresses.
- **Language**: English, Brazilian Portuguese and Spanish; Asteria already has these three and stores the selection in the WebUI localization provider. Do not add a competing language owner.
- MineClone's global config groups these as `language`, `graphics`, `miscellaneous.hud`, `keybinds`, persisted to a user-level `config.json`. Asteria needs its own typed, validated, change-driven **client preference** persistence independent of authored packs and world data.

## Required runtime/UI contract

1. **Single authority per lifetime**: global client preferences, world-session gamerules, and player game mode must be different models with explicit read/write operations. Do not put user preferences under `packs/default` or create parallel state in React.
2. **Typed definitions and bounds in Core** where gameplay invariants require them. Godot adapters own native key events, scene/player behavior, filesystem/platform access and runtime application.
3. **Creation is transactional**: React sends a validated request for name, seed, initial mode and rules; Godot/Core validates before starting the world; invalid values return translation keys. Avoid UI-only values ignored during creation.
4. **Active-world changes**: world rules and mode changes go through their owners; propagate authoritative snapshots back to WebUI, and keep unchanged values from triggering unnecessary work.
5. **Global graphics changes**: render-distance updates must change selection/residency/presentation policy coherently, without restarting world generation or treating render distance as a dimension-authored property.
6. **Keybind capture**: gameplay/global events remain owned by Godot, including when a settings control requests reassignment; reserved/conflicting keys produce explicit feedback. UI controls may use local focus events only for text/form interaction.
7. **Persistence**: keep distinct formats/stores for client preferences, world metadata/rules and player mode. Disk world save/catalog is not yet an Asteria priority; do not call in-memory draft state persisted.
8. **Localization and design**: reuse `SettingsPage`, `SettingRow`, `Toggle`, `Select`, `Slider`, `TextInput`, `ControlsPage`, and existing sections/Storybook. All labels in EN/PT-BR/ES. Preserve **Winky Rough Variable**.
9. **Validation**: add Core unit tests for rule bounds and defaults, key conflicts/reserved keys, config read/write roundtrips, modes and ownership, creation validation, and changing render distance. Run WebUI build, Storybook, localization audit and the relevant Godot/.NET checks.

## Migration order and completion gates

- [x] **A. Core models and session wiring**: `WorldGameRules`, creation options and player mode, defaults, validation, world/session lifetime and unit tests. Do not silently advertise creature spawning or flight before their consumer exists.
- [ ] **B. Client preferences**: mutable render distance with streaming integration, persisted graphics/HUD/keybind settings, typed config store, explicit change notifications and tests.
- [ ] **C. Input and gameplay consumers**: modes/flight/spectator targeting and visibility, action routing and key capture. Implement only actions that exist; carry the rest as visible gaps.
- [ ] **D. UI/controller integration**: wire NewWorldPage, SettingsPage, ControlsPage and PauseMenuPage to authoritative snapshots/actions; apply React/Storybook conventions and all three locales.
- [ ] **E. World save/catalog parity**: world name, saved rules, player mode, load/resume and any required save migration policy when world catalog persistence is implemented.
- [ ] **F. End-to-end parity audit**: check every setting for defaults, mutation, gameplay effect, reload/session transition behavior, localization and tests. No unbacked toggles, placeholders, fake saves, or duplicated owners.

## Explicit gaps as of this audit

Asteria's `App.tsx` mounts gameplay HUD, start and new-world screens only. `SettingsPage`, `ControlsPage`, `PauseMenuPage` and `WorldSelectionPage` are available as presentation but not connected to application flow. `WorldCreationController` sends only `seed`. `Main.cs` continues to use fixed `RenderDistanceChunks = 4`, but world tick rate now reads its shared Core rule. `FpsPlayer` implements hardcoded movement/jump keys and no complete mode/keybind switching. Thus **visual presence is not runtime parity**.

Audit references: MineClone `src/world/game_rules.rs`, `src/world/new_world.rs`, `src/app/game_config.rs`, `src/app/keybinds.rs`, `src/hud/mod.rs`, `src/screens/settings_screen/{layout,game_rules_section,world_settings_section,new_world_section,hud_section,keybinds_section,languages_section,render_distance_section}.rs`; Asteria `docs/UI_MIGRATION.md`, `src/Asteria.Godot/Main.cs`, `src/Asteria.Godot/Gameplay/FpsPlayer.cs`, `src/Asteria.Core/World/WorldTickClock.cs`, `ui/src/{App.tsx,controllers/WorldCreationController.ts,localization/LocalizationProvider.tsx}`.

## Phase A delivery (2026-10-07)

- Implemented Core `WorldGameRules` with MineClone defaults (40 ticks/second, creature spawning enabled), positive-rate validation and no-op mutation reporting.
- Implemented immutable `WorldCreationOptions` with name/seed/initial game mode and rule values, plus `PlayerGameMode` capability policy.
- Each `DimensionSessionStateStore` owns one shared mutable `WorldGameRules`. Its Sphere states share that owner across retirement/reactivation and never mirror the tick rate.
- The Godot runtime now reads the rate from the same owner for the world tick clock, active-fluid scheduling and chunk-activation fluid scheduling; no hardcoded tick rate remains in `Main.cs`.
- Added Core regression tests for defaults, validation, changes, game-mode capability policy, tick timing and session ownership.
- **Not included**: WebUI editing, runtime creature spawner, actual Creative/Spectator physics and inventory, disk save, and complete world-name collision handling. These remain later migration gates, not falsely enabled settings.
