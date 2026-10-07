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

Dimensions live under `data/dimensions/*.json`. A dimension is one authored world-runtime configuration; in-game, dimensions are called **Spheres**. It declares its stable ID, explicit `surfaceBiomes`, optional `volumeBiomes` and optional `undergroundBiomes` pools, sea level, gravity strength, spawn coordinates, optional Sphere Shell bounds and engine-agnostic environment presentation values. Surface biomes author `baseHeightOffset` relative to that dimension sea level rather than baking an absolute world height into each biome. A root world seed is not duplicated into the pack; runtime derives a stable per-dimension seed from the world seed + dimension ID.

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

The referenced biome must be in that Sphere's active `surfaceBiomes` pool and the structure reference must resolve in the selected pack. `spacing` defines the deterministic candidate lattice, `chance` gates each candidate, and `jitter` offsets it within at most half a spacing cell. Placement is evaluated against the base surface rather than additive volume terrain. Structure footprint constraints are checked before materialization; accepted templates may cross chunk boundaries and expand the generator's vertical streaming range.

Block structure templates live under `data/structures/*.json`. The first supported capability is intentionally narrow:

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

`anchor` converts authored grid positions into offsets from the placement origin. `.` is empty; every other symbol must resolve through the palette. Palette entries currently support `block` plus optional block `orientation` (`x`, `y`, or `z`). A template may opt into deterministic quarter-turn rotation. Current restrictions are `groundBlocks`, `maxSlope`, `requiresDryGround`, and `requiredBiomeCoverage`; `groundBlocks` is checked against the authoritative surface material under the support footprint.

Block structures may also author `priority`, `conflictGroups`, and a `generation` object with `replacePolicy`, `fluidPolicy`, and `reserveSpace`. `replacePolicy` supports `any`, `air_only`, and `terrain`; `terrain` does not overwrite a voxel already claimed by an earlier accepted structure. `fluidPolicy: forbid` rejects placements whose block voxels would overlap generated fluid. The current block-only contract rejects `preserve` because block/fluid co-occupancy is not supported. Conflict resolution happens before chunk filtering: higher priority wins first, followed by stable rule and anchor tie-breakers; overlap conflicts when the higher candidate reserves space or both candidates share a conflict group.

Structures may additionally author bounded `restrictions.proximity` rules. Each rule targets exactly one block or fluid, uses mode `required` or `forbidden`, has a required `maxDistance` capped at 64, and may set `minDistance` to form an annulus. Block targets query the authoritative exposed surface material; fluid targets query the existing generated-fluid owner one voxel above the target column's base surface. Proximity never creates terrain or fluid and does not introduce a hydrology subsystem.

This block-only structure-template contract still deliberately does **not** pretend to support MineClone's object attachments, structure-owned fluid payloads, surface layers, connectors, or clear/layers-only cells. Unsupported palette/template fields fail validation instead of being silently ignored. `StructureSet` is supported separately through optional `data/structure_sets/*.json`: a set contains bounded ordered elements, each referencing a structure/group with authored count/chance/required state and bounded relative placement (`relativeTo`, distance annulus, separation, attempts, overlap). Set element counts, distances, attempts and total element count are hard-bounded. A generated surface root may reference either a structure/group or a set; set priority/conflict groups/reserve-space apply to the entire logical root, while the accepted pieces still use the normal structure fitting/restriction contract. The default pack does not activate any structure set yet; MineClone's `enchanted_heart` remains deferred until its dependent enchanted structures/capabilities are ready. The default pack currently ports the four MineClone boulder geometries, four oak-tree block variants, and three willow-tree block variants. Stick object cells and willow moss surface layers are omitted until their respective owners exist. Plains references both `asteria:tree_oak` and `asteria:tree_willow`; swamp references `asteria:tree_willow`. Willow preserves MineClone's required water proximity of 1..12 blocks. Because swamp does not yet author its own generated puddle/fluid feature, current willow water proximity is satisfied only by water that actually exists through the present generated-fluid owners, chiefly ocean/coast water. The inactive Enchanted Forest root is not imported into active Overworld rules.

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
