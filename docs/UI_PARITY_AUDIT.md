# MineClone → Asteria WebUI parity audit

**Original audit date:** 2026-10-08  
**Reviewed update:** 2026-10-09 against Asteria `0346901e15a66430a83b143b903005a28608d36c`; open requirements tracked in [MineClone port remaining](MINECLONE_PORT_REMAINING.md).  
**Correction:** this audit originally compared only MineClone `world-systems-rebuild` and omitted the newer **`main` World Generation section**. The initial parity finding for Create New World was invalid. See [WORLD_CREATION_PARITY.md](WORLD_CREATION_PARITY.md) for the full authoritative comparison.  
**Original Asteria source snapshot:** `cab6017dfbcef8f20cabf79566ea69d736618d22` (`main` at the original audit)  
**MineClone reference snapshot:** `e8bc8562abc22eb4a54561c1e7afe8cc086e3dd9` (`world-systems-rebuild`)

## Scope and evidence standard

**Runtime discrepancy (2026-10-08):** The user's original Godot/WRY screenshot lacked the left navigation and full panels. Chromium tests now assert three sections and sidebar/navigation after implementing World Generation. **This is not a WRY in-game observation**; that verification remains pending.


Source audit comparing MineClone `src/screens/**`, `src/hud/**`, and `src/ui/**` to Asteria `ui/src/components/{atoms,molecules,organisms,templates,pages}/**`, `ui/src/App.tsx`, state/controllers and relevant Godot adapters. The Asteria component subtree has **63 component directories**, with a colocated Storybook story in every directory: 13 atoms, 15 molecules, 19 organisms, 4 templates and 12 pages.

**This is not a pixel-level screenshot or in-game behavior test.** “Visually scaffolded” means structural primitives and styles exist, *not* verified 1:1 rendering. “Wired” is reserved for visibly connected controllers/authoritative messages. Never infer a runtime producer from a nullable UI field alone. MineClone is the reference, with intentional Asteria divergences explicitly preserved (React/Godot boundaries, Winky Rough Variable, Sphere naming, additional biome label, inventory-over-crafting order).

Statuses: **MATCHING FOUNDATION** (main geometry or semantics correspond, manual screenshot QA still needed), **PARTIAL** (real differences), **PRESENTATION ONLY** (no runtime binding), **ASTERIA EXTENSION** (not an expected MineClone port), and **NOT APPLICABLE** (engine-specific implementation with a better equivalent).

## Screens — inspected, including already created screens

| MineClone reference | Asteria implementation | Status | Evidence / remaining parity |
| --- | --- | --- | --- |
| `screens/starting_screen.rs` | `pages/StartingScreenPage` | PARTIAL | Main button order/470×54 sizing, centered brand, cosmic background match in intent. MineClone transitions into settings and controls; Asteria gates those actions by availability. Asteria removed the starting-screen language selector; language remains in Game Settings. Asteria branding is intentionally distinct. |
| `screens/world_selection.rs` + `world_selection/layout.rs` | `pages/WorldSelectionPage`, `organisms/WorldCard` | PARTIAL — LOAD WIRED | `App.tsx` supplies `onLoad`; `WorldLoadController` validates/reconstructs the session asynchronously, and the catalog validates compatibility. **Delete remains unwired**, and full save thumbnail/WRY visual parity is unverified. |
| MineClone `main`: `screens/settings_screen/new_world_section.rs` + `layout.rs` | `pages/NewWorldPage` | **THREE PANELS + CORE WIRING; WRY QA PENDING** | World Settings contains Name/Seed/Game Mode, World Generation contains Normal/Flat/Void, Spawn Biome, size multiplier and four generator toggles, Game Rules contains Ticks and Spawn Creatures. Core generation behavior is per-world and deterministic. Biome labels still need authored localization; in-game screenshot parity is unverified. See `docs/WORLD_CREATION_PARITY.md`. |
| `screens/settings_screen/layout.rs` + six sections | `pages/SettingsPage`, `pages/SettingsWorkspacePage` | PARTIAL | 280 px sidebar, 22 px columns, 42 px section gaps and scrolling largely correspond. Game settings omit MineClone's integrated **Keybinds** section (Asteria has a separate Controls page instead). World rule section now includes runtime-backed `Spawn Creatures`; embedded viewport parity remains unverified. Basic HUD hints, target position, render distance and language selectors exist. |
| `screens/controls_screen.rs` | `pages/ControlsPage` + `presentation/controlGroups.ts` | PARTIAL | Cards, keycaps, editable bindings and categories exist. The guide now includes editable **Change Perspective** (default F5) and Chat (T). Remaining reference differences: W ×2 Run, MMB Pick Block, and dedicated Chat shortcuts for Enter/history/Tab. Do not invent native input commands; display only actual Asteria bindings/actions when implemented. |
| `screens/pause_menu.rs` | `pages/PauseMenuPage` | PARTIAL — SAVE WIRED | `App.tsx` now supplies native Save World and localized success/error feedback; `Ctrl+S` uses the same session checkpoint path. **Leave World / save-and-exit is still not connected**. Gameplay-overlay transition and actual WRY behavior remain to verify. |
| `screens/loading_screen.rs` | `organisms/LoadingOverlay` | MATCHING FOUNDATION | Cosmic background, 560 px frosted card, 18 px gaps, 12 px progress bar present. Asteria deliberately uses real `WorldLoadingState` progress and additional Sphere label. No fake loading timers should be introduced. |

**Important distinction:** MineClone's editable world-name field belongs to **new-world creation**, not the current in-world World Settings layout. Do not add it in-world just to reach superficial feature count.

## Inventory, crafting, storage and overlays

| Reference | Asteria implementation | Status | Remaining difference |
| --- | --- | --- | --- |
| `hud/inventory.rs` + `inventory/layout/player.rs` | `pages/InventoryGameplayPage` + `organisms/PlayerInventoryPanel` | PARTIAL | 3×9 backpack + nine-slot hotbar, slot/cursor, sort and click through `InventoryController` exist. Search currently disables slots based on **ID substring only**; compare with MineClone's localized inventory search semantics. |
| `hud/inventory/layout/creative.rs` | `organisms/CreativeInventoryPanel` | PARTIAL — DATA/CORE/GODOT WIRED | The default pack now contains original category definitions, ten icons and localized names. `InventoryCategoryRegistry` and `Main.SendCreativeCatalog` publish authored category data. **The current React controller/page still infers and alphabetically sorts IDs; icons/names/order are not rendered**, and scroll restoration/localized search remain. |
| `hud/character_info.rs`, `hud/player/portrait.rs` | `organisms/CharacterInfoPanel` | PARTIAL | 320 px panel, 180 px preview region and health bar present. **Source now mounts a Godot-offscreen GLB portrait and a semantic inventory drag-orbit action; actual Godot/WRY visuals are not yet verified.** `InventoryGameplayPage` passes `equipment: []`, so even MineClone's *static empty equipment rows* are hidden. This is a visible layout divergence even without implemented equipment gameplay. |
| `hud/crafting.rs` | `organisms/CraftingPanel` + `CurrentStationPanel` | WIRED — WRY QA PENDING | Pack-authored inventory recipes run through Core transactions and Godot `ui.inventory.craft`; `InventoryGameplayPage` derives live recipes, selection and craftability and supplies `onCraft`. `CurrentStationPanel` has an authored/base station presentation, but station-specific parity and embedded Godot/WRY clicks remain unverified. |
| `hud/storage_box.rs` | `pages/StorageBoxPage` | WIRED — WRY QA PENDING | `StorageBoxPage` is mounted in `App.tsx`, and Godot routes native storage-box open/close, slot and sorting actions to authoritative Core inventories. Full 3×9 visual/input comparison and save/load restoration in a running client remain to be tested. |
| `hud/inventory/layout/item.rs`, `hud/item_icon.rs`, `hud/tool_icon.rs`, `hud/block_icon.rs` | `atoms/ItemGlyph`, `molecules/InventorySlot`, `ItemTooltip` | PARTIAL | PNG icons and stack counts can render from the current catalog, but the fallback is abbreviated ID text. MineClone provides item/metadata-specific icon overrides (including fluid buckets) and specialized block/world-object presentation. Validate metadata, color/tint, texture variants and localized labels. |
| Asteria-only dye/brush palette | `pages/BrushPalettePage` | ASTERIA EXTENSION | No corresponding MineClone screen to reach 1:1. Audit against Asteria's own design system instead. |

**Deliberate layout difference to retain:** Asteria places Inventory above Crafting, whereas MineClone currently uses the reverse order.

## Gameplay HUDs and world-facing presentation

| MineClone reference | Asteria implementation | Status | Remaining difference |
| --- | --- | --- | --- |
| `hud/crosshair.rs` | `atoms/Crosshair` + `molecules/InteractionPrompt` | MATCHING FOUNDATION | 18 px crosshair and semantic action hints exist. Verify exact world-interaction visibility and tool-specific hints in live play; they depend on runtime messages. |
| `hud/hotbar.rs` | `organisms/Hotbar` + `molecules/HotbarSlot` | MATCHING FOUNDATION / PARTIAL | Nine 44 px slots, 4 px gaps and selected borders match. **Selected item name is resolved by item ID**, not the MineClone metadata-aware `stack_display_name` (e.g. bucket fluid). |
| `hud/world/*` | `organisms/WorldBanner` + `molecules/Compass` | MATCHING FOUNDATION | 520 px banner, 360×30 compass, 15-degree markers and world coordinates exist. Sphere and biome label are intentional Asteria adaptation. Verify camera-perspective heading in gameplay when perspective switching exists. |
| `hud/entity_card.rs`, `hud/player.rs` | `organisms/HudEntityCard`, `PlayerVitals` | PARTIAL | 64 px avatar + 180 px details and 22 px health bar present. **A new native offscreen player portrait is supplied to HUD source; Godot/WRY visual parity remains unverified**; Asteria displays generic localized player name rather than MineClone's explicit name. Stamina is an intentional Asteria extension. |
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

1. **P0 — remaining native screen actions:** implement and validate World Selection **Delete** and Pause **Save and Leave/Leave World**, including worker retirement, safe filesystem behavior and failure feedback. World **Load**, standalone **Save**, crafting and Storage Box are already wired; do not reimplement them.
2. **P1 — creative visual wiring:** consume the authored category metadata currently sent by Godot; present MineClone category order, localized names and original PNGs instead of alphabetically inferred text labels. Restore scroll state and localized-name search.
3. **P1 — player and HUD presentation:** real player model/portrait + orbit and equipment rows; creature/target previews, metadata-specific hotbar names and bucket variants.
4. **P1 — incomplete controls/settings:** implement native run/crouch and audit actual camera/control bindings; verify seed editing, localized spawn biome labels and three-pane WRY creation screen.
5. **P2 — visual component completion:** compare independent scroll regions and gameplay overlays, consistent selectable tokens, and focus/input ownership with the Godot-hosted WebUI.
6. **P3 — screenshot and runtime QA:** actual MineClone vs Asteria Godot/WRY side-by-side captures at fixed desktop/narrow viewports and empty/full/error/disabled/focus/hover states. CI/Storybook/Chromium tests do not replace this requirement. See [remaining port roadmap](MINECLONE_PORT_REMAINING.md).

## Invariants

- Preserve `Winky Rough Variable`, Atomic Design, Storybook and React → store → controller → bridge boundaries.
- Never let WebUI own gameplay keyboard/mouse input or invent runtime data/progress.
- Preserve explicit Asteria differences already agreed upon (Sphere terminology, additional biome label, Survival inventory above Crafting, Godot immersion shader).
- During parallel command/world work, re-check `main` and the exact affected files before implementing a batch; this audit is a **source snapshot**, not a lock on ongoing development.

## Executable browser audit gate — New World (2026-10-08)

The original check only looked for isolated components and missed a visible in-game discrepancy. CI now builds Storybook and starts a real Chromium browser. Tests navigate through the **actual React `App`**, using the real `UiStore` and `UiNavigationController` but deliberately fake/non-operative Godot bridge actions, from Starting → World Selection → New World. Checks assert:

- The New World sidebar is visibly left of the content panel at 1920 px **and 640 px**; the former Asteria breakpoint at 760 px wrongly moved it above the form, unlike MineClone's persistent left navigation. Phone-only stacking is now limited to 520 px or narrower.
- Exactly three real sections (World Settings, World Generation, Game Rules), with sidebar labels matching section headings.
- **Seed resides inside World Settings**, after World Name and before Game Mode; it does **not** belong to Game Rules. This follows MineClone `new_world_settings_section()` exactly.
- Game Rules contains both tick-rate and Spawn Creatures controls, and their appropriate interactive state.
- Chromium screenshots at desktop and narrow widths are uploaded as CI artifacts for visual review.

**Boundary:** Chromium Storybook screenshots are *not* proof of what an embedded Godot/WRY executable displays, and are *not* screenshots of the MineClone reference. The reported runtime discrepancy remains open until an in-game A/B comparison is performed.
