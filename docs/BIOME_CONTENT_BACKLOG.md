# Asteria — Biome Content Audit and Implementation Backlog

Status: **proposed, not implemented**. Audited on 2026-10-10 against the
active default-pack Overworld dimension. This is a content gap audit,
not a claim that every world position or procedural density has been
visually validated.

## Scope and evidence

Source of truth:
- `packs/default/data/dimensions/overworld.json`
- all 12 `surfaceBiomes` and both `volumeBiomes` configured therein
- each corresponding `packs/default/data/biomes/*.json`
- current `generatedSurfaceStructures` roots (84) and volume `volumeStructures`
- `BiomeDecorationDefinition`, `SurfaceDecorationField`, existing
  StructureRegistry templates and block definitions.

Counts below are independent *rule/family registrations*, **not**
numbers of variants or observed spawn density. Rare structures can be
invisible in a particular seed despite many registered rules. Avoid
drawing placement conclusions without seed-reproducible worldgen tests.

| Biome | Ground/volume decoration rules | Surface structure families | Volume structure families | Gap |
| --- | ---: | ---: | ---: | --- |
| Plains | 6 | 13 | 0 | Diversity of groundcover/flower species |
| Swamp | 9 | 3 | 0 | Hanging ecology, reed clusters, wetland landmarks |
| Wasteland | 2 | 7 | 0 | Highest gap in unique small-scale flora and props |
| Enchanted Forest | 4 | 2 | 0 | Sparse magical undergrowth and distinct landmarks |
| Desert | 4 | 5 | 0 | Mostly complete; missing succulent and dry-wood accents |
| Alps | 5 | 10 | 0 | Mostly complete; avoid redundant ice spikes |
| Arctic | 4 | 6 | 0 | Mostly complete; avoid redundant snow shrubs |
| Ocean | 5 | 9 | 0 | Mostly complete; missing low-profile sea-floor biodiversity |
| Gorge | 7 | 6 | 0 | Cliff vegetation requires directional attachments |
| Mountain Belt | 3 | 7 | 0 | Generic rock sets, little protected-pass vegetation |
| Mountains | 3 | 7 | 0 | Generic rocks dominate; little slope-adapted plant life |
| Volcano | 4 | 9 | 0 | Recently enriched; selective ash/mineral detail only |
| Floating Islands | 7 | 0 | 2 | Multi-height canopy/underside ecology |
| Caverns | 6 | 0 | 3 | Wall ecology and localized ceiling/floor variety |

**Important nuance:** Wasteland already has `wasteland_snag` and
`fallen_log_wasteland`; Enchanted Forest already has `tree_enchanted`
and `enchanted_grove`. Mountains/Mountain Belt already have multiple
boulder/outcrop/waterfall rules. The gap is *ecological differentiation*,
not a missing tree or boulder system. Swamp has especially dense authored
Willow roots (`spacing:9`, `chance:0.9`), so structure-root count
does not mean sparse observed foliage.

## P0 — best new content work (existing systems)

### A. Wasteland — dry, dead and layered, not overgrown

1. `wasteland_dead_thistle`: sparse silhouette, crossedSprite, Dirt/Gravel,
   favor `rocky_scrub` and `deadwood_pockets`.
2. `wasteland_brittle_tuft`: small straw-colored clump, crossedSprite,
   Dirt, `barren_flats` with sparse clustered distribution.
3. `wasteland_dry_lichen`: groundSprite on Stone/Gravel;
   should not repaint the terrain or create circular patches.
4. `wasteland_bleached_roots`: flat coarse roots on Dirt and Gravel,
   favor deadwood pockets.
5. `wasteland_thorn_scrub`: 2–4 variants of angular 3D thorn thickets;
   rare multi-block landmarks compatible with existing `wasteland_snag`.
6. `wasteland_bone_scatter`: low grounded props, optional
   `wasteland_fossil_rib` sparse multi-block structures. These are
   environmental objects, not mobs or fossil gameplay.

Keep `barren_flats` intentionally open. Use sparse clusters and
independently authored habitat weights. **No random ground-material
patches, extra Pebbles or invented terrain erosion system.**

### B. Enchanted Forest — magical undergrowth / lighting

1. `enchanted_starflower`: glowing flower in `luminous_clearing`,
   crossedSprite with *authored* colored emission.
2. `enchanted_violet_fern` and `enchanted_pink_bloom`: colorful
   wind-swaying distinct foliage in respective habitat bands.
3. `enchanted_glow_pod`: small luminous upright plant, rare under trees.
4. `enchanted_mushroom_ring`: authored structure of mushrooms around
   an empty middle, not a terrain material circle.
5. `enchanted_hollow_stump`: stump with mushrooms or flowers, a
   2–3-variant small authored landmark, grounded on Grass/Dirt.
6. `enchanted_fungal_canopy`: uncommon luminous large cap/group
   using 3D structure voxels, silhouette distinct from existing trees.

**Immediate reuse:** `asteria:mushroom_red` and
`asteria:mushroom_blue` block definitions already exist in the pack
but neither is an active decoration in the audited 14 biomes.
Check their authored textures; reuse selectively rather than creating
redundant new mushroom blocks. Do not re-enable unwanted enchanted-leaf
tint or change existing biome colors.

### C. Mountains and Mountain Belt — distinct altitudinal ecology

Mountains (habitats `foothill_shelves`, `talus_fields`,
`exposed_ridges`):
- `mountain_stonecrop`, `mountain_saxifrage`: small rock-rooted
  plants on Stone/Stone Cobble; avoid arbitrary Dirt patches.
- `mountain_dwarf_juniper`: 2–3 low 3D woody shrub variants on stable
  foothill ledges; slope guard.
- `mountain_rock_lichen`: subtle groundSprite, prefer `talus_fields`.
- Rare `mountain_wind_pine` (2–3 variants) limited to protected
  shelves, not bare summits.

Mountain Belt (habitats `sheltered_passes`, `boulder_runs`,
`bare_crests`):
- `belt_pass_grass`, `belt_thorn_rosette`: sparse small plants in passes.
- `belt_windswept_pine`: twisted conifer group (2–3 3D variants)
  mostly in `sheltered_passes`, visibly unlike alpine snow fir.
- `belt_scree_lichen`: low rock cover in boulder runs, not terrain patches.
- `belt_rock_needle_cluster`: occasional dramatic square-section rock
  spurs for crests; must be different from existing boulder sizes.

Keep peaks bare; these proposals require habitat/slope/height constraints.

## P1 — next biodiversity and object families

| Biome | Proposals | Implementation conditions |
| --- | --- | --- |
| Plains | White daisies, purple wildflower, clover carpet, taller seeded meadow clumps | CrossedSprite or groundSprite; `open` and `thicket`, avoid rocky |
| Swamp | Swamp orchids, bulrush clusters, mushroom shelves on stump structures, draped Willow moss | Ground decorations now; branch-draped moss as variants of Willow structures |
| Gorge | Ocotillo-like shrub, stone rosettes, layered canyon lichens, rare stone arch | Grounded on actual Stone/Dirt/Gravel; wall-clingers deferred |
| Desert | Agave rosettes, succulents, small drywood thickets, rare fossil props | Sand or Sandstone support; no Gravel/Pebbles |
| Ocean | Sea urchins, tube sponges, barnacles, sea cucumbers, tall ribbon-grass clumps | `fluidPlacement: submerged` for small objects; `requiredFluid: water` for multi-block |
| Floating Islands | Aether ferns, dangling luminous berry clusters, intertwined suspended roots, small skywood copses | Use floor/ceiling volume and 3D structures; ensure additive density support |
| Caverns | Cave moss on floor, fungal rings, bulbous small crystal nests, draping root clusters | Floor/ceiling currently supported; true side-wall attachments deferred |
| Volcano | Pumice fragments, scoria crust, dark glass shards, sparse mineral encrustations | Basalt/Basalt Cobble support, forbid Lava, do not alter crater |
| Arctic | Tundra seed-heads, frost flowers, isolated frost-carpet variants | Low density in `frozen_plain`; do not clone current frost shrub |
| Alps | Alpine cushion flowers, crevice sedges, sparse exposed-rock lichens | Stable Stone/Snow support with altitude guards |

These are *candidates*, not all mandatory changes. Reject objects
that do not add a distinct silhouette, size, color, habitat, or gameplay
value over existing content.

## P2 — genuine system prerequisites

1. **Directional wall-attached decorators**: `DecorationSupportSurface`
   currently supports `Floor` and `Ceiling`, not walls. Implement
   wall ownership, attachment orientation and cross-chunk validation
   before wall ferns in Gorge or shelf mushrooms in Caverns. Do not
   fake wall objects using arbitrary floating world props.
2. **Grouped/stacked low-profile decals**: optional reusable surface
   prop semantics for carpets, connected moss patches and barnacle
   colonies only if crossedSprite/groundSprite cannot meet quality.
3. **Fluid-preserving underwater plant visuals**: verify current block/
   fluid renderer and physics for multi-block kelp; the newly authored
   `requiredFluid` placement rule checks generation only.
4. **Controlled particles** (later): ash wisps, spores, sea bubbles
   and volcanic fumaroles should use the existing particle/wind owners,
   not introduce biome-specific ad hoc simulation.
5. **Harvest/drops**: new blocks use validated pack definitions;
   bespoke collection, growth, spread or crafting requires separate
   gameplay work rather than silently assuming interactions.

## Reuse and implementation rules

- 64×64 texture art, pack-owned paths, blocky game aesthetic.
- New plants should be species/shapes, not recolors of current foliage
  unless the result has a distinct gameplay or silhouette purpose.
- Prefer crossedSprite for herbs, groundSprite for carpets/lichens,
  authored 3D structures for arches, woody canopies and thickets.
- Use accurate support blocks from the *current* biome palette:
  `grass_block` on Enchanted/Plains, `dirt`/`gravel`/`stone`
  on Wasteland, `stone` on Mountains/Mountain Belt, `basalt`
  only in Volcano, etc.
- No new terrain patches or unnecessary cross-biome exceptions.
- Prefer deterministic `cluster`, `habitatWeights`, `conditions`;
  for large structures use `generatedSurfaceStructures` or
  `volumeStructures`, slope guards and conflict groups.
- Avoid texture duplicates. Reuse existing block definitions before
  creating new registries/engines or near-identical materials.
- Author content tests for block existence, valid texture paths and
  shapes, support/fluids, relevant biome-only attachment, and
  variant diversity. Validate deterministic generation on multiple
  seeds and selected biome habitat locations when runnable.
- Measure density *in world*, not from numbers of JSON rules. Track
  per-family accepted/rejected placement counts, sample screenshots
  and representative biome-border chunks. Sparse landmarks should
  remain meaningfully sparse. Test visual readability under real
  lighting and foliage tint.

## Next suggested execution order

1. **Wasteland small flora + skeletal scrub** — first, since
   no biome-specific decorator is authored today.
2. **Enchanted undergrowth + mushroom ring** — second, since
   its unique trees are not matched by layered flora.
3. **Mountain/Mountain Belt split** — third; different plant
   sets and altitude/habitat rules rather than copies of rocks.
4. **Plains flower meadow variety and Swamp hanging moss** —
   next, after validating actual Willow placement/density.
5. **Wall attachment and any vertical ecology** — only after
   implementing/validating a generic directional surface contract.
6. **Selective Ocean/Caverns/Floating Islands/Desert/Volcano additions**
   after visual baseline; these already received major recent work.

All proposals above are optional authored content or explicit
extensions. They do not modify current terrain, topology, palettes,
crater, sea level, world Y range, or Sphere boundaries.
