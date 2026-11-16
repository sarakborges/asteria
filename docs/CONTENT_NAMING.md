# Reusable block and object IDs

Authored block and object IDs describe **what the content is**, not the biome where it happens to spawn.

- Name content by recognizable material, species, shape, geometry, or intrinsic property: `daisy`, `lichen_rock`, `mossy_willow_log`, `basalt_column`.
- Biome restrictions belong in `packs/default/data/biomes/*.json` and authored structure restrictions, **not** in an artificial ID prefix.
- A genuinely distinct variant may have a descriptive qualifier: `lichen_dry`, `lichen_soot`, `lichen_cliff`, `fern_marsh`. Do not collapse genuinely distinct assets into a single ID just to shorten names. First inspect existing blocks, including material-specific spike variants and hollow wood variants, before introducing new definitions.
- Species/material terms that happen to resemble biome names are allowed when inherent to the content, such as `edelweiss_alpine`, `volcanic_glass`, `log_skywood`, and `enchanted_hollow_wood`.
- Prefer reusing a block/object across multiple biomes with authored habitat rules over cloning its definition, textures or localization.
- Keep block IDs, definition filenames, localized labels, and texture filenames consistent when introducing or renaming an item. Preserve existing 64×64 texture pixels and pack ownership.
- Structure **families** can retain descriptive habitat/ecology identifiers: they define compositions and worldgen landmarks rather than individual block/object identities.

## 2026-10-11 normalization

The 70 old-to-new IDs are listed in [content-id-renames-2026-10-11.json](content-id-renames-2026-10-11.json). The file documents this content-breaking rename; it is not a runtime alias registry.

**Save policy:** old serialized content IDs are unsupported following a breaking content rename. Do not add aliases, duplicate definitions or automatic migration for older saves unless explicitly requested.

## Review checklist

1. Would the name remain truthful if the same content spawned in another biome?
2. Does any qualifier describe a real, visible or functional distinction rather than current biome placement?
3. Can an existing block, wood variant, texture or attached surface layer already express the same content?
4. Are all IDs in structures/biomes/tests/localization and all pack-relative texture paths updated?
5. Does localization remain complete in English, Brazilian Portuguese and Spanish?
