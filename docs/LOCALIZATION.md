# Localization

Ported from MineClone's domain-keyed catalog contract. English, Brazilian Portuguese and Spanish are mandatory.

- Canonical catalogs: `packs/default/data/localization/{english,portuguese_brazil,spanish}/ui.json`.
- Every locale must contain identical keys, non-empty values and matching named placeholders (including multiplicity).
- Player-facing text in React components must use `useLocalization().t("domain.key")`. Keep components otherwise independent of localization.
- The starting screen selects the active language, stored as `asteria.language` in browser storage. English is the initial default.
- Never mutate the font family when adding translations.
- Additional translated data catalogs for blocks, items, creatures and others should only be activated after wiring their definition loading paths; MineClone definition IDs must not be assumed to match Asteria IDs.
- New user-facing text must be added to all three locale files together.

## Localized game content

- Block, biome/dimension, and fluid names are keyed by stable definition ID with the `/name` JSON pointer, matching MineClone.
- Asteria-only blocks have authored EN, PT-BR and ES names.
- React `contentName(id)` resolves translated content names without modifying gameplay IDs; unknown dynamic IDs retain their generated display label until their catalog exists.
- `npm run check:localization` verifies identical keys and placeholders across languages for every catalog; it runs as part of WebUI and Storybook builds.
- Storybook previews share the React localization provider.
- In Asteria, pack data JSONs remain translation-free. Any future localized definition fields belong to the content-loader boundary, not duplicate runtime models.
