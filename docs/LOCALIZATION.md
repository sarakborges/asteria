# Localization

Ported from MineClone's domain-keyed catalog contract. English, Brazilian Portuguese and Spanish are mandatory.

- Canonical catalogs: `packs/default/data/localization/{english,portuguese_brazil,spanish}/ui.json`.
- Every locale must contain identical keys, non-empty values and matching named placeholders (including multiplicity).
- Player-facing text in React components must use `useLocalization().t("domain.key")`. Keep components otherwise independent of localization.
- The starting screen selects the active language, stored as `asteria.language` in browser storage. English is the initial default.
- Never mutate the font family when adding translations.
- Domain catalogs for blocks, fluids, dimensions, items, tools, and creatures are checked against the authored Asteria pack IDs; translation never changes a stable runtime ID.
- New user-facing text must be added to all three locale files together.

## Localized game content

- Block, biome/dimension, and fluid names are keyed by stable definition ID with the `/name` JSON pointer, matching MineClone.
- Asteria-only blocks have authored EN, PT-BR and ES names.
- React `contentName(id)` resolves translated content names without modifying gameplay IDs; unknown dynamic IDs retain their generated display label until their catalog exists.
- `npm run check:localization` verifies identical keys and placeholders across languages for every catalog; it runs as part of WebUI and Storybook builds.
- Storybook previews share the React localization provider.
- In Asteria, pack data JSONs remain translation-free. Any future localized definition fields belong to the content-loader boundary, not duplicate runtime models.

## Godot → WebUI localized messages

- Godot remains authoritative for world creation validation and loading-phase state. It sends stable error **keys** (`game.world_creation.error.payload.key`) and phase identifiers (`game.loading.payload.phase`), not already-translated prose.
- WebUI controllers validate/normalize incoming identifiers; React calls the active locale catalog when rendering. Changing the language recomputes visible text without restarting Godot or mutating save/protocol IDs.
- World-creation error keys are `newWorld.error.seedMustBeString`, `newWorld.error.invalidSeed` and `newWorld.error.unexpected`. Runtime loading phase keys live under `loading.runtime.*`.
- Debug status keys are presentation-only; bridge event names and runtime diagnostic payloads are technical, untranslated identifiers.
- The catalog check additionally cross-validates every block, biome, dimension and fluid definition ID against domain `/name` translations. This detects missed content during subsequent ports.

## Item, tool and creature migration

- Source: MineClone `world-systems-rebuild`. The default pack now contains 12 items, 9 tools and 20 creatures alongside their EN/PT-BR/ES JSON Pointer (`/name`, `/hint`) catalogs. Tools retain authored behavior IDs but Asteria has not implemented those interactions yet.
- `Asteria.Core.Content` owns immutable typed definition parsing/registries. Godot's selected-pack loaders read authored JSONs through `ProjectDataDocuments`; the Godot composition root loads registries. Creature spawn, movement, combat, inventory use and renderer wiring require separate runtime systems; importing definitions alone does **not** make entities appear in the game.
- Original textures and model GLBs are stored under `packs/default/resources` and stay outside Godot's import scanner. The Architect's Compass image comes from MineClone `main`, where it exists, because the rebuild branch references it without including the file.
- The WebUI translates IDs using the three new domain catalogs; `check:localization` rejects missing/extra IDs, pointer mismatches, placeholder mismatches or broken item/tool/creature resource references.
