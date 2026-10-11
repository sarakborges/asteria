# Biome Content Redundancy Audit — 2026-10-11

Removed six block/object definitions that duplicated existing authored content. No new registry, alias, renderer or biome-specific code path was introduced.

| Deleted definition | Reused definition | Rationale |
| --- | --- | --- |
| `mossy_willow_log` | `log_willow` | Same willow log; swamp stump already uses separate `hanging_moss` and `shelf_fungus` decorations |
| `enchanted_hollow_wood` | `log_enchanted_hollow` | Existing hollow enchanted log variant |
| `sandstone_spur` | `sandstone_spike` | Existing sandstone spike geometry/material |
| `rock_spur` | `stone_spike` | Existing stone spike geometry/material |
| `glacial_shard` | `ice_spike` | Existing ice spike geometry/material |
| `dry_exposed_roots` | `exposed_roots` | Same ground-root function, with distribution controlled by biome habitat |

The original six dedicated PNG assets were also deleted; no remaining block definition references them.

## Preserved content

Distinct colors/species, growth forms, emitted light, texture families, large authored formations and ecosystem-specific behavior are **not** treated as duplicates by name alone. Examples kept: `aether_crystal` (light-emitting spike), `aether_crystal_core` (solid crystal core), `kelp` (sprite) versus `kelp_stem`/`kelp_frond` (3D forest), `charwood` and `frost_wood` (distinct wood materials), and `luminous_giant_fungus_cap` versus `marsh_giant_mushroom_cap` (different mushrooms).

Biome placement, clustering and habitat weights remain authored in the existing JSON rules. Using generic `stone_spike`, `sandstone_spike`, and `ice_spike` intentionally inherits their current placement/support behavior and material appearance rather than preserving bespoke spur/shard behavior.

Existing old-world saves referring to deleted IDs are not automatically migrated: compatibility requires a separately authorized save migration.

## Shared prickly pear structure variants

The four `cactus_prickly_pear_01/02` and `gorge_prickly_pear_01/02` structure files had byte-identical above-ground shapes, with only a generated Sand versus Stone ground voxel changed. They were replaced with **two** `prickly_pear_01/02` variants and a single `prickly_pear` group. Their `groundAnchorY: 0` avoids writing an artificial ground block, while the same two independent dimension rules retain desert versus gorge biome/habitat density. Allowed support is the union of the original pack-authored ground types, and the existing terrain is not overwritten at Y=0.
