# MineClone screen parity

Reference: `sarakborges/mineclone` at `world-systems-rebuild`; inspect `src/screens` and `src/ui` before porting each screen. Preserve React/Atomic Design, Godot bridge/input ownership, pack-authored UI and the **Winky Rough Variable** font.

| Screen | Asteria baseline | Remaining parity |
| --- | --- | --- |
| Starting | Original MineClone logo, four centered menu actions, cosmic background and shared language selector | Authoritative game-version caption; Play now opens World Selection |
| World selection | `WorldSelectionPage` and `WorldCard` on Starting > Play, with bounded asynchronous filesystem catalog and real Open Saves Folder action | On-disk world snapshot writer/restore and load/delete/thumbnail behavior still pending. Incompatible world candidates cannot load; Storybook entries are fixtures only. |
| New world | Shared MineClone-style `SettingsPage` sidebar and scrolling sections: world name, game mode buttons, seed, tick stepper, three footer actions, validation/generating states | Remaining authored generation options/game rules still depend on runtime capabilities |
| Pause menu | `PauseMenuPage` | Verify exit/leave world behavior against save-session backend |
| Game/world settings & controls | Shared scrollable sections, reusable game mode and numeric-stepper controls, categorized Controls screen with current keybinds | More game rules, chat/perspective/other unsupported controls, visual transitions |
| Inventory/crafting | Shared `InventoryWorkspace` now mounts on the actual gameplay modal: Character left, Crafting above Inventory center, Current Station right in Survival; creative catalog + real hotbar/trash and tabs | Recipe selection/crafting, player portrait/equipment, station data, item tooltips and pointer-follow cursor need their authoritative gameplay integration |
| HUD/loading | `GameHudPage`, `LoadingOverlay` | Visual hierarchy and true progress |

Each screen must retain injectable semantic actions, shared design-system components, localized copy and colocated Storybook states, including disabled/busy/empty states where applicable. Validate `npm run build` and `npm run build-storybook`. Never capture global/gameplay input in WebUI.

## Disk catalog contract (current slice)

The runtime scans `OS.GetUserDataDir()/worlds` off-thread on UI entry and at WebUI readiness. Only immediate, non-symlink directories containing `world.json` are considered candidates. The provisional manifest reader accepts `formatVersion: 1`, matching directory `name`, decimal-string `seed`, `dimensionId`, `day`, `lastSavedUnixMs`, and optional `playerPosition` (X/Y/Z). The scan is bounded to 256 directories and 32 KiB per manifest, with deterministic results and explicit errors.

**This is read-only discovery, not a save format implementation.** No serializer or restorer writes/loads Asteria worlds yet. Every candidate remains non-restorable, including valid metadata; load/delete stay disabled. World creation still starts an in-memory session, and exiting still does not persist the world. Do not create a metadata-only "save" or enable Load/Delete until a full snapshot, restoration, and rollback-safe persistence contract exists. Opening the folder is the only enabled file action.

A complete primitive-by-primitive and screen-by-screen backlog now lives in [ui-component-parity.md](ui-component-parity.md); screen parity must progress across HUD, inventory/crafting, storage, chat, and character info without shifting scope to save-system development.

## Tooltip, inventory pointer, Storage Box and Chat

- Runtime inventory: the cursor stack now follows the pointer only while inside the inventory UI surface. Slot tooltips show content name, canonical ID and authored/runtime metadata and are clamped inside the viewport. Godot still owns global gameplay input.
- Shared inventory/search components: `ItemTooltip`, `InventoryCursorOverlay`, `InventoryPanelHeader`; `PlayerInventoryPanel` and `StorageBoxPage` reuse the header, and Storage reuses `InventoryHotbarFooter` without trash.
- Storage Box and Chat visual parity is improved and covered by Storybook, **but neither is mounted to the live Godot gameplay bridge**. Storage sorting/mutations and chat submission/autocomplete must first have authoritative runtime state and explicit semantic bridge commands. Absent callbacks are noninteractive; Storybook examples are not runtime data.
- Chat presentation follows the MineClone 500px bottom-left frame, 64-message bounded history and seven-suggestion selection window. The visual shell does not implement the chat command engine.
- Remaining UI fronts: tooltip tool stats once runtime supplies data, player portrait/equipment, recipes/station, chat and storage runtime wiring, HUD visual audit.
