# Asteria Packs

This document defines the authored contract for Asteria content packs.

A pack is one selectable unit. It owns data plus presentation customizations while preserving strict domain boundaries.

## 1. Canonical layout

Every pack lives under one root:

```text
packs/
  default/
    pack.json
    data/
      blocks/
      fluids/
      biomes/
      structures/
      structure_sets/
      recipes/
      loot/
      dimensions/
    resources/
      textures/
      audio/
      fonts/
      models/
    ui/
      theme.json
      assets/
```

The built-in startup pack is `packs/default/`.

Runtime selection is represented by one `PackSelection` value. The initial value is `default`; future import/selection UI may change it without changing individual content loaders.

## 2. Non-negotiable engine boundary

The authored format belongs to Asteria, not Godot.

External packs must never require:

- Godot `.import` sidecars;
- `.godot/` generated content;
- Godot UID/import cache files;
- `.tres`, `.res` or `.tscn` as authoritative pack data;
- editor-specific absolute paths;
- generated runtime caches.

Built-in packs follow the same rule as external packs: Godot-generated sidecars and import metadata are forbidden inside `packs/`. The repository root contains `packs/.gdignore`, so Godot must not scan or import pack contents. Runtime pack loaders resolve validated pack-relative paths and read authored files directly from the filesystem; rendering adapters create runtime Godot resources from those decoded files when needed.

Because Godot intentionally excludes `.gdignore` trees from normal resource export, desktop/release packaging must ship the selected pack tree as ordinary files rather than relying on the PCK resource importer. This is deliberate: Asteria packs remain externally inspectable/selectable content, not compiled Godot resources.

## 3. Manifest

Each pack has one manifest at `packs/{name}/pack.json`:

```json
{
  "format": 1,
  "id": "example:my_pack",
  "version": "1.0.0",
  "name": "My Pack",
  "description": "Optional description",
  "dependencies": []
}
```

- `format` is the Asteria pack-format version.
- `id` is a stable namespaced identifier.
- `version` is the authored pack version.
- `dependencies` declares pack dependencies/version requirements.
- unsupported mandatory format versions fail clearly.

## 4. Data domain

`data/` owns declarative gameplay/content definitions.

Definitions may add or override namespaced blocks, fluids, biomes, structures, recipes, loot, dimensions and future definition-driven systems.

Dimensions live under `data/dimensions/*.json`. A dimension is one authored world-runtime configuration; in-game, dimensions are called **Spheres**. It declares its stable ID, explicit `surfaceBiomes` and optional `volumeBiomes` pools, sea level, gravity strength, spawn coordinates, optional Sphere Shell bounds and engine-agnostic environment presentation values. Surface-terrain heights are authored as offsets relative to that dimension sea level rather than baking an absolute world height into each biome. A root world seed is not duplicated into the pack; runtime derives a stable per-dimension seed from the world seed + dimension ID.

Example placement pools:

```json
"surfaceBiomes": [
  "asteria:overworld/plains",
  "asteria:overworld/ocean"
],
"volumeBiomes": [
  "asteria:overworld/floating_islands",
  "asteria:overworld/caverns"
]
```

A biome ID may not be repeated across placement pools. Surface entries must author `surfaceLayout` + `surfaceTerrain`; volume entries author `volumeLayout.placement` (`additive` or `carvedVoid`) and the necessary terrain/cave capability. All biomes author a shared `palette.default` and optional face overrides.

`surfaceLayout.weight` controls normal surface-biome distribution. `surfaceLayout.spawnWeight` is separate: it is a non-negative weight used only to choose the preferred initial-spawn biome deterministically from the dimension seed; the generated ocean biome is excluded from that choice. A zero spawn weight keeps a biome in normal world generation while preventing it from being selected as the preferred random spawn biome. `cannotBorder` is symmetric at runtime: if either biome denies the other, the pair is incompatible. MineClone legacy exclusive-neighbor groups are represented explicitly through these authored pairwise `cannotBorder` IDs rather than a second adjacency mechanism.

Surface terrain is a tagged authored profile. The supported profile types are `rolling`, `dunes`, `ocean`, `swamp`, `mountains`, `gorge`, `alps`, `mountain_belt`, and `volcano`; the untagged macro/detail form remains valid for simple/custom noise terrain. Example:

```json
"surfaceTerrain": {
  "type": "volcano",
  "baseHeight": 12,
  "height": 72,
  "craterDepth": 38,
  "craterRadius": 0.14,
  "irregularity": 0.13,
  "irregularityScale": 0.009,
  "detailIrregularity": 0.055,
  "detailScale": 0.031,
  "craterIrregularity": 0.08
}
```

Typed terrain is still evaluated only by `SurfaceTerrainField`. `swamp`, `gorge`, and `volcano` consume the deterministic formation strength supplied by `BiomeField`; they do not create a parallel placement/distribution owner. Optional `surfaceTerrain.modifiers` currently supports `height_offset` and `cliffs`.

A volcano biome may author crater lava without putting random lava patches on the Sphere:

```json
"surfaceFluid": {
  "type": "volcano_crater",
  "fluid": "asteria:lava",
  "minimumStrength": 0.91,
  "levelOffset": 8,
  "spillMinimumStrength": 0.56,
  "spillMaximumStrength": 0.9,
  "spillScale": 0.012,
  "spillWidth": 0.055,
  "spillLevel": 6
}
```

`volcano_crater` is valid only with `surfaceTerrain.type = "volcano"`. `GeneratedFluidField` derives the crater fill level from that terrain definition and may add deterministic spill channels; it never modifies the volcano shape.

A Sphere may define a shell floor, roof, or both:

```json
"shell": {
  "block": "asteria:sphere_shell",
  "floorY": 0,
  "roofY": 383
}
```

`floorY` and `roofY` are optional individually, but at least one is required when `shell` is present. Negative Y is not part of Asteria's world contract, so shell bounds cannot be negative. Block mining policy remains block-authored; the default Sphere Shell uses optional `mining.unbreakable: true`.

A Sphere may author deterministic generated surface structures:

```json
"generatedSurfaceStructures": [
  {
    "biome": "asteria:overworld/plains",
    "structure": "asteria:boulder_small",
    "spacing": 36,
    "chance": 0.28,
    "jitter": 11
  }
]
```

The referenced biome must be in that Sphere's active `surfaceBiomes` pool and the `structure` reference must resolve to a Structure, Structure group, or StructureSet in the selected pack. `spacing` defines the deterministic candidate lattice, `chance` gates each candidate, and `jitter` offsets it within at most half a spacing cell. `placement` defaults to `biomeInterior`; `biomeMargin` is available for direct rotatable Structure roots such as river mouths and deterministically anchors the root immediately outside the authored biome while forcing rotation from the sampled outward margin normal. Placement is evaluated against the base surface rather than additive volume terrain. Structure footprint constraints are checked before materialization; accepted templates may cross chunk boundaries and expand the generator's vertical streaming range.

Structure templates live under `data/structures/*.json`:

```json
{
  "id": "asteria:boulder_small",
  "rotation": true,
  "restrictions": {
    "requiredBiomeCoverage": 1,
    "maxSlope": 1,
    "requiresDryGround": true
  },
  "anchor": { "x": 1, "y": 0, "z": 1 },
  "palette": {
    "S": { "block": "asteria:stone" }
  },
  "layers": [
    { "y": 0, "rows": ["SS.", "SSS", ".S."] }
  ]
}
```

`anchor` converts authored grid positions into offsets from the placement origin. `.` is empty; every other symbol must resolve through the palette. A palette symbol may author exactly one primary payload: `block` (with optional `orientation` of `x`, `y`, or `z`), `fluid` (materialized as a full source cell), or `clear: true`. An output connector may coexist with one primary payload; an input connector is connector-only. A template may opt into deterministic quarter-turn rotation. Restrictions include `groundBlocks`, `minSlope`, `maxSlope`, `requiresDryGround`, `requiredBiomeCoverage`, and bounded `proximity`. `groundBlocks` is checked against the authoritative surface material under the support footprint. Optional `groundAnchorY` chooses the authored layer that is fitted to the sampled ground; without it the lowest payload layer is used. Optional `clearAbove` clears a bounded number of cells above each authored payload column after terrain/generated field fluids exist, which allows rivers, ponds and waterfalls to carve their intended open volume without a second terrain owner.

Structures may also author `priority`, `conflictGroups`, and a `generation` object with `replacePolicy`, `fluidPolicy`, and `reserveSpace`. `replacePolicy` supports `any`, `air_only`, and `terrain`; `terrain` does not overwrite a cell already claimed by an earlier accepted structure. `fluidPolicy: forbid` rejects placements whose payload footprint would overlap generated field fluid. `preserve` remains unsupported while block/fluid co-occupancy is not part of voxel storage. During chunk synthesis, dimension/biome generated fluids are materialized first and accepted Structure operations then apply deterministic clear, block and fluid payloads through the same `SurfaceChunkMaterializer`; Structures never write chunks directly. Conflict resolution happens before chunk filtering: higher priority wins first, followed by stable rule and anchor tie-breakers; overlap conflicts when the higher candidate reserves space or both candidates share a conflict group.

StructureSets live under `data/structure_sets/*.json`. A set is one logical generated root composed from multiple existing Structure or Structure-group references:

Plains ground details extend the same pack-authored generation system: a sparse, irregular `palette.default[0].patch` alternates grass with dirt/gravel without painting circles, while Pebbles and Sticks accept the exposed alternative surfaces. Four `fallen_log_oak` rotated horizontal log variants (including hollow/stripped pieces) and three `oak_stump` templates use `groundAnchorY: 0` to preserve the actual surface voxels below them. Their dry-ground, flat-footprint and 100% biome-coverage restrictions prevent floating logs or edge bleed. The `rock_cluster` StructureSet composes small boulders with bounded distance and separation. All densities reside on the dimension's generated-surface rules; no Plains-specific engine branch was added.

Default Plains vegetation uses reusable authored content: `asteria:bush_oak` groups three irregular Structure templates made of existing oak leaves/logs, `asteria:thicket_oak` groups multiple shrubs into one bounded natural cluster, and `asteria:oak_grove` groups oak variants into small groves. Ground/slope/biome/fluid checks apply to every member. A Sphere chooses frequency in `generatedSurfaceStructures`; individual templates and StructureSets are dimension-agnostic. Willow keeps the authored fluid-proximity restriction and is less frequent than Oak in Plains.

```json
{
  "id": "asteria:example_set",
  "locatable": true,
  "priority": 4,
  "conflictGroups": ["natural"],
  "reserveSpace": true,
  "elements": [
    {
      "id": "root",
      "structure": "asteria:tree_oak",
      "required": true
    },
    {
      "id": "detail",
      "structure": "asteria:boulder_small",
      "count": { "min": 1, "max": 2 },
      "chance": 0.5,
      "placement": {
        "relativeTo": "root",
        "minDistance": 3,
        "maxDistance": 8,
        "minSeparation": 3,
        "attempts": 24,
        "allowOverlap": false
      }
    }
  ]
}
```

Elements are resolved in authored order. `relativeTo` may be `origin`, `any`, or an earlier element ID. Count, chance, placement attempts, annulus distance, separation, variant choice and rotation are deterministic functions of the world seed/root cell. Required elements reject the whole set when their minimum count cannot be placed. Internal overlap is rejected unless explicitly allowed. The set's `priority`, `conflictGroups` and `reserveSpace` apply to the complete logical root, while each piece keeps its own Structure restrictions and generation policy. Queries expose the shared logical root anchor even though individual pieces have independent voxel origins. Chunk boundaries never become StructureSet boundaries.

Palette entries may also author `connector`. A connector is a logical marker on the authored Structure grid and does not become a persistent voxel by itself. `target: null` (or omitted) defines an input connector and must be connector-only. A namespaced `target` defines an output connector and may coexist with a block payload:

```json
"palette": {
  "A": {
    "block": "asteria:stone",
    "connector": {
      "target": "asteria:bridge_segment",
      "face": "right",
      "strength": 1.0,
      "strengthLossOnEachLoop": 0.25,
      "minDistance": 1,
      "maxDistance": 3
    }
  },
  "I": {
    "connector": {
      "face": "left"
    }
  }
}
```

Connector faces are `right`, `left`, `top`, `bottom`, `front`, or `back`. Output targets may reference a Structure or Structure group. Generation aligns an input connector with the output world face, chooses variants/rotations/distances deterministically, rejects overlap across block/fluid/clear payload positions, and expands breadth-first while connector strength remains positive. Recursive connector cycles are valid only when strength decreases; unchanged-strength cycles are rejected because their world-space bounds would be unbounded. Connector expansion is part of the same logical Structure candidate, so conflict resolution, queries, cross-chunk materialization and streaming bounds all see the complete chain.

Structures may additionally author bounded `restrictions.proximity` rules. Each rule targets exactly one block or fluid, uses mode `required` or `forbidden`, has a required `maxDistance` capped at 64, and may set `minDistance` to form an annulus. Block targets query the authoritative exposed surface material; fluid targets query the existing generated-fluid owner one voxel above the target column's base surface. Proximity never creates terrain or fluid and does not introduce a hydrology subsystem.

Ground-level decorative blocks support the `visual.type: "groundSprite"` visual contract: a small horizontal cutout sprite with pack-relative `texture`, `width` (0..1), `height` and `baseOffset` inside its voxel. Unlike MineClone's separate object entity system, Asteria currently stores these as lightweight support-dependent voxel decorators. Their break/drop behavior follows the normal block mutation and drop pipeline.

### Arctic, Desert and Wasteland natural habitats

The default Overworld pack now uses the same world-space `surfaceHabitats` / `habitatWeights` system for three sparse biomes, retaining their MineClone-derived terrain and layers. **Arctic** is divided into Frozen Plain (mostly open snow), Ice Fields (ice/blue outcrops), and Rocky Ridges (Pebbles and clustered boulders). **Desert** has Open Dunes and Sand Flats (sand-only top material), plus Sandstone Outcrops. Gravel patches and Pebbles were removed; Sandstone begins below the exposed Sand cap. **Wasteland** has Barren Flats (bare soil), Deadwood Pockets (leafless oak snags and fallen logs), and Rocky Scrub (boulders, small rock clusters and Pebbles). Organic `palette.default[].patch` variations use warped world-space noise with bounded coverage, retaining the core materials; neither patches nor habitats modify height or create standalone biomes. Every new structure uses existing Block palette data, a bounded shape, dry-ground and 100% biome coverage restrictions. Wasteland deadwood sits above the authored surface and cannot turn dirt into grass. Existing four Wasteland boulder roots were made habitat-weighted instead of duplicated. All density parameters live in the biome and dimension JSON files; no engine-specific generation path was introduced.

Mountain biome enrichment uses the same generic content pipeline. The authored Mountains cliffs are now less abrupt, the ridges use modulated detail, and the mountain material palettes no longer create fixed-altitude stripes. Mountains authors Foothill Shelves, Talus Fields and Exposed Ridges; Mountain Belt authors Sheltered Passes, Boulder Runs and Bare Crests; Alps authors Snowfields, Glacial Ice and Wind-scoured Rock; Gorge authors Valley Floor, Rubble Slopes and High Rims. The first four profiles combine smooth deterministic habitat bands with limited, warped `palette.default[0].patch` regions on the *existing* terrain surface. The original Stone/Snow core remains below a shallow affected layer. Generic `conditions` use authored absolute altitude and slope thresholds: low, sheltered gentle slopes can contain Gravel/Dirt and sparse Grass/Sticks while Alpine upper shelves expose Ice/Gravel/Stone; steep and incompatible positions retain the original material. Pebble placement also has per-biome slope/altitude filters. Existing boulder roots are habitat-weighted, existing waterfall roots keep their old unweighted placement, and eight new habitat-weighted Structure roots use the generic `talus_outcrop`, `rock_cluster` and existing `ice_outcrop` templates. All Structure ground-fit, fluid and full-biome-coverage restrictions still apply. Each value can be tuned in the JSON pack without new biome-specific code. Note that material patches use 2D noise inside altitude/slope eligibility; habitats influence decoration/structure *probability*, not material patch ownership.

### Volcano: authored geology and preserved lava

The Volcano terrain definition retains its existing `cone`, `crater` and `crater.fluidFill` contract (including lava source level and spill noise); this enrichment does **not** add another lava producer, touch geometry, or invoke a Hydrology system. Its original Basalt remains the unlimited stone core. A bounded two-block Basalt cap has warped `palette.default[].patch` alternatives (Basalt Cobble and limited Gravel), eligible only on suitable lower/sloped ground, while all exposed upper and steep regions keep Basalt. Biome-owned `surfaceHabitats` organize **Ash Aprons**, **Fractured Flanks**, and **Hardened Highlands** as natural deterministic distribution regions: they influence Pebble and Structure placement probabilities, not the crater position. Four generated Structure rules place Basalt Cobble outcrops, larger Basalt boulders, narrow Basalt spires, and small `basalt_cluster` StructureSets, using six reusable JSON templates. Ground fit, fluid-forbidden policy, shared rock conflict group, required full biome coverage and biome-edge behavior all use the pre-existing generic Structure owner; lava-filled positions are excluded by the generated-fluid owner. No new Blocks or rendering effects are necessary. The names of the habitat bands are distribution regions, **not** guaranteed geographic rings around the crater; only existing altitude/slope conditions constrain material eligibility.

### Shared surface material/fluid mosaics

A surface biome may optionally author `palette.surfaceMosaic` for one
organic 2D noise-driven choice at each world X/Z coordinate:

```json
"surfaceMosaic": {
  "scale": 24,
  "detailScale": 9,
  "detailStrength": 0.24,
  "entries": [
    { "block": "asteria:grass_block", "weight": 2 },
    { "block": "asteria:dirt", "weight": 2 },
    { "block": "asteria:mud", "weight": 2.5 },
    { "fluid": "asteria:water", "weight": 3.5 }
  ]
}
```

The field selects **one** entry, not overlapping patches; block and fluid
IDs use their corresponding registries. Noise parameters and positive
relative weights are data-driven. Dry entries replace only the top
solid material. A fluid entry removes exactly the top solid voxel and
adds a source fluid in its place, at the same authored surface Y as the
dry alternatives; the regular `palette.default` layers remain under
the liquid. Carving and filling are resolved from the same immutable
`SurfaceMosaicField` through `GeneratedFluidField` and the existing
`SurfaceTerrainField` cut contract. Thus fluid queries, surface
decorators, Structures and chunk materialization agree on occupancy.
This requires normal-mode surface terrain with `fillToSeaLevel: false`;
Flat/Void world modes do not apply the mosaic.

### Ocean seabeds, cave floors, and additive floating island tops

Surface, volume, and underground biome identity remain distinct. **Ocean** has Sand Flats, Gravel Banks and Rocky Reefs with continuously blended, horizontal world-space placement weights. Its Sand top layer gets shallow warped Gravel/Clay/Stone Cobble patches, while two wet `ocean_rock` Structure templates and a bounded `ocean_rock_cluster` StructureSet add submerged rock formations. These are explicitly **fluid-displacing solid rock** payloads, not water sources. They can be anchored on the existing sand/gravel seabed because their `requiresDryGround: false` and `fluidPolicy: "displace"` use the already-authoritative generated-fluid checks and clear only cells occupied by rock. The existing ocean fill, beach margin and river mouth structure remain unchanged; no underwater ground sprites are overlaid into fluid voxels.

**Caverns** uses a generic Stone/Stone Cobble/Gravel/Dirt/Clay palette without Basalt, Sandstone or Terracotta. It declares sparse Pebbles, brown mushrooms and purple mushrooms as volume `decorations`; **Floating Islands** declares Grass, Sticks and purple mushrooms as volume `decorations`. The existing `SurfaceDecorationField` evaluates these same validated decorator rules using a 3D world-position roll on vertically exposed support surfaces. `SurfaceChunkMaterializer` remains the **sole chunk writer** and only places underground decorations where the authoritative terrain-density field carved a void with solid support immediately below; the `UndergroundBiomeField` determines that void's biome identity. For volume formations, every exposed disconnected additive island top is eligible, not just the highest surface in a column. Exposed support can cross a vertical chunk boundary without losing decoration. Non-carved stone, unowned volume coordinates, fluids and Sphere Shell cells are not decorated. There are no habitat bands for cave/volume biomes (these are surface-biome-only); their `cluster`, `conditions` and `chance` values use the existing generic decorator format. No new vegetation Blocks or separate cave/volume worldgen owner were introduced.

An optional biome-owned `surfaceHabitats` distribution organizes decorations and generated surface structures into persistent natural regions. Ordered noise bands describe habitats independently of chunk boundaries. Placement rules may author `habitatWeights` (multipliers 0..4, omitted bands treated as zero). Neighboring bands blend continuously over `transitionWidth`; the same deterministic noise sample and biome ID drive every participating rule. Unweighted rules keep their previous behavior. Habitat rules only alter candidate probability, not terrain height, biome identity, fluid ownership or Structure fit/conflict logic.

```json
"surfaceHabitats": {
  "scale": 128,
  "transitionWidth": 0.12,
  "bands": [
    {"id": "grove", "maximum": -0.3},
    {"id": "open", "maximum": 0.1},
    {"id": "thicket", "maximum": 0.34},
    {"id": "rocky", "maximum": 1.0}
  ]
}
```

For example, `decorations[].habitatWeights: { "grove": 2, "open": 0.4 }` raises sticks around groves and reduces them in open fields; `generatedSurfaceStructures[].habitatWeights` uses the same contract for tree, thicket and rock roots. The default Plains pack separates open grass meadows, Oak groves with fallen wood, shrubby thickets, and rocky areas with boulders/pebbles. Swamp reuses the same mechanism for willow groves, open mire, and fungus-rich ground; its two existing Structure roots are habitat-weighted, but tree/fluid proximity and slope checks remain authoritative. Enchanted Forest uses luminous clearings, violet undergrowth, and pink glades for its existing grass, mushrooms, and sticks (its own tree Structures are not yet ported). Wraith Grove demonstrates cross-Sphere reuse with dusky underbrush, pale clearings, and decay pockets; its brown mushroom decorator permits the biome's grass-block surface. Wraith Grove now has three distinct sparse-canopy spectral tree templates and two almost-leafless twisted snags, derived from the MineClone Willow/Enchanted Tree silhouettes but authored as independent `tree_wraith` and `wraith_snag` Structures. Their palette deliberately reuses pale stripped/hollow Enchanted logs and tintable Willow leaves so the Wraith Grove leaf tint remains authoritative; they require dry ground, slope fit, and full Wraith Grove biome coverage without Willow's water-proximity requirement. Umbral alone authors three habitat-weighted roots: spectral trees and small `wraith_grove` tree sets in Dusky Underbrush, dead snags concentrated in Decay Pockets, and few trees in Pale Clearings. This alters no other Sphere and introduces no Wraith-specific engine logic. There is no secondary biome registry or habitat-owned mutation state.

Any surface biome can author deterministic decorators through `decorations`; optional `cluster` noise limits eligible locations to irregular patches, independent of chunk boundaries:

```json
"decorations": [
  {
    "block": "asteria:pebble",
    "chance": 0.12,
    "surfaceBlocks": ["asteria:grass_block"],
    "cluster": { "scale": 14, "threshold": -0.12 }
  }
]
```

The `chance` roll is per world column and weighted by biome influence; `cluster.scale` is the horizontal noise wavelength and `cluster.threshold` (between -1 and 1) keeps columns above the fractal-noise cutoff. Missing `cluster` preserves existing independent decorator placement. Pebbles and Sticks use exposed compatible ground and are skipped for generated fluid-filled columns. These are microdecorations, not locatable surface Structures. Stick uses the same authored ground-sprite contract as Pebble (with a larger sprite), spawning in Plains, Swamp, Enchanted Forest and Wraith Grove. Both declare `interaction: "pickup"` and a `pickupItem` ID referencing a real pack Item. Right-click transfers one Item directly into inventory if capacity allows, then removes the voxel through `VoxelMutationRuntime` (no spawned dropped entity). Left-click mining/breaking is forbidden on pickup-only blocks. A tight ground-sprite targeting hitbox (`visual.targetHeight`, in voxels) prevents the whole air voxel from becoming an interaction target. Placement of normal blocks remains unchanged. The inventory catalog validates every referenced pickup item on initialization.

The Structure contract still deliberately does **not** support MineClone object attachments or Structure-authored surface-layer decorators; those require their own explicit owners before import. StructureSets, connector chains, Structure-owned source-fluid payloads and explicit clear cells are supported. Unsupported palette/template fields fail validation instead of being silently ignored. The default pack currently ports the four MineClone boulder geometries, four oak-tree block variants, three willow-tree block variants, four enchanted-tree block variants, and the 27 connected-water Structure variants used by lakes, mountain ponds, mountain waterfalls, river lakes, river segments and the ocean-margin river mouth. Plains references `asteria:lake`, while Mountains/Alps/Mountain Belt reference `asteria:mountain_waterfall`; Ocean references `asteria:river_ocean_mouth` with `biomeMargin`. River/lake/waterfall/pond expansion stays entirely inside generic Structure groups/connectors—no hydrology subsystem exists. Stick object cells and willow moss surface layers remain omitted. Willow preserves MineClone's required water proximity of 1..12 blocks. Swamp Water is selected from the same noise-driven `palette.surfaceMosaic` as Grass Block, Dirt and Mud. It occupies the same surface voxel rather than a terrain depression; proximity queries use the shared `GeneratedFluidField` owner. Enchanted Forest is active in the Overworld surface-biome pool.

Enchanted Forest reuses the MineClone rebuild's four `tree_enchanted` voxel templates, translated to the Asteria `groupId` schema, with dry-ground and full-biome-coverage fit requirements. The Overworld authors both rotated `tree_enchanted` roots and an `enchanted_grove` StructureSet with 2–5 companion trees; both use the existing `surfaceHabitats` density weights (Violet Undergrowth preferred, Pink Glade sparse, Luminous Clearing largely open). No new blocks, palette textures or ID-specific worldgen behavior are required. Generic Structure ownership keeps decorations coherent across chunks and honors conflicts with other trees.

A Sphere may define one explicit generated ocean rule:

```json
"generatedOcean": {
  "biome": "asteria:overworld/ocean",
  "fluid": "asteria:water",
  "shore": {
    "shelfDepth": 4,
    "beachHeight": 2,
    "beachStartDominance": 0.62,
    "shelfStartDominance": 0.72,
    "deepWaterStartDominance": 0.85
  }
}
```

The referenced biome must be in that Sphere's `surfaceBiomes` pool and the referenced fluid must exist in the selected pack. Ocean fill is generation-time content, not a separate hydrology layer: only columns whose authoritative primary biome matches the rule are filled, only density-empty voxels above the base terrain are eligible, and fill stops at `seaLevel` (or below an authored Sphere roof). Generated cells are normal full source fluid cells and enter the existing runtime fluid simulation after residency. Caves below the base terrain are not flooded by this rule.

A Sphere may also define bounded `generatedSurfaceFluids` for shallow local pools owned by a surface biome:

```json
"generatedSurfaceFluids": [
  {
    "biome": "asteria:overworld/swamp",
    "fluid": "asteria:water",
    "spacing": 18,
    "radius": 5,
    "jitter": 3,
    "chance": 0.75,
    "depth": 1
  }
]
```

Each surface biome may have at most one local rule. `spacing` is 2..512, `radius` is 1..256, `jitter` cannot exceed half the spacing, `radius + jitter` cannot exceed spacing, `chance` is within `(0, 1]`, and `depth` is 1..4. Presence is a deterministic world-space lattice patch derived only from the dimension seed, biome ID and coordinates. `GeneratedFluidField` owns patch presence/fluid identity and returns the shallow cut depth; `SurfaceTerrainField` applies that cut to its authoritative base surface and stores the resolved cut in the column snapshot. `SurfaceChunkMaterializer` then fills only the matching cut's density-empty cells with full source fluid cells. Ground decorators are suppressed inside a resolved cut so they cannot occupy the reserved pool volume. Runtime fluid simulation owns behavior after residency. No hydrology owner is introduced.

The default Overworld does not use `generatedSurfaceFluids` for Swamp or Volcano. Swamp instead authors `palette.surfaceMosaic`, selecting Grass Block, Dirt, Mud or Water from one continuous noise field; Water replaces exactly one exposed surface voxel and retains solid Mud below. It does not lower the terrain or use `fillToSeaLevel`. Volcano uses `surfaceTerrain.crater.fluidFill`. The generic bounded-pool capability remains available to packs that explicitly want it.

`shore` is the terrain-side coastal profile for that generated ocean. `shelfDepth` is the shallow shelf depth below sea level and `beachHeight` is the dry beach floor above sea level. The three dominance values are normalized pairwise ocean-vs-strongest-neighbor blend thresholds and must satisfy `0.5 < beachStartDominance < shelfStartDominance < deepWaterStartDominance <= 1`. The ocean-owned shore profile is independent of neighboring peak elevation. Its beach finishes inside the Ocean; higher terrain starts rising only once the higher land biome becomes primary.

A Sphere may additionally define a bounded subtractive cave field. This
is part of its single terrain-density owner, not a separate generator:

```json
"caves": {
  "minDepth": 10,
  "maxDepth": 120,
  "horizontalScale": 56,
  "verticalScale": 36,
  "noiseHalfWidth": 0.18,
  "densityScale": 24,
  "boundaryFade": 8
}
```

Caves are optional, occur below the exposed base surface, and cannot
puncture the top terrain crossing. Both scales are bounded. Depth and
boundary fade must fit within a finite positive underground band.

Surface and volume biomes share one authored `palette` and one
`decorations` system. The volume placement mode controls spatial
ownership, never a second biome class. A `carvedVoid` volume is
effective only where the authoritative Sphere cave density carved
space; `additive` volumes author bounded `terrain3d.additive`.

For a cave biome, floor, walls and ceiling can have independent
inward material depths and patches:

```json
{
  "id": "asteria:overworld/caverns",
  "volumeLayout": { "placement": "carvedVoid" },
  "palette": {
    "default": [{ "block": "asteria:stone" }],
    "floor": [
      { "block": "asteria:gravel", "depth": 2 },
      { "block": "asteria:stone" }
    ],
    "walls": [
      { "block": "asteria:basalt", "depth": 3,
        "patch": {
          "scale": 32, "coverage": 0.35,
          "blocks": ["asteria:stone", "asteria:terracotta"]
        }
      },
      { "block": "asteria:stone" }
    ],
    "ceiling": [{ "block": "asteria:stone" }]
  }
}
```

Only `palette.default` is required. `floor`, `walls` and
`ceiling` inherit from it when omitted. Each profile is an ordered
layer stack: all but the final core layer must have a positive
`depth`, and the final layer omits it. Depth on volume faces is
measured **into the solid from the exposed face** (not world-downward
for a wall). At equal-distance corners, floor takes precedence,
followed by walls, then ceiling. Patch masks are deterministic in
3D for volume faces and remain 2D for normal exposed surface columns.
Interior rock outside the profile's authored depth is unaffected.

Top-facing profiles on surface biomes and additive volume biomes
use the same layer and patch resolution that already governs their
terrain. Exterior wall/ceiling overrides are applied only when
authored, and never replace the cave volume's own palette.
`decorations` remains a sibling of `palette`: material choices
never implicitly spawn decorators.

Tintable blocks declare a semantic block tint category such as `grass`, `leaf` or `foliage`. Surface biomes may author matching RGB colors:

```json
"tints": {
  "grass": "3DB329",
  "leaf": "3DB329",
  "foliage": "3DB329"
}
```

Tint fields are optional individually. When a contributing biome omits a tint category, the block's authored preview color is the fallback for that influence. Runtime blends normalized biome influences in linear-light RGB and supplies the result as vertex tint only to dyable texture layers, producing gradual transitions across biome boundaries without changing terrain material ownership.

Data must not contain executable gameplay code. Native/code plugins are a separate future extension system.

Data definitions may reference presentation resources by logical pack-relative keys, but must not embed Godot-specific metadata.

## Ambient wind and particles

- Sphere environment `data/dimensions/*.json` may contain `environment.wind: { "direction": [0.93, 0.37], "strength": 0.45 }`. The vector defines a horizontal X/Z direction; strength is nonnegative. Absent settings use the engine-independent default. Wind has a single authoritative definition per Sphere.
- A block may opt in to GPU vertex sway with `"windSway": true`. It is intended for foliage and small vegetation, and does not change physics, voxels or chunk meshing per frame. All six terrain shader variants receive the same wind vector on Sphere activation.
- Ambient particle definitions live in `data/ambient_particles/*.json`. They use the MineClone schema: `id`, `source: { "type": "dimension" | "biome" | "fluid_surface", "id": "..." }`, HSI `color`, `opacity`, `size`, `lifetime`, `spawnRate`, `velocity`, `velocityJitter`, `acceleration`, `wanderStrength`, `windInfluence`, `popAtEnd`, `spawnRadius` and `verticalRange`.
- Source references are validated when the pack is loaded. Biome particles consult the 3D effective-biome query rather than assuming the surface biome is always active; fluid-surface emitters inspect resident fluid cells without changing them.
- The particle runtime is presentation-only and uses an explicit global cap of 512 particles, a 64-block maximum distance and bounded emission/sample attempts. Godot publishes at most one MultiMesh node per particle rule within the active Sphere subtree, and removes all particles on Sphere retirement. No pack-authored Godot metadata or per-particle gameplay nodes are needed.

## 5. Resource domain

`resources/` owns non-UI presentation assets: textures, item/world images, audio, fonts, models and related declarative presentation descriptors.

Definitions reference these by logical paths such as:

```text
textures/blocks/stone.png
```

The selected pack resolver maps that to:

```text
packs/{selected}/resources/textures/blocks/stone.png
```

## 6. UI domain

`ui/` owns UI presentation customization.

The initial supported contract is `ui/theme.json`, a declarative map of whitelisted semantic design tokens. The WebUI receives the selected theme through the existing Godot → WebUI bridge and applies only recognized CSS variables.

Current theme token categories include colors, borders, radii, spacing and typography. UI-specific images/fonts may later live under `ui/assets/` and use the same validated pack-relative resolution.

A pack **does not** replace the trusted WebUI application code. It must not inject arbitrary HTML or JavaScript, redefine IPC messages, own controllers, or bypass Atomic Design/bridge boundaries.

The built-in WebUI implementation remains under `ui/`; `packs/{name}/ui/` only supplies presentation data/assets.

## 7. Layering

Built-in/default content is the base layer.

When overlays are added, resolution order is deterministic:

1. base/default pack;
2. enabled overlays in explicit configured order;
3. later layers replace earlier content only through canonical keys.

Never use filesystem enumeration order, archive entry order, hash-map order or timestamps to decide precedence.

## 8. Names and paths

Pack folder names use lowercase ASCII letters, digits, `.`, `_` or `-`.

Pack paths are relative. Absolute paths, `..`, empty segments, backslashes and URI/scheme separators are invalid.

Path comparison/collision behavior is defined by Asteria and must be consistent across Windows/Linux/macOS.

## 9. Runtime boundary

Loaders validate the selected pack root, parse portable source data and produce resolved engine-agnostic/runtime presentation inputs.

Godot may consume decoded pack files to create runtime engine objects, but it does not own pack schema or gameplay definitions. Pack files are not Godot resources and must not be loaded through `ResourceLoader`/engine import metadata.

The WebUI consumes validated UI theme payloads through its controller boundary; individual components do not parse pack files.

## 10. Caching and compatibility

Derived caches are disposable, regenerate from authored pack contents and are versioned separately from the pack format.

While Asteria is pre-release, new pack-format versions do not promise backward compatibility unless explicitly required.

## 11. Security

Pack files are untrusted input. Loaders must enforce or evolve toward:

- bounded files/archive sizes;
- bounded decoded asset dimensions;
- path traversal rejection;
- duplicate/collision detection;
- schema validation;
- dependency-cycle detection;
- deterministic failure reporting.

Keeping data and UI customization declarative prevents pack installation from implicitly granting code execution.

## Imported item, tool and creature definitions

`data/items`, `data/tools` and `data/creatures` are separate authored categories, each loaded deterministically through typed Core registries. Items author category, icon and optional metadata-selected icon variants. Tools author icon, optional tint icon, left/right behavior IDs, and optional mining category/speed. Creatures author model, health, population cap, colliders, animation names, movement settings, material textures and particle effects. All referenced presentation assets use validated relative paths under the **selected** pack's `resources/` directory; pack JSON retains technical IDs and has no translated player-facing names.

Localized names and tool hints are authored under `data/localization/{english,portuguese_brazil,spanish}/{items,tools,creatures}.json` with definition-ID / JSON-Pointer entries. These definitions are declarative; implemented runtime consumers include inventory, selected tool actions, creature movement, limited natural spawning, targeting, combat, physical loot and Godot model presentation. Unsupported MineClone behavior IDs remain data only and do not imply working gameplay. Add future capabilities behind Core-owned boundaries.

## Creature gameplay runtime (initial slice)

- `CreatureRuntime` in `Asteria.Core.World` is the authoritative, dimension-isolated creature population. Stable instance IDs, health, per-definition `maxPerType`, 128-entity global cap, five-second distance-despawn grace and snapshot restore are checked by focused Core tests.
- `DimensionRuntimeSession` restores and archives creature population with the other dimension session state; no cross-Sphere entity reuse. `CreaturePresentationController` creates Godot scenes directly from the selected pack's GLB files using `GltfDocument`, without Godot imports or `ResourceLoader`.
- QA: while playing with the debug HUD enabled (F3), press **F5** to spawn an `asteria:slime_aqua` at a safe nearby generated destination, if the target chunk is resident and the authored population cap allows it. The worldgen destination capability owns safe placement.
- Basic slime hop phases (idle, anticipate, airborne, land), deterministic headings, loaded-world voxel collision, per-dimension gravity and visual animation switching are now driven by Core's `CreatureHopSolver`. Movement state is included in the per-Sphere creature snapshot. Creature combat, elemental loot and basic natural spawning are implemented in separate Core-owned runtimes. MineClone's `world-systems-rebuild` branch has natural spawning explicitly disabled; Asteria uses its own bounded authored policy and the existing generated-destination capability rather than copying the obsolete worldgen-scanning implementation.

## Player attacks and creature combat

- Author attacks in `data/attacks/*.json`. The default `asteria:punch` matches MineClone's 1 damage / guaranteed 0.75 knockback definition. Attack strength and chance are validated at load, and probabilistic effects derive from an instance-local hit serial retained by dimension snapshots.
- The Core `CreatureTargetQuery` chooses the nearest authored target collider on the player's ray, breaks exact-distance ties by stable creature ID, and rejects hits obscured by voxel raycast. Creature health, hit serial, effects and death are owned by `CreatureRuntime`; Godot only receives the attack input and publishes the model/HUD consequences.
- Left-click attacks a creature **only if it is the nearest unobstructed target**; otherwise the existing block break path runs unchanged. The existing entity health HUD shows the localized creature name and current/max health without separate authoritative UI state.
- Player damage is not yet ported. Creature hurt/death reactions and physical loot now use authoritative Core and the shared drop runtime.

## Creature hit and death presentation

- MineClone's creature combat attaches a short death timer after lethal damage and triggers authored `hurt` and `death` clips. Asteria now keeps the 0.75-second death lifecycle and 0.4-second hurt reaction in the authoritative `CreatureRuntime`, including remaining time in each Sphere snapshot.
- Attacks increment the instance's hit revision. The Godot presentation adapter replays a repeated `hurt` animation when the revision changes, shows `death` for the death grace interval, and returns to locomotion after the hurt reaction expires. Dying creatures are no longer hittable or selected by the creature ray query.
- Creature loot is emitted as physical, collectible items through the shared bounded drop runtime. Natural spawning is active only for explicitly authored habitat rules, and reuses generated-destination queries without scanning or reproducing worldgen.

## Authored elemental creature loot

- Each `data/creatures/*.json` may author `lootTable: [{ "item": "asteria:...", "chance": 1, "quantity": 1 }]`. Missing `chance` and `quantity` default to 1; all references must resolve to selectable pack items. Loot entries (maximum 16), quantities per entry (maximum 16), and chances in [0,1] are validated.
- The 18 elemental slime variants have **Asteria-authored** drops, not imported MineClone loot: normal variants yield one corresponding `essence_*` item; large variants yield two. Generic/legacy slimes have no loot. The authored default pack can rebalance this without modifying runtime code.
- `CreatureLootTable.Roll` is deterministic by creature instance ID and entry index and is evaluated **once** on the lethal player attack. The dimension session validates item IDs at construction and creates single-item `InventoryStack` drops through the **existing** bounded `DroppedBlockRuntime`, which owns falling, contact, pickup, visualization and Sphere snapshot state for blocks, items and tools.
- The 0.75-second death animation still plays; loot appears at the lethal hit. Attacking the dying creature again cannot duplicate the roll. Natural spawning is not enabled by these loot definitions.

## Authored natural creature spawning

- `data/creatures/*.json` may opt into spawning with `naturalSpawn: { "surfaceBiomes": ["asteria:overworld/plains"], "weight": 1 }`. Missing policy means **no** natural spawning. The initial authored pool enables only generic `asteria:slime` in Overworld Plains and Swamp; all other creatures remain manual/QA-only until explicitly authored.
- `CreatureNaturalSpawnRuntime` owns the per-Sphere attempt schedule (one candidate per ten seconds of world ticks), deterministic seed/tick placement and weighted selection from authored biome permissions. It asks `BiomeWorldGenerator` for **one** safe generated surface destination, then validates the current loaded/edited world, fluids, collision, player clearance and existing creatures. It never scans chunks or generates terrain.
- The world gamerule `spawnCreatures` gates every attempt. Attempts require an active player, resident chunks and available per-creature/global population slots. The next attempt tick survives Sphere retirement and re-entry; missed time never causes a burst of catch-up spawns.
- This is a deliberately bounded starter policy, not a port of MineClone's old natural-spawn algorithm: the rebuild branch disables that algorithm pending proper worldgen queries. Extend the JSON biome pools as creature habitat rules become defined.


Umbral Reach and Withered Waste also reuse exactly the same biome-owned `surfaceHabitats` distribution, without introducing new worldgen owners. Umbral Reach authors **Shadow Meadows** (grassier open fields), **Spectral Groves** (sparse `tree_wraith` and bounded `wraith_grove` StructureSets) and **Stony Fields** (pebbles, boulders and `rock_cluster`). Withered Waste authors **Dead Expanse** (mostly bare dirt), **Withered Thickets** (rare `withered_snag` trees and fallen deadwood) and **Rocky Barrens** (pebbles and boulder groups). Both surface material profiles keep the original authored top/core layers and add deterministic multi-scale warped dirt/gravel/stone patches, avoiding uniform circular shapes. The two `fallen_log_wraith` templates are ground-bound, dry-ground, full-biome-coverage adaptations of already ported fallen-oak geometry using the existing Enchanted stripped/hollow wood blocks, with no grass/leaf payload. All new placement probabilities remain in `packs/default/data/dimensions/umbral.json` and the decorations and habitat parameters in each biome JSON. No negative-world-Y or new fluid system is introduced.

The two `withered_snag` templates adapt existing sparse Wraith deadwood silhouettes but omit their base grass-block cell. With explicit `groundAnchorY: 0` and all remaining payload at Y>=1, these dead trees keep Withered Waste's authored Dirt/Gravel/Stone surface unchanged.
