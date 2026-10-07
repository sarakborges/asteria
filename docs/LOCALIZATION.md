# Localization

Ported from MineClone's domain-keyed catalog contract. English, Brazilian Portuguese and Spanish are mandatory.

- Canonical catalogs: `packs/default/data/localization/{english,portuguese_brazil,spanish}/ui.json`.
- Every locale must contain identical keys, non-empty values and matching named placeholders (including multiplicity).
- Player-facing text in React components must use `useLocalization().t("domain.key")`. Keep components otherwise independent of localization.
- The starting screen selects the active language, stored as `asteria.language` in browser storage. English is the initial default.
- Never mutate the font family when adding translations.
- Additional translated data catalogs for blocks, items, creatures and others should only be activated after wiring their definition loading paths; MineClone definition IDs must not be assumed to match Asteria IDs.
- New user-facing text must be added to all three locale files together.
