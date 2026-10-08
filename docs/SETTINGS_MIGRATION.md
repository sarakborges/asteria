# MineClone settings and gamerules migration

Source of truth for this audit: `sarakborges/mineclone` branch `world-systems-rebuild`, compared with Asteria `main` on 2026-10-07. This is an **implementation inventory**, not a claim that runtime behavior was ported.

## Scope and authoritative owners

MineClone separates three lifetimes. Asteria must preserve them without copying Bevy resources or bypassing the Godot/Core/WebUI boundary.

| Scope | MineClone reference | Asteria target | Current Asteria status |
| --- | --- | --- | --- |
| Per-world gamerules | `src/world/game_rules.rs` | Core world/session settings; Godot runtime consumer | WorldGameRules runtime owner implemented; WebUI editing pending |
| Per-world creation | `src/world/new_world.rs`, `src/screens/settings_screen/new_world_section.rs` | Core validated creation request, active world identity | Validated WorldCreationOptions exists; runtime UI still sends seed only |
| Per-player mode | `src/player/game_mode.rs`, `src/screens/settings_screen/world_settings_section.rs` | Core player state and Godot movement/interaction adapter | Mode enum and creation selection modeled; gameplay behavior pending |
| Client graphics | `src/world/render_distance.rs`, `src/app/game_config.rs` | Godot client config/streaming selection | Client preference model, runtime radius and disk settings implemented; UI pending |
| Client HUD | `src/hud/mod.rs`, `src/screens/settings_screen/hud_section.rs` | Client preferences; derived WebUI HUD state | 10 hints, hideHints and target position modeled/persisted; UI pending |
| Client keybinds | `src/app/keybinds.rs`, `src/screens/settings_screen/keybinds_section.rs` | Godot input action/keymap owner | 7 keybind preferences persisted with conflict checks; Godot input consumers pending |
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
- [x] **B. Client preferences**: mutable render distance with streaming integration, persisted graphics/HUD/keybind settings, typed config store, explicit change notifications and tests.
- [ ] **C. Input and gameplay consumers**: modes/flight/spectator targeting and visibility, action routing and key capture. Implement only actions that exist; carry the rest as visible gaps.
- [ ] **D. UI/controller integration**: wire NewWorldPage, SettingsPage, ControlsPage and PauseMenuPage to authoritative snapshots/actions; apply React/Storybook conventions and all three locales.
- [ ] **E. World save/catalog parity**: world name, saved rules, player mode, load/resume and any required save migration policy when world catalog persistence is implemented.
- [ ] **F. End-to-end parity audit**: check every setting for defaults, mutation, gameplay effect, reload/session transition behavior, localization and tests. No unbacked toggles, placeholders, fake saves, or duplicated owners.

## Explicit gaps as of this audit

Asteria's `App.tsx` mounts gameplay HUD, start and new-world screens only. `SettingsPage`, `ControlsPage`, `PauseMenuPage` and `WorldSelectionPage` are available as presentation but not connected to application flow. `WorldCreationController` sends only `seed`. `Main.cs` reads the client-owned render distance and the world-owned tick rate. Editing screens are not yet wired. `FpsPlayer` implements hardcoded movement/jump keys and no complete mode/keybind switching. Thus **visual presence is not runtime parity**.

Audit references: MineClone `src/world/game_rules.rs`, `src/world/new_world.rs`, `src/app/game_config.rs`, `src/app/keybinds.rs`, `src/hud/mod.rs`, `src/screens/settings_screen/{layout,game_rules_section,world_settings_section,new_world_section,hud_section,keybinds_section,languages_section,render_distance_section}.rs`; Asteria `docs/UI_MIGRATION.md`, `src/Asteria.Godot/Main.cs`, `src/Asteria.Godot/Gameplay/FpsPlayer.cs`, `src/Asteria.Core/World/WorldTickClock.cs`, `ui/src/{App.tsx,controllers/WorldCreationController.ts,localization/LocalizationProvider.tsx}`.

## Phase A delivery (2026-10-07)

- Implemented Core `WorldGameRules` with MineClone defaults (40 ticks/second, creature spawning enabled), positive-rate validation and no-op mutation reporting.
- Implemented immutable `WorldCreationOptions` with name/seed/initial game mode and rule values, plus `PlayerGameMode` capability policy.
- Each `DimensionSessionStateStore` owns one shared mutable `WorldGameRules`. Its Sphere states share that owner across retirement/reactivation and never mirror the tick rate.
- The Godot runtime now reads the rate from the same owner for the world tick clock, active-fluid scheduling and chunk-activation fluid scheduling; no hardcoded tick rate remains in `Main.cs`.
- Added Core regression tests for defaults, validation, changes, game-mode capability policy, tick timing and session ownership.
- **Not included**: WebUI editing, runtime creature spawner, actual Creative/Spectator physics and inventory, disk save, and complete world-name collision handling. These remain later migration gates, not falsely enabled settings.

## Phase B delivery (2026-10-08)

- `ClientPreferences` is the Core owner of mutable, validated client graphics/HUD/keybind state, with revision on meaningful changes only.
- Render distance is 4–24 chunks; **Asteria retains its existing default of 4** to avoid suddenly scaling its still-in-progress worldgen workload up to MineClone's default of 12. The full authored range is supported.
- `ChunkStreamingController` no longer stores a duplicate radius; streaming selection takes the current client preference explicitly, including after Sphere transitions and after loading. Its existing revisioned worker rejects superseded requests.
- HUD preferences: global hide-hints, all ten MineClone hint switches, and target position (center, top-right, hidden). They are persisted, but dormant controls await actual HUD consumers in phase D.
- Keybinding preferences: seven MineClone actions with default bindings, rejection of reserved movement/hotbar keys, and duplicate binding validation. The actual Godot input actions remain hardcoded until phase C; saved keybinding values do **not yet change gameplay input**.
- Godot reads/writes `user://client-preferences.json` via the OS user-data directory and an atomic temporary-file replacement, not under any pack. Corrupt config is rejected with a warning and defaults restored in memory.
- WebUI bridge now sends `game.client_preferences` snapshots and accepts semantic `ui.client_preferences.{render_distance,hide_hints,target_block_position,hint,keybind}` mutations, with validation and explicit error statuses; the UI does not yet expose controls.
- Unit tests cover range bounds, defaults, hint independence, key conflicts and JSON roundtrip/invalid files. This is client config, **not** world disk saving.

## Phase C implementation progress (2026-10-08)

- Core `PlayerSessionState` now owns active mode and creative flight state for one player across Sphere transitions; validated Survival, Creative and Spectator changes are idempotent.
- Creative flight toggles on a double-tap of the configured Jump key within 12 logical world ticks, with flying ascent (Jump), descent (Descend) and gravity/collision retained. Landing while descending ends creative flight.
- Spectator mode always flies, disables body collisions and block break/place commands, clears target HUD, and hides interaction HUD/hotbar/crosshair via an authoritative `game.player_mode` message. Positions are clamped to the nonnegative world Y boundary.
- Native Godot physical key events are mapped to the saved keybind contract, including left/right modifier locations. Jump and Descend are live configurable actions; WASD remains reserved movement. Uncaptured mouse/keyboard input is cleared to prevent stuck movement.
- Godot keybind capture uses `ui.client_preferences.capture_keybind` (payload `{action}`) and `ui.client_preferences.cancel_key_capture`, with `game.client_preferences.key_capture` outcomes (Capturing, Changed, Unchanged, Cancelled, UnsupportedKey, ReservedKey, KeyConflict, SaveFailed); browser-side global key events are not used.
- Runtime game-mode updates use `ui.player.set_game_mode` (payload `{mode: "Survival" | "Creative" | "Spectator"}`) and publish `game.player_mode` (lowercase mode, flying and canInteract). Initial game mode comes from the Core creation option, not from a second mutable field in Godot.
- **Remaining before Phase C is fully complete**: Inventory, Chat, Tool Action and Drop Item keybind consumers await their real gameplay runtime; Change Perspective awaits an actual third-person camera/player rendering contract. Spectator exit is rejected with `NoSafeCollisionSpace` if the destination body overlaps collision or spans unloaded chunks; a future teleport/relocation UX can offer a safe destination without silently trapping the player. No placeholder key handlers are added to pretend these mechanics already exist.
- Config/state mutation remains Core authoritative. Mode-specific movement is confined to `FpsPlayer`; web presentation remains bridge/controller/UiStore/React. Configurable keybind capture stays owned by native Godot, never a WebUI global event listener.

Asteria reserves **F3/F4** in addition to MineClone's WASD/hotbar keys because those are engine-owned debug/Sphere-switch hotkeys; allowing their reassignment would create two authoritative handlers for one key.

## Phase D implementation notes (2026-10-08)

- Added typed world creation name, initial mode and ticks-per-second fields over the validated Core `WorldCreationOptions` boundary. The name is session metadata, not a claim of disk saving.
- Added live `ui.world.set_ticks` and `game.world_settings` bridge messages; the world clock and fluid schedulers already consume the same mutable rules owner.
- Added `ui.game.resume` for deliberate Godot-native mouse recapture. The WebUI must not capture global gameplay input.
