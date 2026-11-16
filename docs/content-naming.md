# Content naming

Authored content names use `type_variation` in lowercase snake_case. The type
comes first; species, material, color, size, environment and state follow it.
A type with no variation keeps its bare name, such as `stone`, `stick` or `water`.
Compound types are allowed, such as `mushroom_cap`, `crystal_core` and `stair_segment`.

| Content | Canonical examples |
| --- | --- |
| Mushrooms | `mushroom_red`, `mushroom_cap_glow`, `mushroom_stem_glow` |
| Logs | `log_oak`, `log_oak_stripped`, `log_oak_stripped_hollow` |
| Tree foliage | `leaf_oak`, `leaf_frost_wood_arctic` |
| Planks and saplings | `planks_oak`, `sapling_oak` |
| Shaped materials | `bricks_stone`, `tiles_ice`, `layer_snow`, `spike_basalt` |
| Ore layers and raw ore items | `ore_iron`, `ore_iron_raw` |
| Tools | `hatchet_rustic`, `axe_carpenters_rustic`, `kit_artisans_rustic` |
| Equipment | `helmet_wayfarer`, `boots_wayfarer` |
| Plants | `fern_marsh`, `grass_tall`, `flower_cactus_pink` |
| Dyes | `dye_red`, `dye_light_blue` |
| Structures | `tree_oak_01`, `outcrop_basalt_01`, `stump_oak_01` |

## IDs, files and references

- Keep the namespace (`asteria:`) and any explicit domain scope (`ambient/`).
- A definition filename must match the final component of its ID, including
  variations and any numeric structure suffix.
- Inventory resource filenames use the same order and underscores, never hyphens.
- Oak's tree-resource layout is the reference: `textures/blocks/trees/{species}/`
  contains `side.png`, `top.png`, `stripped.png`, `planks.png`, `leaf.png` and
  `sapling.png`. Face names are local to that directory and remain unchanged.
- Every tree's IDs use the same species spelling as its resource directory.
  For example, enchanted tree uses `log_enchanted_tree`, `leaf_enchanted_tree`,
  `planks_enchanted_tree` and `sapling_enchanted_tree`.
- Update recipes, loot, variant families, structures, structure groups, biome
  references, behavior bindings, localization keys and code together when renaming.
- Display names and translated prose are independent of internal IDs.
- Dimensions, dimension-scoped biome identities, engine classes and gameplay
  action names retain their own domain contracts; this convention names authored
  content types and their variations, not programming symbols.

This normalization replaces previous IDs rather than registering legacy aliases.
Save files containing previous content IDs require an explicit migration before
they can be loaded against the normalized catalog.
