# Biome Content Redundancy Audit — 2026-10-11

Removed six block/object definitions that duplicated existing authored content. No new registry, alias, renderer or biome-specific code path was introduced.

| Deleted definition | Reused definition | Rationale |
| --- | --- | --- |
| `mossy_willow_log` | `log_willow` | Same willow log; swamp stump already uses separate `moss_hanging` and `fungus_shelf` decorations |
| `enchanted_hollow_wood` | `log_enchanted_tree_hollow` | Existing hollow enchanted log variant |
| `sandstone_spur` | `spike_sandstone` | Existing sandstone spike geometry/material |
| `rock_spur` | `spike_stone` | Existing stone spike geometry/material |
| `glacial_shard` | `spike_ice` | Existing ice spike geometry/material |
| `dry_exposed_roots` | `roots_exposed` | Same ground-root function, with distribution controlled by biome habitat |

The original six dedicated PNG assets were also deleted; no remaining block definition references them.

## Preserved content

Distinct colors/species, growth forms, emitted light, texture families, large authored formations and ecosystem-specific behavior are **not** treated as duplicates by name alone. Examples kept: `aether_crystal` (light-emitting spike), `crystal_core_aether` (solid crystal core), `kelp` (independent Object) versus `kelp_stem`/`kelp_frond` (multi-block forest structures), `log_charwood` and `log_frost_wood` (distinct wood materials), and `fungus_cap_luminous_giant` versus `mushroom_cap_marsh_giant` (different mushrooms).

Biome placement, clustering and habitat weights remain authored in the existing JSON rules. Using generic `spike_stone`, `spike_sandstone`, and `spike_ice` intentionally inherits their current placement/support behavior and material appearance rather than preserving bespoke spur/shard behavior.

Old save IDs are not preserved: Asteria is pre-release, and no content alias or migration is added without an explicit request.

## Shared prickly pear structure variants

The four `cactus_prickly_pear_01/02` and `gorge_prickly_pear_01/02` structure files had byte-identical above-ground shapes, with only a generated Sand versus Stone ground voxel changed. They were replaced with **two** `prickly_pear_01/02` variants and a single `prickly_pear` group. Their `groundAnchorY: 0` avoids writing an artificial ground block, while the same two independent dimension rules retain desert versus gorge biome/habitat density. Allowed support is the union of the original pack-authored ground types, and the existing terrain is not overwritten at Y=0.
