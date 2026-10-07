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

## First migrated screen

`NewWorldPage` consumes the migrated design-system primitives. It deliberately exposes only controls backed by Asteria's current world-creation contract rather than inventing unsupported MineClone settings.

## Next screen/component families

1. Starting screen.
2. World selection.
3. Settings + controls.
4. Pause menu.
5. Inventory + creative inventory.
6. Crafting.
7. Remaining HUD presentation details.

Each screen must reuse migrated primitives instead of introducing page-local copies of button, surface, input or screen-shell styling.
