# MineClone screen parity

Reference: `sarakborges/mineclone` at `world-systems-rebuild`; inspect `src/screens` and `src/ui` before porting each screen. Preserve React/Atomic Design, Godot bridge/input ownership, pack-authored UI and the **Winky Rough Variable** font.

| Screen | Asteria baseline | Remaining parity |
| --- | --- | --- |
| Starting | Original MineClone logo, four centered menu actions, cosmic background and shared language selector | Authoritative game-version caption; Play currently opens New World |
| World selection | `WorldSelectionPage` and `WorldCard` accessible via Starting > Play, with a truthful unavailable state | Real save catalog, thumbnails, load/delete/open folder remain pending backend. Storybook worlds are fixtures only. |
| New world | `NewWorldPage`, entered from World Selection; Back returns to selection | Layout, validation and creation progress |
| Pause menu | `PauseMenuPage` | Verify exit/leave world behavior against save-session backend |
| Game/world settings & controls | `SettingsWorkspacePage`, `ControlsPage` | Hierarchy, controls, keybinding and navigation states |
| Inventory/crafting | `InventoryGameplayPage`, `InventoryPage` | Panel geometry, recipes, creative/survival, interactions |
| HUD/loading | `GameHudPage`, `LoadingOverlay` | Visual hierarchy and true progress |

Each screen must retain injectable semantic actions, shared design-system components, localized copy and colocated Storybook states, including disabled/busy/empty states where applicable. Validate `npm run build` and `npm run build-storybook`. Never capture global/gameplay input in WebUI.
