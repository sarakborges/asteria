# MineClone → Asteria: WebUI parity ledger

**Reconciled 2026-10-14** against [Asteria `fd28b67b2`](https://github.com/sarakborges/asteria/commit/fd28b67b252bcb26195fcfa10c9cb5f46032ae09), MineClone `main` `a4e9d00` and `world-systems-rebuild` `e8bc856`. The 2026-10-08/09 source audit and its stale "Prioritized correction batches" remain in Git history; they are **not** instructions to re-implement features corrected later. See the [current port ledger](MINECLONE_PORT_REMAINING.md).

**Verdict:** no source-confirmed missing WebUI feature is established by the previous audit after reconciling later commits. **Wired in source** is not the same as **visually verified in the embedded Godot/WRY client**.

## Current implementation inventory

| UI surface | Connected implementation | Evidence and acceptance boundary |
| --- | --- | --- |
| **Starting Screen** | `StartingScreenPage`, real World Selection/Settings/Controls and Mods/Packs navigation; intentionally Asteria-branded. | `2f7dfe2`, `a67d1f0`; verify live screen layout/keyboard |
| **World Selection** | Load, Delete with focus-contained confirmation, Recreate on saved-world cards (not Loading), thumbnail previews, error recovery/guarded native actions. | `e0b6d00`, `eb6a16f`, `a16e1f6`; live filesystem and keyboard QA only |
| **New World / Game Rules** | Three left-nav sections (World Settings, World Generation, Game Rules); unsigned-64 seed, modes Normal/Flat/Void, Spawn Biome localized labels, Single Biome, size, caves/oceans/structures, creature and tick rules; actions single-flight. | `3d8f2a6`, `0bfabac`, `b98c5a4`; real generation connected; see [source contract](WORLD_CREATION_PARITY.md) |
| **Settings and Controls** | Shared settings shell/ScrollArea, native key capture, editable binds including E, T, F5, pick block, movement, game modes, dropdown/numeric/selection controls and shared transitions. | `b0ef823`, `b6a782b`, `39f66da`; visual/input QA only |
| **Pause / Death / Loading** | Save-and-leave/exit lifecycle, save failure confirmation, death/respawn state, nonfabricated loading state and immediate ESC semantics. | `45d247b`, `5e8ce0c`, `e68b3a4`; play-session acceptance only |
| **Survival / Creative inventory** | Real Core-backed backpack/hotbar/cursor/equipment; right/Shift-clicks, Creative fill, metadata-aware search, filtered dimming, true recipe availability, category order/icons and independent fast-scroll memory. | `aaf24aa`, `84ea9ce`, `0de8c35`, `be85ab9`; real WRY clicking/scrolling QA only |
| **Storage / Crafting / Brush** | `StorageBoxPage`, `CraftingPanel`, Brush palette connected to existing Core/native actions; unavailable actions are not fabricated. | `a9788b6`, `f85f8d7`, `518d103`/PR #34; interactive QA only |
| **Player/creature/world HUD** | Vitals/oxygen/armor wear, portrait, target block/Object/creature/fluid details, active Layer face/spatial tint, real voxel PNGs, native icon precedence, localized metadata and front-camera compass reversal. | `8d78e64`, `d37c81e`, `dfedab1`, `f4cfc9d`; Godot rendering and WRY pixel QA only |
| **Chat** | Real dock/commands, localized autocomplete, input history, action links, T autofocus, focus handoff and OS refocus, scroll follow-at-bottom. | `1080a23`, `21ca2e9`, `b74c594`, `6cf4738`; live WRY QA only |
| **Shared primitives** | Screen shell, buttons, toggles, dropdowns, UInt64 and numeric stepper, ScrollArea, selectable states, Storybook fixtures, 160+160ms transitions. | `b0ef823`, `c96fb1b`, `b6a782b`; CSS screenshots cannot prove embedded runtime |

These citations identify representative corrective commits. The authoritative behavior resides in current `ui/src/App.tsx`, `ui/src/controllers/`, `ui/src/state/`, `ui/src/components/` and Godot bridge adapters. Do not infer new work from pre-fix historical statuses.

## Confirmed design adaptations — keep

- **Separate Controls page** rather than adding MineClone's integrated Game Settings Keybinds section again.
- **Inventory above Crafting**, instead of rearranging it solely to match the reference.
- **Winky Rough Variable**, Sphere/biome identity in the world HUD, Asteria-only Brush palette and extensions.
- **Fluid immersion** is a Godot shader overlay, not a React `fluid_immersion` component.
- **Language selection** lives in Settings, not the Starting Screen.
- **Save policy** is save on leave/exit, not a separate save button/autosave.
- **Real loading progress**, never synthetic progress or timers.

## What is *not* signed off

The original 2026-10-08 WRY screenshot lacked parts of the expected New World screen. Subsequent Chromium/browser tests establish that the real three-section React layout exists at desktop and narrow viewports, but they do **not** establish that every embedded Godot/WRY window presents it identically. Likewise, successful DOM/autofocus, portrait, biome-tint and native smoke tests do not prove identical pixels or driver behavior.

The existing test surfaces are `ui/tests/browser/`, Storybook stories and `tests/godot/native_presentation_smoke.gd` with its C# smoke companions. Run those plus an interactive A/B capture before calling a visual/input defect confirmed. Report the exact viewport, input steps, actual screenshots/log and reference behavior. **Do not** reimplement scrolling, focus, HUD previews, movement controls or World Creation as generic "port batches" without a reproduced discrepancy.

**No missing WebUI implementation batch is currently authorized by this reconciliation.**
