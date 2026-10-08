# MineClone UI component and screen parity

Reference: `sarakborges/mineclone` branch `world-systems-rebuild`. Screens live in `src/screens`, generic UI in `src/ui`, gameplay overlays in `src/hud`. Asteria is React/WebUI with pack-authored presentation, Atomic Design, Godot-owned input and **Winky Rough Variable**.

**Rule:** an existing React component is not evidence that the corresponding feature is connected to Godot. Track visual parity, behavior and runtime integration separately. Storybook fixture data is not gameplay state.

## Reusable UI primitives

| MineClone | Asteria | Status and next gap |
| --- | --- | --- |
| `ui/button.rs` | `atoms/Button` | Normal/primary/danger, sizes, hover and selected/pressed semantics available |
| `ui/surface.rs` | `atoms/Surface` | Frosted, HUD and inset variants available |
| `ui/typography.rs` | `atoms/Text` | Style tokens mapped; **keep Winky Rough Variable**, not MineClone's font |
| `ui/screen.rs` | `templates/ScreenShell` | Header/body/footer layout available |
| `ui/cosmic_background.rs` | `organisms/CosmicBackground` | Available |
| `ui/selectable.rs` | `atoms/Button`, `atoms/Select`, `molecules/GameModePicker` | Selection state via React props; no duplicate Bevy interaction state |
| `ui/dropdown.rs` | `atoms/Select` | Native/selectable semantic control exists; full custom dropdown visual/menu positioning still differs |
| `ui/numeric_input.rs` | `atoms/TextInput`, `molecules/NumericStepper` | Numeric field and −/+ stepping available, with explicit commit callbacks |
| `ui/slider.rs` | `atoms/Slider` | Track/thumb and keyboard interaction available |
| `ui/toggle.rs` | `atoms/Toggle` | Geometry and switch semantics available |
| `ui/scrollbar.rs` | Scrollable page containers | Currently browser scrollbar styling; authored scrollbar visuals pending |
| `ui/settings.rs` | `molecules/SettingRow`, `pages/SettingsPage` | Section layout and shared setting rows available |
| `ui/transition.rs` | Controller + `UiStore` routing | No equivalent visual transition animation yet |
| `ui/visibility.rs` | React conditional rendering | Available; global/gameplay input remains with Godot |
| Controls key tags | `atoms/KeyCap`, `molecules/ControlBindingEntry` | Fixed/editable/capturing states available |

## Screen parity

| MineClone | Asteria | Present | Remaining |
| --- | --- | --- | --- |
| Starting | `StartingScreenPage` | Brand, cosmic background, actions | Version label and transitions |
| World Selection | `WorldSelectionPage`, `WorldCard` | Real read-only catalog, empty/error states | Full save restore, thumbnails, load/delete |
| New World via Settings | `NewWorldPage` + shared `SettingsPage` | Sidebar sections, name, seed, mode buttons, ticks stepper, footer actions, pending/error/generating | Authored generation options and remaining rules only after corresponding Asteria runtime support |
| Settings | `SettingsWorkspacePage`, `SettingsPage` | Graphics, HUD, language, world settings, game rules with scrollable sections | Keybinds inside Settings, additional authored game rules |
| Controls | `ControlsPage`, `ControlBindingEntry` | Movement/interface/actions, actual editable bindings, capture states | Chat/perspective/pick-block and any unsupported controls remain excluded until implemented |
| Pause | `PauseMenuPage` | Resume, settings, controls, exit | Leave World vs Exit semantics and feedback require persistence |
| Loading | `LoadingOverlay` | Real runtime loading progress | Visual calibration and states |
| Game HUD | `GameHudPage`, HUD atoms/molecules/organisms` | Hotbar, target, crosshair, clock, world banner, toasts, vitals | Compare MineClone spacing, breakpoints, presentation and status effects |
| Inventory / Crafting | Shared `InventoryWorkspace`, `InventoryGameplayPage` and `InventoryPage`; `InventoryHotbarFooter`, player/creative, character/crafting/station organisms | In-game survival uses three-column Character → Crafting-over-Inventory → Station layout; creative catalog has real hotbar/trash and tabs; metadata comes from runtime | Character 3D portrait/equipment data, recipe and station gameplay bridges, real crafting selection/action; item tooltips and cursor-follow stack icon now exist |
| Storage Box | `StorageBoxPage`, shared `InventoryPanelHeader` and `InventoryHotbarFooter` | Nine-column storage/backpack grid, hotbar, sort/search actions and localized copy; read-only Storybook states | **Not mounted in gameplay**: needs Storage Box authoritative state and transport for operations |
| Chat | `ChatPanel` + `ChatDock`, `ChatController`, Core `PlayerChatSession` | Native configured Chat key, bounded local 64-line history, 10-second feedback, MineClone command grammar and actual `/spawn`, `/locate`, `/warp`, `/kill` runtime capabilities, localized feedback and up/down/Tab suggestions | `/place`, `/modify`, spawn metadata, locate-structure variations, distant same-Sphere warp and parameter ID completion remain pending |
| Character Info | `CharacterInfoPanel` | Shown at inventory left in Survival and uses real HUD health when provided; missing portrait/equipment state is explicit | Wire authoritative equipment and 3D player preview without inventing state |
| Brush Palette | `BrushPalettePage` | Asteria-specific gameplay overlay | Preserve semantic input/bridge ownership; no forced MineClone analogue |

## Implementation policy

- MineClone is a behavioral and visual reference. Do not introduce unsupported world/gameplay capabilities merely to fill a UI slot.
- All reusable components have their own TSX/CSS/Storybook files and controlled/disabled/error states as appropriate.
- World state flows `bridge -> controllers -> UiStore -> React`. Local editable form state is acceptable; no gameplay/global event listeners in WebUI.
- For each subsequent parity slice, inspect the precise MineClone file(s), compare the current Asteria counterpart, implement the component/page changes, and update this matrix.
- Validate both `npm run build` and `npm run build-storybook` before claiming full completion.

## Inventory and crafting parity slice

The MineClone source at `src/hud/inventory/layout.rs` composes Survival as Character Info (320px), Crafting (482px) over Player Inventory, and Current Station (244px), with 24px panel gaps. Creative uses a single creative catalog with category sidebar, nine-column/five-row scroll and a player hotbar with trash action.

Asteria now shares a single `templates/InventoryWorkspace` between the Storybook Inventory page and the **live** `InventoryGameplayPage`. `molecules/InventoryHotbarFooter` owns the reusable nine-slot hotbar/trash presentation, and existing `PlayerInventoryPanel`, `CreativeInventoryPanel`, `CraftingPanel`, `CharacterInfoPanel`, and `CurrentStationPanel` preserve their separate responsibilities.

**Gameplay truth:** inventory slots, creative catalog and inventory mutations come from the existing inventory controller. Character health is read from the authoritative HUD snapshot. There is **no** connected recipe list/craft action, active crafting station data, equipment model or real 3D portrait in this gameplay modal; the UI visibly labels those portions unavailable, disables missing action callbacks and does not fabricate recipes, gear or a station. Storybook examples still use fixtures. Pending runtime work must cross the normal bridge/controller/UiStore boundary.

## Inventory tooltip, pointer, storage and chat slice

MineClone sources inspected: `src/hud/inventory/layout/item.rs`, `src/hud/inventory/sync.rs`, `src/hud/storage_box.rs`, `src/hud/chat.rs` and `src/hud/chat/visual.rs`.

- `molecules/ItemTooltip` shows localized item name, canonical ID and existing metadata in a portal to avoid slot-grid overflow clipping. `InventorySlot` positions it only during explicit UI control hover/focus, clamps to the viewport and never attaches global input listeners.
- `molecules/InventoryCursorOverlay` displays the actual inventory cursor stack icon and quantity, repositioned by pointer movement **inside** the open `InventoryGameplayPage` only. Its movement is purely presentation; Godot/Core continue to own pickup/place/swap. The fixed old cursor preview card was replaced.
- `molecules/InventoryPanelHeader` is shared by the player inventory and `StorageBoxPage`; the latter also reuses `InventoryHotbarFooter` without an invented trash action. Storage search disables nonmatching slots without erasing occupied slots.
- `ChatPanel` now matches MineClone's 500px-wide, bottom-left chat frame, 330px/15-line history, 64 latest messages and centered seven-result autocomplete viewport. The Chat is now mounted through the real native-key -> Godot -> Core -> WebUI bridge, with a narrow local command interpreter.
- Storage remains **presentation-only** until an authoritative Storage Box subsystem and UI message handlers are implemented; absent callbacks disable actions.
- The Winky Rough Variable family is unchanged. No gameplay/global browser listeners were introduced. Existing Storybook fixture data remains isolated from live gameplay.

## Live local Chat bridge (2026-10-08)

- Core `PlayerChatSession` is the sole transcript owner: 64 bounded lines, monotonically increasing IDs, 256-character trimmed input and explicit open/close. `PlayerChatCommandProcessor` parses the exact MineClone rebuild command set: `/spawn`, `/place`, `/locate`, `/warp`, `/kill`, `/modify`, including authored grammar, coordinate order X Z Y, optional arguments and usage feedback. The invented `/help`, `/position`, `/time` commands were removed. Ordinary text is **local echo only**, not network messaging.
- `FpsPlayer.ChatRequested` uses the **existing configurable Chat key** from `ClientPreferences` (default T). The Godot `Main` composition root enters/exits a modal, suspends gameplay input, publishes `game.chat.state` and handles semantic `ui.chat.submit`/`ui.chat.close` calls. World coordinates and clock are read from the current authoritative runtime. Closing restores normal mouse capture.
- WebUI `ChatController` validates the state boundary, `UiStore.chat` holds a readonly presentation snapshot, and `ChatDock` owns only the text draft and focused autocomplete keyboard controls. `ChatPanel` localizes server feedback; browser-level gameplay keys and command mutation are prohibited.
- Messages fade after 10 seconds of closed-chat activity, but the full Core transcript remains available when reopening.
- `StorageBoxPage` still lacks an authoritative Core storage owner or active block/container interaction. Its Storybook fixtures cannot imply a functional container.

## MineClone command parity correction (2026-10-08)

Reference source: `mineclone/src/hud/chat/commands.rs` and `autocomplete.rs` on `world-systems-rebuild`. Previous local-only `/help`, `/position`, `/time` were **not** MineClone commands and have been removed from parser, autocomplete and translations.

| Original command | Asteria state | Notes |
| --- | --- | --- |
| `/spawn <id> [meta_tag]` | Real for MineClone tags | Both `NO_AI` and `PERSISTENT` accepted; immutable metadata persists across Sphere state snapshots. `NO_AI` stops ordinary movement/damage, `PERSISTENT` prevents distance despawn |
| `/place structure <id> [variation]` | Partial, real | Places standalone structures and group variants fitting authored terrain, accepted generated conflict policy and loaded voxels through `VoxelMutationRuntime`; connector graphs and StructureSets are explicitly unsupported, never silently truncated |
| `/locate biome <id>` | Real | Searches only an authored active surface biome; work runs off-thread with stale-Sphere rejection |
| `/locate structure <id> [variation]` | Real for authored groups | Resolves a numbered group member and filters accepted generated placements via `SurfaceStructureField` without bypassing conflict resolution. Explicit variation on StructureSet is unsupported |
| `/warp <x> <z> <y> [dimension]` | Real with safety limits | Distant same-Sphere warp reenters through drained archival and normal loading; checks saved voxel occupancy and seeks safe resident entry before releasing player, with authored spawn fallback if destination was edited beyond safe recovery |
| `/kill` | Real | Uses player target ray, authoritative CreatureRuntime death transition, loot and animation |
| `/modify <add|remove|edit> <meta_tag> [value]` | Real for MineClone tags | `NO_AI` and `PERSISTENT` use immutable typed per-creature metadata; `add/remove/edit`, including optional string values, preserve state and reject unknown tags |

All command names are present in the grammar and **contextual autocomplete** (creature IDs, active biomes, structures/groups, group variations, Spheres, current X/Z/Y, NO_AI/PERSISTENT); unsupported operations report `chat.command.notImplemented`. The UI must never report them as successful. **The full command port is not complete.** The parser is Core-owned with dedicated regression tests; commands call the existing content, generator and creature runtimes.

## Contextual autocomplete and NO_AI

- `ChatDock` uses a pure caret-aware `chatAutocomplete.ts` function to complete a token under the cursor (not the full draft). Supported argument categories match MineClone: command, creature ID, metadata tag, modify verb, structure literal/reference/variation, locate kind, biome, coordinate and Sphere.
- The Godot chat snapshot sends bounded, authoritative lists from current pack registries and player position. `ChatController` validates the complete catalog before presenting it; React never searches the world or invents definitions.
- `CreatureRuntime` owns the initial and mutable `NO_AI` tag: spawn with `NO_AI`, `/modify add NO_AI`, `/modify remove NO_AI`. The flag survives Sphere snapshots and suppresses ordinary movement and damage. The only MineClone tags on this branch are `NO_AI` and `PERSISTENT`; both and their optional edit values are now ported.
- The query API `SurfaceStructureField.FindNearest` optionally filters accepted candidate pieces by a specific member ID and preserves existing placement/conflict semantics. The asynchronous locate controller rejects stale results from retired Spheres. Group variations resolve in deterministic authored ID order.
- `/place` in the MineClone rebuild reference is itself marked temporarily unavailable; the Asteria equivalent remains unimplemented rather than creating a second procedural world writer. Similarly, unloaded same-Sphere distant `/warp` must acquire residency/prepare destinations first.

## Distant reentry and explicit placement

Distant same-Sphere `/warp` does not teleport into unloaded chunks: `DimensionSessionController.RequestRelocation` performs the same cooperative drain/archive/restore loading sequence used by dimension travel. `ResidentWarpDestinationQuery` validates loaded and edited voxels, fluid, solid footing and a two-voxel head clearance; a blocked target is resolved deterministically nearby or redirected via the normal generated-spawn loading path. This deliberately favors safety over instant traversal.

`/place structure` supports bounded manual placement of independent authored structures (or a selected group member). `SurfaceStructureField.TryPrepareManualPlacement` reuses the authored ground, biome, generated-fluid, vertical bounds and generated-structure conflict logic. `ManualStructurePlacementRuntime` validates all requested cells are loaded and mutable, observes `AirOnly` and player collision safety, then delegates block, microblock/surface-state, fluid and clear operations to the canonical `VoxelMutationRuntime`. Maximum initial payload is 4096 voxels within a 64×64 horizontal footprint. StructureSets, connectors and compound multi-piece placement are explicitly rejected, rather than partially applying content. Since the MineClone rebuild's own placement method is currently a stub, this is new Asteria behavior and is not falsely described as complete MineClone parity.
