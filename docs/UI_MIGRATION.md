# WebUI MineClone migration

This document tracks the deliberate migration of MineClone's UI presentation contracts into Asteria's React WebUI.

## Non-negotiable adaptation

MineClone uses `SystemUi` in its Bevy typography helpers. Asteria does **not** port that font choice. Asteria keeps its current `Winky Rough Variable` family unless the user explicitly requests a typography-family change.

The migration ports layout, color, surface, spacing, control, interaction and hierarchy contracts while keeping Asteria's WebUI architecture:

`Godot bridge -> controllers -> UiStore -> React`

## Reference branch

MineClone reference: `sarakborges/mineclone`, branch `world-systems-rebuild`.

## Ported foundation

- `src/ui/theme.rs` -> pack-driven CSS color/surface tokens.
- `src/ui/typography.rs` -> React `Text` variants, preserving Winky Rough.
- `src/ui/button.rs` -> `Button` variants (`normal`, `primary`, `danger`) and 44/54 px control heights.
- `src/ui/surface.rs` -> `Surface` variants with frosted/HUD/inset/elevated presentation.
- `src/ui/text_input.rs` -> `TextInput` focus/border/inset contract.
- `src/ui/cosmic_background.rs` -> `CosmicBackground`.
- `src/ui/screen.rs` -> `ScreenShell` 1120 px content/header/body/footer layout.
- `src/ui/settings.rs` -> shared settings gaps/tokens.
- `src/ui/numeric_input.rs` -> digit-only `NumericInput` editor (Escape cancels, Enter/blur validates); `NumericStepper` reuses it rather than browser number controls.

## First migrated screen

`NewWorldPage` consumes the migrated design-system primitives. It deliberately exposes only controls backed by Asteria's current world-creation contract rather than inventing unsupported MineClone settings.

## Screen migration status

- Starting screen: **ported and wired**. Play enters Asteria's existing world-creation flow; Exit crosses the bridge as `ui.app.exit`. Settings/Controls buttons stay disabled until authoritative runtime owners exist.
- World selection: **presentation ported**. Cards, metadata, load/delete actions, empty/error states and screen layout are available in React/Storybook; runtime wiring intentionally waits for disk save/catalog ownership.
- Settings: **presentation ported**. Navigation, section cards, `Toggle`, `Select`, `Slider` and `SettingRow` are available; runtime wiring waits for authoritative settings ownership.
- Controls: **presentation ported**. Group/card/keycap layout is available; editable keybind behavior waits for a real keybind owner.
- Pause menu: **presentation ported**. The 360 px centered menu, paired settings actions, leave/exit hierarchy and save-feedback area match the MineClone contract. Runtime pause/resume/leave wiring intentionally waits for a real pause/save owner.
- Inventory: **presentation ported**. Character Info, 3×9 backpack, hotbar footer, search/sort/trash controls and 40 px slot system are reusable React components.
- Creative inventory: **presentation ported**. Category rail, search field, 9-column catalog and Inventory/Creative view tabs are available.
- Crafting: **presentation ported**. Available Recipes, Selected Recipe, result/ingredient states, craftability, Current Station and the MineClone sizing contracts are available.

### Inventory layout adaptation

Asteria deliberately orders the survival center column as **Inventory → Crafting** instead of MineClone's current **Crafting → Inventory** order. This keeps Character Info on the left and Current Station on the right top-aligned with Inventory, while Crafting sits directly below Inventory with the normal panel gap. `Available Recipes`, `Selected Recipe`, and `Current Station` use equivalent heading hierarchy, and Current Station is always rendered as a surfaced card.

## HUD migration status

- Crosshair/action hint: **ported/adapted** to MineClone's 18 px crosshair and lightweight hint placement.
- Hotbar: **ported** to the 44 px slot, 4 px gap, 4 px row padding and selected-state contract. Asteria keeps its existing selected-item name.
- World HUD: **ported/adapted** to the 520 px banner and 360×30 compass. Asteria deliberately keeps the current biome label as an extra presentation line.
- Player HUD: **ported/adapted** to the shared 64 px entity card + 180 px info area. Asteria may additionally show stamina when an authoritative runtime value exists.
- Target HUD/entity target HUD: **presentation ported**. Optional typed bridge messages are supported, but the WebUI does not invent target data.
- World clock and FPS: **presentation ported**. They remain hidden until authoritative runtime messages exist.
- Status effects and toast stack: **retained Asteria extensions** using the migrated design-system tokens.
- Chat: **presentation ported** with the MineClone 500 px history panel, 15-line history cap, autocomplete surface and input contract. Runtime/chat-command ownership is not invented in WebUI.
- Storage Box: **presentation ported** with the 3×9 storage grid, search/sort controls, 3×9 player backpack and hotbar row. Runtime storage ownership remains outside WebUI.
- Loading screen: **presentation ported** to MineClone's cosmic background, 560 px frosted card, 18 px gaps and 12 px progress bar. Its progress and phase remain exclusively supplied by Asteria's authoritative `WorldLoadingState`.

## Migration status

Major MineClone screens, modals, inventory/crafting and HUD surfaces have been migrated, but reusable-control and transition parity is not yet complete.

## Remaining presentation differences

- `src/ui/dropdown.rs`: Asteria's `Select` uses a native browser menu, not MineClone's 44 px anchored option panel, selected/hovered states or 40 px option rows.
- `src/ui/scrollbar.rs`: scroll containers currently use CSS scrollbar styling; MineClone's auto-overflow visibility and 8 px track / minimum 28 px thumb are not yet consistently applied.
- `src/ui/transition.rs`: MineClone's 160 ms fade-out + fade-in transition is not yet represented by a shared WebUI screen transition. Keep this presentation-only; navigation ownership stays in controllers.
- Runtime-backed screens marked presentation-only above still need their authoritative gameplay owners before activation. Never invent save, inventory, pause or command behavior merely to make a screen clickable.

## Runtime integration status

- FPS HUD: **wired**. Godot owns the 250 ms frame-sampling presentation tracker and publishes only changed values.
- Block target HUD + action hint: **wired**. The existing authoritative voxel raycast is shared by block interaction and HUD projection; WebUI receives only a changed presentation snapshot and never performs targeting.
- Target entity metadata: presentation remains ready, but no creature/entity-targeting owner exists in Asteria yet.
- World clock: **wired** to the authoritative per-Sphere `DayNightClock`. `game.hud.clock` publishes changes of minute/day and an initial snapshot. The full day/night runtime is documented in `docs/DAY_NIGHT.md`; shader/voxel lighting changes are explicitly excluded.
- Save catalog, settings/keybinds, pause/session save, inventory/crafting/storage/chat remain pending their authoritative runtime owners.

Remaining work is runtime integration, not additional presentation copying.

Each screen must reuse migrated primitives instead of introducing page-local copies of button, surface, input or screen-shell styling.
