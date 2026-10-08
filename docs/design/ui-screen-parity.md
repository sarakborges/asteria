# MineClone screen parity

Reference: `sarakborges/mineclone` at `world-systems-rebuild`; inspect `src/screens` and `src/ui` before porting each screen. Preserve React/Atomic Design, Godot bridge/input ownership, pack-authored UI and the **Winky Rough Variable** font.

| Screen | Asteria baseline | Remaining parity |
| --- | --- | --- |
| Starting | Original MineClone logo, four centered menu actions, cosmic background and shared language selector | Authoritative game-version caption; Play now opens World Selection |
| World selection | `WorldSelectionPage` and `WorldCard` on Starting > Play, with bounded asynchronous filesystem catalog and real Open Saves Folder action | On-disk world snapshot writer/restore and load/delete/thumbnail behavior still pending. Incompatible world candidates cannot load; Storybook entries are fixtures only. |
| New world | `NewWorldPage`, entered from World Selection; Back returns to selection | Layout, validation and creation progress |
| Pause menu | `PauseMenuPage` | Verify exit/leave world behavior against save-session backend |
| Game/world settings & controls | `SettingsWorkspacePage`, `ControlsPage` | Hierarchy, controls, keybinding and navigation states |
| Inventory/crafting | `InventoryGameplayPage`, `InventoryPage` | Panel geometry, recipes, creative/survival, interactions |
| HUD/loading | `GameHudPage`, `LoadingOverlay` | Visual hierarchy and true progress |

Each screen must retain injectable semantic actions, shared design-system components, localized copy and colocated Storybook states, including disabled/busy/empty states where applicable. Validate `npm run build` and `npm run build-storybook`. Never capture global/gameplay input in WebUI.

## Disk catalog contract (current slice)

The runtime scans `OS.GetUserDataDir()/worlds` off-thread on UI entry and at WebUI readiness. Only immediate, non-symlink directories containing `world.json` are considered candidates. The provisional manifest reader accepts `formatVersion: 1`, matching directory `name`, decimal-string `seed`, `dimensionId`, `day`, `lastSavedUnixMs`, and optional `playerPosition` (X/Y/Z). The scan is bounded to 256 directories and 32 KiB per manifest, with deterministic results and explicit errors.

**This is read-only discovery, not a save format implementation.** No serializer or restorer writes/loads Asteria worlds yet. Every candidate remains non-restorable, including valid metadata; load/delete stay disabled. World creation still starts an in-memory session, and exiting still does not persist the world. Do not create a metadata-only "save" or enable Load/Delete until a full snapshot, restoration, and rollback-safe persistence contract exists. Opening the folder is the only enabled file action.
