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
| Game HUD | `GameHudPage`, HUD atoms/molecules/organisms | Hotbar, target, crosshair, clock, world banner, toasts, vitals | Compare MineClone spacing, breakpoints, presentation and status effects |
| Inventory / Crafting | `InventoryGameplayPage`, `InventoryPage`, inventory/crafting organisms | Existing presentation structure | Check recipe navigation, selection and creative/survival parity against MineClone `src/hud/inventory` and `src/hud/crafting.rs` |
| Storage Box | `StorageBoxPage` | Component exists | Audit game integration and interaction parity with `src/hud/storage_box.rs` |
| Chat | `ChatPanel` | Component exists | Audit runtime commands, history, completion and screen placement against `src/hud/chat` |
| Character Info | `CharacterInfoPanel` | Component exists | Audit inventory alignment/position, stats and `src/hud/character_info.rs` |
| Brush Palette | `BrushPalettePage` | Asteria-specific gameplay overlay | Preserve semantic input/bridge ownership; no forced MineClone analogue |

## Implementation policy

- MineClone is a behavioral and visual reference. Do not introduce unsupported world/gameplay capabilities merely to fill a UI slot.
- All reusable components have their own TSX/CSS/Storybook files and controlled/disabled/error states as appropriate.
- World state flows `bridge -> controllers -> UiStore -> React`. Local editable form state is acceptable; no gameplay/global event listeners in WebUI.
- For each subsequent parity slice, inspect the precise MineClone file(s), compare the current Asteria counterpart, implement the component/page changes, and update this matrix.
- Validate both `npm run build` and `npm run build-storybook` before claiming full completion.
