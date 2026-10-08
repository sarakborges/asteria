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

Dimensions live under `data/dimensions/*.json`. A dimension is one authored world-runtime configuration; in-game, dimensions are called **Spheres**. It declares its stable ID, explicit `surfaceBiomes`, optional `volumeBiomes` and optional `undergroundBiomes` pools, sea level, gravity strength, spawn coordinates, optional Sphere Shell bounds and engine-agnostic environment presentation values. Surface-terrain heights are authored as offsets relative to that dimension sea level rather than baking an absolute world height into each biome. A root world seed is not duplicated into the pack; runtime derives a stable per-dimension seed from the world seed + dimension ID.

Example placement pools:

```json
"surfaceBiomes": [
  "asteria:overworld/plains",
  "asteria:overworld/ocean"
],
"volumeBiomes": [
  "asteria:overworld/floating_islands"
],
"undergroundBiomes": [
  "asteria:overworld/caverns"
]
```

A biome ID may not be repeated across placement pools. Surface entries must author `surfaceLayout` + `surfaceTerrain`; volume entries must author `volumeLayout` and the bounded volume capability required by that biome; underground entries must author `undergroundLayout`. A Sphere may only declare underground biomes when it also authors a cave field.

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

The Structure contract still deliberately does **not** support MineClone object attachments or Structure-authored surface-layer decorators; those require their own explicit owners before import. StructureSets, connector chains, Structure-owned source-fluid payloads and explicit clear cells are supported. Unsupported palette/template fields fail validation instead of being silently ignored. The default pack currently ports the four MineClone boulder geometries, four oak-tree block variants, three willow-tree block variants, and the 27 connected-water Structure variants used by lakes, mountain ponds, mountain waterfalls, river lakes, river segments and the ocean-margin river mouth. Plains references `asteria:lake`, while Mountains/Alps/Mountain Belt reference `asteria:mountain_waterfall`; Ocean references `asteria:river_ocean_mouth` with `biomeMargin`. River/lake/waterfall/pond expansion stays entirely inside generic Structure groups/connectors—no hydrology subsystem exists. Stick object cells and willow moss surface layers remain omitted. Willow preserves MineClone's required water proximity of 1..12 blocks. Swamp water is produced when the typed swamp terrain falls below the Sphere's authored sea level, so proximity queries resolve against the same generated-fluid owner used by materialization. Enchanted Forest is active in the Overworld surface-biome pool.

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

The default Overworld does not use `generatedSurfaceFluids` for Swamp or Volcano. Swamp water follows the typed swamp terrain below sea level; Volcano uses its authored crater geometry plus `surfaceFluid.type = "volcano_crater"`. The generic bounded-pool capability remains available to packs that explicitly want it.

`shore` is the terrain-side coastal profile for that generated ocean. `shelfDepth` is the shallow shelf depth below sea level and `beachHeight` is the dry beach floor above sea level. The three dominance values are normalized pairwise ocean-vs-strongest-neighbor blend thresholds and must satisfy `0.5 < beachStartDominance < shelfStartDominance < deepWaterStartDominance <= 1`. The coast is reshaped continuously on both sides of the biome boundary so the ocean-owned sand surface becomes dry before ownership changes to the neighboring biome.

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

Underground biomes are a separate identity domain layered on top of the authoritative cave carve:

```json
{
  "id": "asteria:overworld/caverns",
  "undergroundLayout": {}
}
```

An underground-only biome may omit `surfaceLayout`, `surfaceTerrain`, `volumeLayout` and `surfaceLayers`. `undergroundLayout` controls deterministic underground identity placement, but it never decides where air exists. The biome becomes effective only at voxels that the Sphere's existing cave field actually carved. Solid rock, the exposed surface and Sphere Shell boundaries never become underground biomes merely because their X/Z lies inside an underground region.

Volume biomes author their own horizontal placement and bounded additive 3D formation:

```json
{
  "id": "asteria:overworld/floating_islands",
  "volumeLayout": {
    "weight": 1,
    "regionSize": { "min": 192, "max": 384 }
  },
  "surfaceLayers": [
    { "block": "asteria:grass_block", "depth": 1 },
    { "block": "asteria:dirt", "depth": 4 },
    { "block": "asteria:stone" }
  ],
  "terrain3d": {
    "floatingFormation": {
      "minY": 200,
      "maxY": 280,
      "horizontalScale": 112,
      "detailScale": 40,
      "coverage": 0.55,
      "roughness": 0.18,
      "densityScale": 28
    }
  }
}
```

A volume-only biome omits `surfaceLayout` and `surfaceTerrain`; it cannot become the ground/surface owner. `volumeLayout` uses the same deterministic region-shaping inputs as surface placement, while surface biomes act only as internal no-volume competitors that bound volume regions. Floating bounds use non-negative absolute world Y with positive span at most 512 blocks. Additive density is emitted only for the authoritative volume owner. The exposed tops of disconnected masses restart their own material layering from that volume biome's `surfaceLayers`.

Exposed solid material for surface and volume biomes is authored with ordered `surfaceLayers`:

```json
"surfaceLayers": [
  {
    "block": "asteria:grass_block",
    "depth": 1,
    "patch": {
      "scale": 28,
      "coverage": 0.52,
      "roughness": 0.3,
      "blocks": ["asteria:dirt", "asteria:mud"]
    }
  },
  { "block": "asteria:dirt", "depth": 4 },
  { "block": "asteria:stone" }
]
```

Every entry before the last requires a positive `depth`; those depths accumulate downward from the exposed surface. The final entry omits `depth` and is the unlimited core material. A patch may exist only on a finite layer and replaces that layer's base block through a deterministic continuous world-space noise field. `scale` controls the broad material-region size, `coverage` controls how much of the layer is replaced, and `roughness` adds bounded smaller-scale irregularity. Patch boundaries are organic and continuous across chunk borders; radial/circular footprints are not part of the contract. Patch alternatives cannot repeat the base block. Material ownership follows the sampled primary biome; nearby biome influence weights blend terrain shape, not material identity.

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

Localized names and tool hints are authored under `data/localization/{english,portuguese_brazil,spanish}/{items,tools,creatures}.json` with definition-ID / JSON-Pointer entries. These definitions are available to runtime systems but do not themselves implement item interactions, creature spawning, AI, combat or rendering. Gameplay capabilities must be added behind Core-owned runtime boundaries; do not interpret a copied MineClone behavior ID as an implemented Asteria action.

## Creature gameplay runtime (initial slice)

- `CreatureRuntime` in `Asteria.Core.World` is the authoritative, dimension-isolated creature population. Stable instance IDs, health, per-definition `maxPerType`, 128-entity global cap, five-second distance-despawn grace and snapshot restore are checked by focused Core tests.
- `DimensionRuntimeSession` restores and archives creature population with the other dimension session state; no cross-Sphere entity reuse. `CreaturePresentationController` creates Godot scenes directly from the selected pack's GLB files using `GltfDocument`, without Godot imports or `ResourceLoader`.
- QA: while playing with the debug HUD enabled (F3), press **F5** to spawn an `asteria:slime_aqua` at a safe nearby generated destination, if the target chunk is resident and the authored population cap allows it. The worldgen destination capability owns safe placement.
- Basic slime hop phases (idle, anticipate, airborne, land), deterministic headings, loaded-world voxel collision, per-dimension gravity and visual animation switching are now driven by Core's `CreatureHopSolver`. Movement state is included in the per-Sphere creature snapshot. Natural spawning, attack/targeting, loot and item/tool behaviors are **not yet** implemented. MineClone's `world-systems-rebuild` branch has natural spawning explicitly disabled; do not copy its obsolete worldgen-scanning implementation. These capabilities must be added separately without giving Godot ownership of the simulation.
