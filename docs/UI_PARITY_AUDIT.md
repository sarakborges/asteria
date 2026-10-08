# MineClone → Asteria WebUI parity audit

**Audit date:** 2026-10-08  
**Asteria source snapshot:** `cab6017dfbcef8f20cabf79566ea69d736618d22` (`main`)  
**MineClone reference snapshot:** `e8bc8562abc22eb4a54561c1e7afe8cc086e3dd9` (`world-systems-rebuild`)

## Scope and evidence standard

**Runtime discrepancy (2026-10-08):** User reports Create New World showing no left navigation, no World Settings and no Game Rules. Source inspection shows `NewWorldPage` builds two `SettingsSectionView`s and `SettingsPage` has a `.settings-page__navigation` node. This means **source parity and the actual embedded build disagree**. `WebUiHost.gd` loads `res://ui/dist/index.html` and uses modification-time stale detection. The embedded bundle/version and actual runtime DOM require verification; do not assert this is caused by cache without reproducing it. A Storybook `play` DOM guard was added for the sidebar and both panels, but it does not replace the in-game check.


Source audit comparing MineClone `src/screens/**`, `src/hud/**`, and `src/ui/**` to Asteria `ui/src/components/{atoms,molecules,organisms,templates,pages}/**`, `ui/src/App.tsx`, state/controllers and relevant Godot adapters. The Asteria component subtree has **63 component directories**, with a colocated Storybook story in every directory: 13 atoms, 15 molecules, 19 organisms, 4 templates and 12 pages.

**This is not a pixel-level screenshot or in-game behavior test.** “Visually scaffolded” means structural primitives and styles exist, *not* verified 1:1 rendering. “Wired” is reserved for visibly connected controllers/authoritative messages. Never infer a runtime producer from a nullable UI field alone. MineClone is the reference, with intentional Asteria divergences explicitly preserved (React/Godot boundaries, Winky Rough Variable, Sphere naming, additional biome label, inventory-over-crafting order).

Statuses: **MATCHING FOUNDATION** (main geometry or semantics correspond, manual screenshot QA still needed), **PARTIAL** (real differences), **PRESENTATION ONLY** (no runtime binding), **ASTERIA EXTENSION** (not an expected MineClone port), and **NOT APPLICABLE** (engine-specific implementation with a better equivalent).

## Screens — inspected, including already created screens

| MineClone reference | Asteria implementation | Status | Evidence / remaining parity |
| --- | --- | --- | --- |
| `screens/starting_screen.rs` | `pages/StartingScreenPage` | PARTIAL | Main button order/470×54 sizing, centered brand, cosmic background match in intent. MineClone transitions into settings and controls; Asteria gates those actions by availability. Asteria adds a starting-screen language selector still using native `Select`, whereas game settings use the new `Dropdown`. Asteria branding is intentionally distinct. |
| `screens/world_selection.rs` + `world_selection/layout.rs` | `pages/WorldSelectionPage`, `organisms/WorldCard` | PRESENTATION ONLY | Card metadata, 256×144 thumbnail region, 170 px action column and compatible/incompatible treatment represented. **`App.tsx` never supplies `onLoad` / `onDelete`**, leaving actions disabled. `WorldCatalogController` reads summaries but no thumbnail field; `WorldCard.thumbnailUrl` cannot be populated from the current catalog message. Folder open + scan are wired separately. |
| `screens/settings_screen/new_world_section.rs` + `world_name_section.rs` | `pages/NewWorldPage` | **REGRESSÃO VISUAL REPORTADA; QA RUNTIME PENDENTE** | A auditoria anterior falhou ao ignorar ausência de sidebar, World Settings e Game Rules na versão observada pelo usuário. Corrigidos em fonte: ordem Nome → Seed → Game Mode, espaçamento interno 8 px, regras horizontais, botões sem stretch e controle `Spawn Creatures` ligado ao Core. **A presença no TSX/Storybook não comprova renderização no WRY.** Verificar bundle embutido e screenshots antes de classificar como pareada. |
| `screens/settings_screen/layout.rs` + six sections | `pages/SettingsPage`, `pages/SettingsWorkspacePage` | PARTIAL | 280 px sidebar, 22 px columns, 42 px section gaps and scrolling largely correspond. Game settings omit MineClone's integrated **Keybinds** section (Asteria has a separate Controls page instead). World rule section now includes runtime-backed `Spawn Creatures`; embedded viewport parity remains unverified. Basic HUD hints, target position, render distance and language selectors exist. |
| `screens/controls_screen.rs` | `pages/ControlsPage` + `presentation/controlGroups.ts` | PARTIAL | Cards, keycaps, editable bindings and categories exist. Missing from the listed guide: **W ×2 Run**, **Change Perspective**, **MMB Pick Block**, and dedicated **Chat** group with **Enter**, **↑/↓ history**, **Tab autocomplete**. Do not invent native input commands; display only actual Asteria bindings/actions when implemented. |
| `screens/pause_menu.rs` | `pages/PauseMenuPage` | PARTIAL | Centered 360 px actions, paired settings controls and hierarchy match. **`App.tsx` does not provide `onLeaveWorld` or `saveFeedback`**; leave stays disabled and save-failure feedback is absent. The reference uses a shared transition for pause/settings/controls; Asteria does not currently fade gameplay overlays. |
| `screens/loading_screen.rs` | `organisms/LoadingOverlay` | MATCHING FOUNDATION | Cosmic background, 560 px frosted card, 18 px gaps, 12 px progress bar present. Asteria deliberately uses real `WorldLoadingState` progress and additional Sphere label. No fake loading timers should be introduced. |

**Important distinction:** MineClone's editable world-name field belongs to **new-world creation**, not the current in-world World Settings layout. Do not add it in-world just to reach superficial feature count.

## Inventory, crafting, storage and overlays

| Reference | Asteria implementation | Status | Remaining difference |
| --- | --- | --- | --- |
| `hud/inventory.rs` + `inventory/layout/player.rs` | `pages/InventoryGameplayPage` + `organisms/PlayerInventoryPanel` | PARTIAL | 3×9 backpack + nine-slot hotbar, slot/cursor, sort and click through `InventoryController` exist. Search currently disables slots based on **ID substring only**; compare with MineClone's localized inventory search semantics. |
| `hud/inventory/layout/creative.rs` | `organisms/CreativeInventoryPanel` | PARTIAL | Nine-column catalog, search and category choices work. MineClone categories have **authored order, localized display names and category icons**; Asteria constructs category IDs from available catalog entries, sorts them alphabetically and renders text-only buttons. Scroll positions per category/search are not retained as in MineClone's `CreativeScrollState`. |
| `hud/character_info.rs`, `hud/player/portrait.rs` | `organisms/CharacterInfoPanel` | PARTIAL | 320 px panel, 180 px preview region and health bar present. **In game, preview is a fixed “unavailable” label instead of a real 3D character portrait and drag-orbit.** `InventoryGameplayPage` passes `equipment: []`, so even MineClone's *static empty equipment rows* are hidden. This is a visible layout divergence even without implemented equipment gameplay. |
| `hud/crafting.rs` | `organisms/CraftingPanel` + `CurrentStationPanel` | PRESENTATION ONLY | 482 px crafting panel, 218 px recipe list, 58 px rows, result/ingredient states and 244 px station panel are represented. **Runtime page passes `recipes={[]}`, `selectedRecipeId={null}`, `station={null}`** and never supplies `onSelectRecipe` / `onCraft`. No real recipe/station behavior is exposed. |
| `hud/storage_box.rs` | `pages/StorageBoxPage` | PRESENTATION ONLY | Two search/sort headers, 3×9 storage + 3×9 backpack + hotbar are present in React, but **StorageBoxPage is not mounted by `App.tsx`** and no corresponding storage overlay state/controller is available. |
| `hud/inventory/layout/item.rs`, `hud/item_icon.rs`, `hud/tool_icon.rs`, `hud/block_icon.rs` | `atoms/ItemGlyph`, `molecules/InventorySlot`, `ItemTooltip` | PARTIAL | PNG icons and stack counts can render from the current catalog, but the fallback is abbreviated ID text. MineClone provides item/metadata-specific icon overrides (including fluid buckets) and specialized block/world-object presentation. Validate metadata, color/tint, texture variants and localized labels. |
| Asteria-only dye/brush palette | `pages/BrushPalettePage` | ASTERIA EXTENSION | No corresponding MineClone screen to reach 1:1. Audit against Asteria's own design system instead. |

**Deliberate layout difference to retain:** Asteria places Inventory above Crafting, whereas MineClone currently uses the reverse order.

## Gameplay HUDs and world-facing presentation

| MineClone reference | Asteria implementation | Status | Remaining difference |
| --- | --- | --- | --- |
| `hud/crosshair.rs` | `atoms/Crosshair` + `molecules/InteractionPrompt` | MATCHING FOUNDATION | 18 px crosshair and semantic action hints exist. Verify exact world-interaction visibility and tool-specific hints in live play; they depend on runtime messages. |
| `hud/hotbar.rs` | `organisms/Hotbar` + `molecules/HotbarSlot` | MATCHING FOUNDATION / PARTIAL | Nine 44 px slots, 4 px gaps and selected borders match. **Selected item name is resolved by item ID**, not the MineClone metadata-aware `stack_display_name` (e.g. bucket fluid). |
| `hud/world/*` | `organisms/WorldBanner` + `molecules/Compass` | MATCHING FOUNDATION | 520 px banner, 360×30 compass, 15-degree markers and world coordinates exist. Sphere and biome label are intentional Asteria adaptation. Verify camera-perspective heading in gameplay when perspective switching exists. |
| `hud/entity_card.rs`, `hud/player.rs` | `organisms/HudEntityCard`, `PlayerVitals` | PARTIAL | 64 px avatar + 180 px details and 22 px health bar present. **Local player portrait still falls back to “?”**, instead of MineClone's rendered player portrait; Asteria displays generic localized player name rather than MineClone's explicit name. Stamina is an intentional Asteria extension. |
| `hud/entity_targeting.rs` + portrait | `HudEntityCard` via `state.hud.targetEntity` | PARTIAL | `Main.cs` **does** publish `game.hud.target_entity` from actual target snapshots. Correcting the prior inaccurate claim that no producer exists. The WebUI portrait remains optional/fallback and must be compared to MineClone's render-texture portrait in gameplay. |
| `hud/targeting.rs` | `organisms/TargetHud` | PARTIAL | Kind/name/details, position and mining progress exist. **Target block/object icon is abbreviated ID text**, not the MineClone 34 px icon/model/tinted target preview. |
| `hud/time.rs` | `organisms/WorldClock` | MATCHING FOUNDATION | Day/time component and runtime clock updates exist. |
| `hud/fps.rs` | `atoms/FpsCounter` | MATCHING FOUNDATION | 250 ms sampling is tracked by Godot and published to WebUI; same fundamental presentation contract. |
| `hud/chat.rs` + `chat/visual.rs` | `organisms/ChatDock`, `ChatPanel` | PARTIAL | 500 px panel, 330 px/15-line viewport, up to 64 historical entries, seven autocomplete rows and 256-char input. **`ChatPanel` supports clickable message actions but `ChatDock` never passes `onMessageAction`**, so structure-file/warp-link UI actions are disabled. Backend command parity is tracked separately from screen parity. |
| `hud/fluid_immersion.rs` | `Asteria.Godot/Rendering/UnderwaterViewPresentation.cs` | NOT APPLICABLE (adapter equivalent) | Asteria already applies fluid underwater presentation through a Godot overlay shader, not React. **Do not report the whole immersion effect as missing.** Compare tint/opacity per fluid in gameplay. |
| Status effects, toasts, debug overlay | `StatusEffects`, `ToastStack`, `StatusCard` | ASTERIA EXTENSION | These are legitimate extra Asteria presentation surfaces; ensure they do not conflict with MineClone HUD spatial layout. |

## Shared UI primitives — complete MineClone src/ui comparison

| MineClone `src/ui/` | Asteria equivalent | Status |
| --- | --- | --- |
| `button.rs` | `atoms/Button` | MATCHING FOUNDATION; verify hover/pressed/disabled states visually |
| `cosmic_background.rs` | `organisms/CosmicBackground` | MATCHING FOUNDATION |
| `dropdown.rs` | `atoms/Dropdown` | PARTIAL: settings uses custom panel; starting language still uses native `Select` |
| `numeric_input.rs` | `atoms/NumericInput`, `molecules/NumericStepper` | PARTIAL: tick-rate editor covered; seed uses generic `TextInput`; uint64 seed needs string-safe validation |
| `screen.rs` | `templates/ScreenShell` | MATCHING FOUNDATION (1120 px content; 116/104 px header/footer) |
| `scrollbar.rs` | `molecules/ScrollArea` | PARTIAL: settings migrated; Chat, Controls, World Selection and Creative catalog/category use independent native/CSS scroll regions |
| `selectable.rs` | Selection CSS in `Button`, slots, recipe/category panels | PARTIAL: multiple independent hover/active/pressed treatments; ensure all follow MineClone token-state mapping |
| `settings.rs` | `SettingRow`, `SettingsPage`, tokens | MATCHING FOUNDATION |
| `slider.rs` | `atoms/Slider` | MATCHING FOUNDATION; check focus, step and value display |
| `surface.rs` | `atoms/Surface` | MATCHING FOUNDATION |
| `text_input.rs` | `atoms/TextInput` | MATCHING FOUNDATION; verify committed/focused/error states |
| `theme.rs` | `ui/src/styles/tokens.css` + pack UI tokens | MATCHING FOUNDATION; preserve pack override support |
| `toggle.rs` | `atoms/Toggle` | MATCHING FOUNDATION |
| `transition.rs` | `templates/ScreenTransition` | PARTIAL: 160 + 160 ms pre-world only; MineClone also uses transitions from pause/settings/controls |
| `typography.rs` | `atoms/Text` and Winky Rough Variable | INTENTIONAL ADAPTATION: **do not replace the font family** |
| `visibility.rs` | Conditional rendering + CSS and state-driven view selection | NOT APPLICABLE as a standalone Bevy abstraction |

## Prioritized correction batches

1. **P0 — misleading or inert screens:** wire world load/delete only after actual save/catalog owner supports them; wire Pause “Leave World”/save feedback to session owner; mount storage only with real storage snapshots; wire crafting only through authoritative recipes/stations.
2. **P1 — incomplete controls/settings:** add the actual `Spawn Creatures` world rule if authoritatively supported, restore missing Controls guidance with real bindings, and keep keybind editing in Godot (never global browser input). Align seed editor with MineClone's strict digit behavior without unsafe JS 64-bit conversion.
3. **P1 — visually obvious HUD gaps:** player preview and drag orbit, stable empty equipment frames, player/creature portraits, target block/object icons, metadata-sensitive selected hotbar label.
4. **P2 — inventory Creative details:** authored category ordering, icons, localization, persistent category/catalog scroll position, name-based search and variant/tooltips.
5. **P2 — visual component completion:** common overflow-aware scrollbars in Chat/Controls/World Selection/Creative/Crafting; compare game-overlay transitions with Godot input constraints; unify selected/hover/pressed tokens; replace starting-screen native select or explicitly document the intentional difference.
6. **P3 — screenshot and runtime QA:** fixed viewport captures (e.g. 1920×1080 and 1280×720) for every screen/HUD in both projects, including narrow viewport, empty/full/error/disabled/focus/hover and keyboard navigation. Storybook build success alone is not 1:1 parity.

## Invariants

- Preserve `Winky Rough Variable`, Atomic Design, Storybook and React → store → controller → bridge boundaries.
- Never let WebUI own gameplay keyboard/mouse input or invent runtime data/progress.
- Preserve explicit Asteria differences already agreed upon (Sphere terminology, additional biome label, Survival inventory above Crafting, Godot immersion shader).
- During parallel command/world work, re-check `main` and the exact affected files before implementing a batch; this audit is a **source snapshot**, not a lock on ongoing development.
