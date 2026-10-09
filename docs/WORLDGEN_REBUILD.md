# Asteria Worldgen Rebuild — MineClone parity map

Reference implementation and behavioral contract:

- repository: `sarakborges/mineclone`
- branch: `world-systems-rebuild`
- primary design: `docs/design/world-systems-rebuild.md`
- implementation status reference: `docs/design/world-systems-rebuild-status.md`

Asteria adapts that contract to Godot + C#, but the rebuild phases and proven world-generation invariants are the migration checklist. Deliberate Asteria improvements are allowed when they preserve the same ownership and behavioral intent and are documented explicitly.

This document is the active parity map for the migration. It must be updated whenever a worldgen phase moves forward. Do not treat isolated feature ports as completion of the corresponding phase.

## Status vocabulary

- **Ported** — the MineClone rebuild contract exists in Asteria with equivalent ownership/behavior.
- **Adapted** — equivalent contract exists, with a deliberate Asteria-specific implementation difference.
- **Partial** — part of the phase exists, but one or more required contracts are still missing.
- **Missing** — the phase contract has not been implemented.
- **Divergent** — Asteria currently implements a conflicting semantic contract that must be reconciled deliberately.

## Phase parity

| Phase | MineClone contract | Asteria status | Required work |
| --- | --- | --- | --- |
| 0 — Impact audit and external contracts | Audit every consumer of generated-world facts and classify preserved/adapted/rewritten/obsolete dependencies. | **Ported / Audited** | Active consumers are inventoried below. Future generated-world consumers must enter through the same narrow generator capabilities rather than reconstructing worldgen. |
| 1 — Cleanup | Remove old biome/worldgen/loading ownership and repair/fallback paths before building replacement ownership. | **Ported for active worldgen** | Keep deleted/obsolete ownership from re-entering through compatibility helpers or consumer-side reconstruction. |
| 2 — Generation foundation/query model | Immutable deterministic generator; pure scalar + bounded queries; no semantic generation tiles; order-independent direct far-coordinate access. | **Ported / Adapted** | Preserve scalar/batch equivalence and bounded cache semantics as later capabilities are added. |
| 3 — Biome Layout | Organic formation field, `regionSize`, weights, `cannotBorder`, primary + normalized influences, deterministic search, biome-map sampling. | **Ported / Adapted** | Semantic layout, deterministic search and query-only biome-map diagnostics are implemented. Asteria emits SVG + legend instead of MineClone's PNG renderer. |
| 4 — Terrain | Continuous surface field plus authoritative 3D density, caves/floating terrain, bounded queries, seam/order independence. | **Ported / Adapted** | `SurfaceTerrainField` now supports the MineClone terrain profiles `noise`, `rolling`, `dunes`, `ridges`, `valley`, and `cone`, plus authored height/cliff modifiers, without adding a second terrain owner. |
| 5 — Surface/material/generated fluids | Deterministic layers, patches, generated natural fluids and runtime handoff. | **Ported / Adapted** | `GeneratedFluidField` owns ocean fill, biome-authored Volcano crater lava, authored surface mosaics and optional bounded `generatedSurfaceFluids`. Swamp uses a shared noise choice to replace one top solid with Water, rather than depression-based sea fill; runtime simulation takes over after residency. |
| 6 — Structures/features | One authoritative Structure placement/query owner; StructureSets, variants, conflicts, connectors/chains, biome/terrain/fluid restrictions and cross-chunk materialization. | **Ported / Adapted** | Authoritative queries, deterministic multi-piece StructureSets, connector/chains, fluid/clear payloads, biome-margin roots, and the lake/river/mountain-pond/mountain-waterfall content path are implemented through generic Structures. No hydrology subsystem exists. |
| 7 — Chunk synthesis | One procedural writer composes density/materials/features/structures/generated fluids into runtime chunks. | **Ported / Adapted** | `SurfaceChunkMaterializer` remains the single procedural writer for all current generation content. |
| 8 — Consumer integration | Streaming, biome/Structure locate, spawn, warp and dimension travel consume narrow generator query capabilities; no hidden chunk generation. | **Ported / Adapted for active consumers** | Biome and Structure search plus exact-first/surface destination preparation are generator-owned and query-only. Initial spawn and dimension travel use destination preparation. Asteria currently has no local-warp or /locate gameplay consumer; when added, they must call these existing capabilities rather than scan/materialize chunks. |
| 9 — Persistence | First materialization makes a chunk authoritative persisted spatial state, including unedited/empty chunks; query-only access does not persist. | **Ported / Adapted for current persistence owner** | Session archive now retains every materialized chunk zero-copy, including pristine/empty chunks; dirty state is tracked separately. Query-only generator access never enters persistence. Asteria still has no disk-save subsystem, so this phase applies to the current per-dimension session persistence owner rather than inventing one. |
| 10 — Loading pipeline | New world, save load and dimension travel share one loading path using real required-residency work. | **Ported / Adapted for active entry paths** | New-world entry, restored in-memory dimension sessions and dimension travel share `WorldLoadingState` over the existing streaming/residency pipeline. Bootstrap uses a 2-chunk horizontal radius, then expands to normal render distance after readiness. Asteria has no disk-save/load subsystem, so no fake save-load path is introduced. |
| 11 — Loading screen | UI renders only authoritative loading phase/progress; no fake timers or duplicate loading state. | **Ported / Adapted** | Godot emits `game.loading` from `WorldLoadingState`; WebUI's `LoadingOverlay` only renders phase/completed/total. Retirement is indeterminate `0/0`; materialization and destination presentation use real counters. No timer or synthetic percentage exists. |
| 12 — End-to-end/performance | Fixed-seed fixtures, near/far/order tests, visual probes and evidence-based performance baselines. | **Ported / Validated** | Multi-seed/order/concurrency tests, near/far biome/Structure/destination queries, allocation-aware cold/warm benchmarks, initial playable-area synthesis, evidence-based CI budgets, and fixed-seed Overworld/Umbral biome-map artifacts are active. |

## Migration status

The MineClone world-systems rebuild migration checklist is complete for Asteria's current runtime/content surface. No required phase remains **Partial**, **Missing**, or **Divergent**. Future worldgen/content work extends these owners; it does not reopen legacy ownership or add compatibility layers.

## Generated-world consumer audit

- **Streaming/residency** — preserved. `ChunkStreamingController`/Core residency consume `IChunkSurfaceRangeProvider` and `IChunkProvider`; they do not own biome/terrain/Structure truth.
- **Biome search / future locate** — `BiomeField.FindNearestSurfaceBiome` owns deterministic formation-aware search and `BiomeWorldGenerator.FindNearestSurfaceBiome` exposes it. Asteria currently has no `/locate biome` command.
- **Structure search / future locate** — `SurfaceStructureField.FindNearest` remains the authoritative bounded search and `BiomeWorldGenerator.FindNearestSurfaceStructure` exposes it. Asteria currently has no `/locate structure` command.
- **Initial spawn** — `DimensionRuntimeSession` uses `BiomeWorldGenerator.FindGeneratedSurfaceDestination` around authored spawn X/Z with a 64-block radius. A restored session position remains authoritative.
- **Local warp** — no current Asteria gameplay consumer. `FindGeneratedDestination` and `FindGeneratedSurfaceDestination` are the required generated-world preparation capabilities when warp is introduced.
- **Dimension travel** — explicit destination requests are transient, not written into saved session position. The target session tries the exact generated 3D destination first and falls back to a safe generated surface destination near the same X/Z.
- **Loading** — new-world entry, restored in-memory dimension sessions and dimension travel all enter the same `WorldLoadingState` path. The same `ChunkStreamingController` owns bootstrap residency at radius 2 and ordinary gameplay residency afterward; readiness is based on completed required residency plus the destination presentation needed by Godot collision/rendering.
- **Persistence** — every resident materialized chunk is archived on retirement, including pristine/empty chunks; dirty is independent mutation metadata. Generated queries never persist or materialize chunks.
- **Debug/map tooling** — F3/HUD does not reconstruct worldgen. `BiomeMapDiagnostic` and `tools/BiomeMap` sample the authoritative `BiomeField` and emit deterministic SVG/legend fixtures; they are consumers, not semantic owners.

## Biome layout / biome map clarification

The MineClone rebuild has two related but distinct pieces:

1. **Biome Layout** — the semantic generated-world owner implemented in `src/world/generator/biome.rs`.
2. **Biome map renderer** — the debug/visual tool in `src/world/generator/biome_map.rs`, which samples the same Biome Layout and writes a PNG + legend.

Asteria **has ported the Biome Layout**. `Asteria.Core/World/BiomeField.cs` mirrors the rebuild algorithm closely:

- deterministic organic formation seeds;
- seed spacing derived from minimum authored region span;
- weighted biome assignment;
- formation continuation/growth;
- `cannotBorder` filtering during assignment;
- deterministic absorption when a new compatible formation cannot be created;
- jittered formation centers;
- shape/bias entropy domains;
- coarse/fine domain warping;
- primary biome plus normalized local influences;
- bounded formation-assignment memoization;
- scalar and bounded-grid sampling.

Asteria also ports the diagnostic role through `BiomeMapDiagnostic` + `tools/BiomeMap`. The tool samples the same authoritative `BiomeField` and emits deterministic SVG + legend output. The SVG choice is a tooling adaptation; it owns no biome semantics. CI publishes fixed-seed Overworld and Umbral maps for visual review.

### Deliberate Biome Layout difference

MineClone currently uses `BLEND_SCORE_BAND = 0.18`. Asteria's default Sphere packs author a wider `biomeBlending.scoreBand: 0.50` so tall biome profiles and visible tint gradients transition without artificial walls. The value is now a validated per-Sphere setting, not a separate biome-layout algorithm.

### Terrain-profile extension

Asteria keeps the rebuild branch's single `SurfaceTerrainField` owner but restores the richer MineClone terrain profiles from `main` as authored variants of that owner. Formation strength is derived from Asteria's existing organic biome formation field and is consumed by strength-shaped terrain such as swamp, gorge, and volcano. Volcano geometry owns its cone/crater shape; `GeneratedFluidField` only fills the authored crater/spill result and never invents terrain.

## Current generation ownership

```text
BiomeWorldGenerator
  |- BiomeField
  |- VolumeBiomeField
  |- UndergroundBiomeField
  |- SurfaceTerrainField
  |- BiomeSurfaceMaterialField
  |- SurfaceDecorationField
  |- GeneratedFluidField
  |- SurfaceStructureField
  |- GeneratedSurfaceDestinationQuery
  `- SurfaceChunkMaterializer
```

Binding ownership rules:

- `BiomeField` owns surface biome identity/influences.
- `VolumeBiomeField` owns bounded volume-biome identity.
- `UndergroundBiomeField` owns underground identity only where authoritative cave geometry creates a void.
- `SurfaceTerrainField` owns base terrain and final 3D density.
- `BiomeSurfaceMaterialField` owns generated solid-material classification.
- `SurfaceDecorationField` owns ground decorators.
- `GeneratedFluidField` owns explicit generation-time fluid placement; runtime fluid simulation takes over after residency.
- `SurfaceStructureField` owns generated surface-Structure placement/conflicts and Structure search.
- `GeneratedSurfaceDestinationQuery` composes generated-world queries for safe spawn/travel destinations without chunk materialization.
- `SurfaceChunkMaterializer` is the only procedural voxel writer.
- streaming/residency owns scheduling and publication lifecycle, not generated-world truth.

No later migration phase may create a second biome resolver, terrain sampler, generated-fluid planner, Structure planner or procedural chunk writer.

## Current known deliberate improvements over MineClone

Asteria may improve the reference when the ownership contract is preserved. Current documented examples:

- surface material patches use continuous deterministic world-space noise instead of MineClone's radius-based patch footprints;
- biome influence blending is wider to support Asteria's tall terrain profiles and tint transitions;
- Core/Godot separation keeps generated-world calculation engine-agnostic;
- negative world Y is prohibited and Sphere Shell boundaries are explicit dimension content;
- hydrology is prohibited; water-related authored features use their concrete owner or generic Structure/connectors.

These adaptations are not permission to skip rebuild phases.

## Reproducible benchmarking

The canonical CI fixture is:

```sh
dotnet run -c Release --project tools/WorldgenBenchmark -- \
  --seed 181960897289965 --dimension asteria:overworld \
  --center-x 0 --center-z 0 --samples 2 --area-size 4 \
  --map-size 64 --map-step 4 --chunks 1 --search-radius 1024 \
  --enforce-ci-budgets true --output worldgen-benchmark.json
```

The benchmark records cold/warm elapsed time, current-thread allocations and a generated-content digest for biome scalar/area/map queries, terrain scalar/area/density, near/far biome search, near/far Structure search, near/far destination preparation, chunk synthesis and the radius-2 initial playable-area synthesis used by loading.

Baseline from successful CI run `37696389347` on 2026-10-07:

| Metric | Cold | Warm |
| --- | ---: | ---: |
| biome map (4096 samples) | 85.3 ms | 48.6 ms |
| biome search near / far | 24.3 / 46.5 ms | 0.18 / 0.28 ms |
| destination near / far | 303.9 / 420.2 ms | 0.35 / 0.16 ms |
| Structure search near / far | 919.3 / 986.6 ms | 2.27 / 0.74 ms |
| chunk synthesis | 262.5 ms | 1.32 ms |
| initial playable area | 547.3 ms | 167.1 ms |

CI budgets intentionally include substantial runner headroom and apply only to this canonical fixture. Digests must remain identical between cold/warm passes; caches may change cost, never output.

Visual fixtures are generated with:

```sh
dotnet run -c Release --project tools/BiomeMap -- \
  --seed 181960897289965 --dimension asteria:overworld \
  --width 64 --depth 64 --step 8 --mode influences \
  --output biome-map-overworld.svg
```

CI publishes equivalent fixed-seed SVG artifacts for both `asteria:overworld` and `asteria:umbral`.

## Data-driven biome contours and surface-patch morphology

The Sphere-level `biomeBlending.contourHarmonics` list configures the
organic shape of each biome formation. Each harmonic defines `lobes`
(1..12) and `amplitude` (0..0.4). Up to four harmonics may contribute,
with a combined amplitude no greater than 0.75 to keep formation radii
positive. An empty list yields a circular contour; the default is
`[{lobes:3, amplitude:0.13}, {lobes:5, amplitude:0.07}]`.
`sizeExponent`, `seedBiasAmplitude` and `continuationBonus` tune the
authored formation score without changing its deterministic ownership.
The same contour computation is used for placement scoring and formation
terrain-strength falloff; additional harmonic phases have distinct
stable generation domains. The default Overworld preserves its original
parameters; Umbral authors a different contour to show the extension.

Surface `patch` supports independently authored noise scales:
`detailScale` (default `max(2,scale/4)`) and `selectionScale` (default
`scale*2`). The optional `warpScale`, `warpStrength` (default `0`)
and `stretchZ` (default `1`) deform the noise coordinates into less
circular/anisotropic patches. Warp samples are evaluated only when enabled,
and the original integer-value-noise path is preserved when deformation
is disabled. All scales and amplitudes are validated and bounded.

`patch.weights` is an optional block-ID-to-relative-weight object for
the existing `blocks` alternatives (unspecified weights default to 1).
The material registry validates the named blocks and precomputes
cumulative weights once; runtime queries never create weighted
collections. Patches remain resolved by the authoritative
`BiomeSurfaceMaterialField` on world-space X/Z, so scalar and chunk
synthesis use the same material choice. There is no per-chunk selection
state or secondary terrain generator.

Example:

```json
{
  "patch": {
    "scale": 24,
    "coverage": 0.24,
    "roughness": 0.34,
    "detailScale": 9,
    "selectionScale": 64,
    "warpScale": 72,
    "warpStrength": 12,
    "stretchZ": 1.6,
    "blocks": ["asteria:dirt", "asteria:gravel"],
    "weights": {
      "asteria:dirt": 3,
      "asteria:gravel": 1
    }
  }
}
```

Patch noise morphing intentionally changes authored worldgen patterns;
existing material-depth, placement, no-negative-Y and chunk-query
contracts are unchanged.

## Conditional surface materials and decorators

Surface patches and ground decorators can share an optional `conditions`
object, evaluated against the owning biome's **authoritative terrain
surface** at the world-space X/Z column. Surface biomes read the base surface;
volume biomes read the final additive surface, so floating masses do not
change the placement conditions of the underlying ground:

- `minY` / `maxY`: inclusive, non-negative surface-altitude bounds.
- `minSlope` / `maxSlope`: inclusive maximum adjacent terrain height
  difference (blocks vertically per one horizontal block), sampled from
  the four directly adjacent X/Z positions. The condition uses the same
  world-space queries on chunk boundaries, so there is no chunk-local
  discontinuity.
- Any omitted bound is unconstrained. A conditional **surface patch**
  falls back to its authored base layer block when conditions fail; a
  conditional **decorator** is skipped. The original biome/terrain/material
  owner remains unchanged.

Example:

```json
{
  "patch": {
    "scale": 28,
    "coverage": 0.52,
    "roughness": 0.3,
    "blocks": ["asteria:mud"],
    "conditions": {"maxY": 94, "maxSlope": 2}
  }
}
```

Decorators additionally support `cluster.octaves` (1..6, default 3) and
`cluster.transitionWidth` (0..2, default 0). Zero preserves the
existing hard threshold; positive widths use smoothstep to reduce the
effective spawn chance across an organic noise transition. Biome influence
and existing `chance` still govern the final probability. No new
generation pass, chunk writer, or hydrology subsystem is created.

Terrain context is sampled lazily: unconditioned rules incur no extra
terrain queries, altitude-only restrictions query only the local column,
and adjacent heights are read only when at least one authored condition
needs slope. Content JSON belongs to the selected pack.

## Persistent world diagnostics and deep-material fast path

The Godot adapter writes `user://logs/worldgen-latest.log` automatically
at startup. Its absolute OS path is printed once to the Godot output; the
file is overwritten on the next client launch. Collection does not depend
on F3 or the WebUI. Every five seconds, it records actual chunk
materializations/restores, mean-independent total and maximum materializer
worker milliseconds, pending/in-flight/resident/presented counts, terrain
mesh-worker work, stale meshlets, and lighting-worker work. Materialization
errors are written immediately. Diagnostics report existing runtime facts;
they do not own queues, scheduling decisions or derived simulation state.

Worldgen materialization now checks the validated immutable deepest
material layer before resolving conditional surface patches. When the
whole requested voxel depth belongs to the core layer, it avoids patch
noise, terrain-condition queries and material-column allocations.
The current surface/volume biome remains authoritative and shallow
layers retain the original material selection. Scalar deep-layer queries
use the same bypass. This is especially valuable for underground vertical
chunk bands. For completely light-opaque chunks, direct lighting remains
authoritative and the local relaxation queue is skipped: no voxel in
such a chunk can transmit light. Partially empty or translucent chunks
continue through the full propagation algorithm.

The `asteria:dirt` block has no biome tint and a single base texture;
therefore a gray visual effect on Wasteland dirt should first be
investigated in voxel lighting/AO, face shading, geometry or adjacent
gravel layers, rather than changing Wasteland's grass/foliage tint data.

## Authored biome tint blends

The biome tint sampler blends only defined colors for the requested tint
channel. A neighboring surface biome without a leaf/grass/foliage palette
does **not** contribute a color. The sampler renormalizes the participating
influence weights to prevent `BlockPreviewColor.Missing` (magenta) from
desaturating oak foliage across Plains/Mountains and similar boundaries.
If none of the influencing biomes specifies that channel, the original
block preview fallback is preserved. This is purely material presentation:
world-space biome placement, tree generation and authoritative block tint
settings remain unchanged. Enchanted/world-tree foliage stays intentionally
untinted.

## Tree leaf biome tint

Oak and willow foliage use `tint: "leaf"` and tint-enabled texture layers;
`BiomeTintField` resolves authored surface-biome colors at mesh vertices.
Enchanted and world-tree leaves are **intentionally untinted**, with
`tint: "none"` and texture `dyable: false`; their authored intrinsic
texture color must be preserved. Do not generalize oak tint rules to every
leaf block. Regression coverage distinguishes these two contracts.

## Non-regression rules

- No legacy worldgen implementation may be restored for convenience.
- No compatibility shim for obsolete generation contracts unless explicitly requested.
- No semantic generation region/tile or chunk-owned generated truth.
- No duplicate biome-layout, terrain, generated-fluid or Structure owner.
- No hydrology subsystem, planner or abstraction.
- No negative world Y.
- Queries remain pure, deterministic and independent of chunk/request/task order.
- Scalar and bounded/batch APIs must remain semantically equivalent.
- Caches are bounded acceleration only; cache warmth/eviction never changes output.
- Runtime mutable-world questions may inspect `VoxelWorld`; untouched generated-world facts come from generator capabilities.
- Do not mark the migration complete while any required rebuild phase remains Partial, Missing or Divergent.

## Composable 3D terrain density (data-driven)

The single `SurfaceTerrainField` combines authored density contributions; neither
volume biomes nor cave layers write chunks or own a second voxel field.

- Volume biomes declare `terrain3d.additive: [ ... ]` instead of the old
  `terrain3d.floatingFormation` singleton. Every entry has its own bounded
  `minY`/`maxY`, `horizontalScale`, `detailScale`, `coverage`,
  `roughness` and `densityScale`. Optional `verticalFalloff`,
  `horizontalFalloff` (both default `1`) and `densityBias` (default
  `0`) control the density envelope without new biome-specific algorithms.
  Entries are combined with maximum density. Vertical gaps do not inherit the
  volume biome identity simply because another additive entry exists above.
- Spheres declare `caves.layers: [ ... ]`. Each layer has independent depth
  bounds (`minDepth`, `maxDepth`, `boundaryFade`), `noiseHalfWidth`,
  `densityScale` and `channels: [{ horizontalScale, verticalScale }, ...]`.
  `combination: "intersection"` takes the largest absolute channel noise,
  producing narrower overlapping voids; `"union"` takes the smallest,
  connecting the channels' carve regions. Different cave layers combine using
  the strongest subtractive density.
- Both contracts are bounded and validated. New entropy domains distinguish
  each additive entry and cave layer/channel; therefore fixed-seed shape
  details may change compared with the removed singleton algorithms. No
  compatibility parser for the old contracts is retained.
- Negative Y is forbidden; shell floor/roof remains authoritative and caves
  cannot pierce the local base surface. Scalar/volume queries and materialized
  chunks still use the same density field.

Example authored volume-biome layers:

```json
"terrain3d": {
  "additive": [
    {
      "minY": 200, "maxY": 280, "horizontalScale": 112,
      "detailScale": 40, "coverage": 0.55,
      "roughness": 0.18, "densityScale": 28,
      "verticalFalloff": 1.5, "horizontalFalloff": 1,
      "densityBias": 0
    }
  ]
}
```

The default Overworld and Umbral preserve their single cave layer and two
intersection channels as authored data; more layers can be added without
changing `SurfaceTerrainField`.

## Shared surface and volume biome palettes

Surface and volume biomes author the same `palette` and
`decorations` definitions. `palette.default` is required;
`palette.floor`, `palette.walls` and `palette.ceiling` are optional
profiles that fall back to `default`. All profiles contain ordered
`{ block, depth?, patch? }` layers with a depthless core layer.
`surfaceLayers`, `caveMaterials`, `undergroundLayout` and
`undergroundBiomes` are no longer authored inputs.

A `volumeLayout.placement` value of `additive` owns bounded
`terrain3d.additive` formations; `carvedVoid` owns regions only
within the authoritative Sphere cave carve. Caverns and Floating
Islands are both volume biomes. Neither can overwrite terrain merely
by sharing a horizontal column.

For exposed volume faces the palette is evaluated in 3D world
coordinates. Depth is measured inward from the chosen floor,
wall or ceiling face, across chunk seams, with floor > walls >
ceiling tie priority. The rock core beyond the authored paint
depth is unchanged. For base surfaces and additive tops,
`palette.floor` (or its inherited default) retains established
depth and 2D patch behavior.

The materializer remains the sole writer; `decorations` is
independent and supports both surface and volume biome identities.
No extra material sub-biome or parallel decoration pipeline is
required.

## Cave chambers and clustered spike formations

Cave chambers extend the existing dimension-authored subtractive density
layers through `caves.layers[].chambers`. They widen interconnected tunnels,
not a second carving owner. As with all terrain generation, the density
field is queried deterministically and the chunk materializer is the sole
voxel writer.

An underground biome can author `caveSpikes[]`. Each rule references a
registered block with `shape.type: "spike"` and configures:

- `block`, `chance`, `minHeight`, `maxHeight`, `minClearance`,
  and `directions` (`up`, `down`);
- optional `surfaceBiomes`: specialized materials take precedence over the
  unrestricted stone fallback at the same surface column;
- optional `minSpacing` (0..4): a deterministic horizontal priority
  neighborhood, stable across negative coordinates, directions, Y levels,
  and chunk borders;
- optional `cluster` with `horizontalScale`, `verticalScale`,
  `threshold`, and `transitionWidth`. It uses a smooth 3D noise
  envelope to concentrate growth into natural underground pockets.

Example (in `packs/default/data/biomes/caverns.json`):

```json
{
  "block": "asteria:stone_spike",
  "chance": 0.22,
  "minHeight": 2,
  "maxHeight": 9,
  "minClearance": 10,
  "directions": ["up", "down"],
  "minSpacing": 1,
  "cluster": {
    "horizontalScale": 32,
    "verticalScale": 22,
    "threshold": -0.28,
    "transitionWidth": 0.36
  }
}
```

All spike blocks share a **voxel-stepped square prism** presentation.
`shape.type: "spike"` accepts `baseRadius` and `tipRadius` as
square half-widths (0..0.5) plus `taperPower`. Each voxel contains up
to four height tiers, with widths snapped to the 1/32 voxel grid.
Every wall normal is aligned to X or Z, and the horizontal top,
bottom, and tier transition faces are closed and collidable.
Upward/downward formations use the same profile across chunk seams.
Polygon `sides`, radial corner `irregularity` and random rotation
are no longer part of the authored spike contract.

The initial stochastic chance and spacing priority are tested before
expensive cave-density probes. The 3D cluster envelope is then evaluated
only for eligible wall-adjacent anchors; final height is bounded by cave
clearance to avoid floor/ceiling spike intersections in narrow passages.
Rules are world-coordinate and seed-based, so cross-chunk materialization
does not depend on loading order. Stone, ice, basalt, sandstone and
terracotta reuse one spike geometry; no material-specific generation
branches belong in Core.

This changes spatial distribution relative to previous uniform cave-spike
rolls, intentionally. Do not add parallel chamber decorators, per-block
Godot nodes, or mutations outside the materializer to implement new
underground formations.

## Authored special terrain features (data-driven)

Special surface appearances are authored as **geometry + optional features**, rather than branching by biome ID:

- `surfaceTerrain.type: "cone"` is a reusable cone shape. The default volcano uses it, but the generator has no special volcano identity.
- `surfaceTerrain.crater` is independent of the base shape. Any surface terrain type can author a formation-centered depression using `depth`, `radius`, `irregularity`, `noiseScale`, `transitionWidth`.
- `surfaceTerrain.crater.fluidFill` optionally fills the depression. `fluid` names any registered fluid, `topLevel` is relative to the **dimension's sea level**, `minimumStrength` limits fill to the formation core. An optional `spill` defines `minimumStrength`, `maximumStrength`, `scale`, `width` and `level`. SurfaceTerrainField owns crater geometry, GeneratedFluidField owns generation-time fluid policy, and SurfaceChunkMaterializer alone writes voxels.
- `surfaceTerrain.influenceMode`: `blend` (default), `lowerOnly` (blended terrain constrained below unaffected neighbors) and `primary` (formation primary shape overrides neighboring heights) are authored data, replacing special volcano, ocean and swamp blending checks.
- `surfaceTerrain.fillToSeaLevel: true` opts the biome into the dimension's authored `generatedOcean` fill. There is no swamp-type special case; a dimension must still author `generatedOcean` for this to do anything.
- Primitive profiles remain useful geometry algorithms, but their shaping constants are now authored: dunes (`waveDirectionZ`, `broadScaleMultiplier`, `waveWeight`); swamp depressions (`pondBroadScaleMultiplier`, `pondDetailScaleMultiplier`, `pondBroadWeight`, `pondBias`, `pondTransitionWidth`, `pondSharpness`); gorge (`rimFalloff`, `floorFalloff`); alps (`detailSharpness`); cone (`slopeNoiseGain`).

The obsolete `surfaceFluid: { type: "volcano_crater" }` and `surfaceTerrain.type: "volcano"` contracts are deliberately removed, not kept as aliases. The default volcano's crater-fluid top stays 54 blocks above dimension sea level. Spill noise uses a new generic generation domain and may produce a different deterministic pattern.

### Authored coast profiles and biome blending

A Sphere can configure `biomeBlending` in its dimension data. These
parameters affect both surface biome placement and volume-biome placement:

- `scoreBand` is the maximum formation-score difference allowed into the
  normalized influence set (default `0.50`).
- `influenceCurve` is `linear`, `smoothStep` (default), or `smootherStep`.
- `jitterFraction` is the fraction of seed spacing used to perturb formation
  centers (default `0.32`).
- `coarseWarpPeriod`/`fineWarpPeriod` are noise periods in multiples of
  seed spacing (defaults `4`/`1.35`), while
  `coarseWarpStrength`/`fineWarpStrength` are displacement fractions of
  seed spacing (defaults `0.42`/`0.16`).

The authored values are validated and immutable. They tune the **existing**
`BiomeField` algorithm and must not introduce a competing layout owner.
Use larger `scoreBand` for gradual terrain/tint transitions, without
changing primary-biome ownership or circumventing `cannotBorder`.

The `generatedOcean.shore.samples` profile replaces fixed coast phases
(`shelfDepth`, `beachHeight` and hard-coded dominance thresholds).
Samples are sorted by strictly increasing `dominance`, from `0` to `1`,
and author `minimumHeight` relative to the Sphere's sea level plus an
effect `strength` between `0` and `1`. Samples interpolate with
smoothstep. At each sample, the coast can raise the ocean-owned terrain to an
authored minimum height. It does not borrow the neighboring land's peak
height; the taller terrain rises entirely inside the taller biome.
The first and last strengths must be zero, so pure land and pure ocean
are unaffected. This profile creates beaches/shelves **inside the existing
terrain owner**, never by generating water or carving separate geometry.

The default Overworld profile keeps the shallow shelf (`-4`), raised
beach (`+2`) inside Ocean and an uphill transition within the adjoining land biome; any future Sphere can
author a distinct coast contour without adding C# terrain special cases.
Only `GeneratedFluidField` decides actual generated water occupancy.

### Ocean seabed enrichment

The authored Ocean Sand surface remains continuous on the beach. Its
`palette.default[0].patch.conditions.maxY` limits Gravel, Clay and
Stone Cobble variants to ground below Y=86 (the default Overworld sea
level is 90); the patch stays warped and seed-stable rather than
painting randomly at the shore.

The existing `ocean_rock` group now contains four rotated templates:
two original Stone Cobble outcrops, one low Clay/Gravel shingle outcrop
and one wider layered Stone/Stone Cobble reef. Existing Structure and
StructureSet roots choose from that group without changing their
spacing, ocean size, fluid generation or terrain shape. Authored
`habitatWeights` favor Rocky Reefs, with fewer structures on Gravel
Banks and almost none on Sand Flats. All variants use existing blocks,
`requiresDryGround: false`, `maxSlope: 1`, complete Ocean biome
coverage and `fluidPolicy: "displace"` to replace only occupied water
cells with solid rock. No separate underwater placement owner exists.

### Solid coral reefs (first Ocean biodiversity slice)

The default pack adds three distinct solid coral blocks (Pink, Blue and Yellow),
with dedicated 16x16 pack-owned textures and two rotated `coral_reef`
Structure variants. They use the existing generated-Structure owner:
`requiredBiomeCoverage: 1`, sandy/rocky seabed support, low slope,
`fluidPolicy: "displace"` and an exact-distance required Water proximity
rule at the root. Biome-authored `habitatWeights` concentrate them in
Rocky Reefs, with fewer examples on Gravel Banks and sparse Sand Flats.
No new terrain or fluid generator exists; ocean shoreline and rock
formations retain their independent rules.

These are **solid coral formations**, not soft aquatic vegetation.
They replace the fluid voxels occupied by their solid bodies, just like
the existing underwater rock structures. Thin algae, seagrass, anemones
and shells that visually coexist with water require an explicit
fluid-compatible decoration/presentation contract; never generate
ordinary air holes by placing crossed sprites in water.

### Biome palette corrections (default pack)

- Desert uses Sand for exposed ground, Sandstone as an underlying
  layer, no surface Gravel patches and no Pebble decorators.
- Caverns limits common material patches to Stone, Stone Cobble,
  Gravel, Dirt and Clay. Basalt remains in Volcano-owned geology,
  with its authored Volcano-only cave-spike variant.
- Mountains and Mountain Belt no longer gate shallow material patches
  at a fixed Y level; Mountains soften the abrupt cliff modifier and
  ridged profiles use localized modulated detail.
- Swamp uses `palette.surfaceMosaic` to choose Mud, Dirt, Grass Block
  or Water at exactly the same Y. Water replaces the chosen top
  block without making depressions; normal fluid generation owns it.

### Shared block/fluid surface mosaic

`palette.surfaceMosaic` is an optional surface-biome selector.
`scale` and `detailScale` control a continuous, seed-stable 2D
noise field; `detailStrength` blends its secondary frequency, and
ordered `entries` carry positive weights with **exactly one** of
`block` or `fluid`. The selector is shared by
`BiomeSurfaceMaterialField` and `GeneratedFluidField`; each column
chooses one entry, never multiple independent patches.

Dry choices overwrite only the top solid block. Fluid choices use
the existing authored surface-cut machinery to replace exactly
one top voxel with a source fluid at the same Y as dry grass/dirt/mud.
The next solid layer remains beneath Water, with no depression,
sea-level fill or separate terrain owner. Generated queries, structure
fluid restrictions and decorators observe the same immutable outcome.
Mosaics are disabled in Flat/Void mode. Unspecified biomes retain
ordinary layered palettes with no extra noise cost.

### Generic terrain consolidation

The content-specific surface shape types are eliminated. Biomes are compositions of reusable shapes and modifiers:

| Default biome | Authored shape | Extra authored behavior |
| --- | --- | --- |
| ocean | `rolling` with negative `baseHeight` | `influenceMode: lowerOnly`, dimension ocean-shore/fill |
| swamp | `rolling` (flat local profile) | `palette.surfaceMosaic`, no terrain depressions or sea-level fill |
| mountains | `ridges` | optional `cliffs` modifier |
| alps | `ridges` | `detailMode: ridged` and `detailSharpness` |
| mountain_belt | `ridges` | `detailMode: modulated` (detail follows ridge strength) |
| gorge | `valley` | `rimFalloff`, `floorFalloff` |
| volcano | `cone` | optional `crater` + `fluidFill` + `spill` |

The `depressions` modifier can be added to **any** surface shape with `depth`, `broadScale`, `detailScale`, `broadWeight`, `bias`, `transitionWidth`, and `sharpness`. This replaces the special swamp height algorithm. Every modifier has a deterministic per-biome/index noise domain. Existing cliff and height-offset modifiers remain usable on any profile.

The former `ocean`, `swamp`, `mountains`, `gorge`, `alps`, `mountain_belt`, and `volcano` surfaceTerrain types are deliberately not supported: migrate content into generic shapes rather than carrying aliases. The optional depressions modifier is still available to other authored terrains; the default Swamp now uses a surface mosaic instead.

## Caverns biodiversity — emissive crystals and directional decorators

Caverns now uses four new default-pack blocks: `cave_crystal_azure`,
`cave_roots`, `cave_glowcap`, and `cave_glow_fern`, each with a 64×64
texture. Azure crystals use the existing square CaveSpikeField, with bounded
height, clearance, spacing and clustered probability. The glowing flora
are transparent, non-collidable crossed sprites with authored RGB
`lightEmission`; their placement uses the existing 3D decorator roll
and terrain-material restrictions.

Decorators support `supportSurface: "floor" | "ceiling"` (default `floor`).
A ceiling rule is eligible only inside an actual carved cave void, directly
below a density-solid ceiling of an allowed material. This works across
vertical chunk borders and uses the cave's `palette.ceiling` profile when
the supporting block is outside the current chunk. The resulting
`cave_roots` block has `support_above`, so runtime edits to its host rock
wake and detach the unsupported root through generic BlockPhysics.
Additive floating islands keep their original top-only decoration policy.
No new generated-fluid or lighting owner is introduced.

## Floating Islands: authored surface and underside objects

The `asteria:overworld/floating_islands` additive volume retains the
same `terrain3d.additive` configuration and its Grass/Dirt/Stone
palette. Four default-pack block objects add a distinct sky-island
character using only authored decorators:

- `sky_reed`: wind-swaying tall cyan-green grasses on Grass Block/Dirt.
- `aether_bloom`: sparse lavender flowers with subtle RGB emission.
- `aether_crystal`: occasional single-voxel, square-section spikes on
  Grass, Dirt or exposed Stone; existing shape renderer and emission.
- `sky_vines`: pendulous sprites on the underside of real additive
  solids, with `supportSurface: "ceiling"`, `support_above`, and wind sway.

Each uses an independent 64x64 pixel texture, height bounds,
deterministic noise clusters and authored support materials. The
vertical decorator pass resolves additive ceiling ownership from
the solid voxel **above** the empty position; it does not paint
sky/ground outside an additive island or introduce another world
writer. When support lies in the next vertical chunk, the sampled
material follows the exact additive depth/palette policy, avoiding
chunk-boundary differences. Existing cave ceilings continue to use
their CarvedVoid palette and original placement path. The terrain
density, island footprint, vertical band and base voxel materials
are unchanged.

## Arctic and Alps authored microflora and ice formations

Arctic and Alps keep their original terrain shapes, palettes, patch
settings and habitat definitions. The default pack introduces five
64x64-pixel objects using existing sprite/spike meshing and
`decorations` rules, not new hardcoded generator systems:

- `frost_shrub`: low, ice-rimmed vegetation native to Arctic frozen
  plains, concentrated on Snow and rare exposed Gravel.
- `frost_lichen`: compact pale lichen (groundSprite) shared by Arctic
  and Alps, favoring ice fields and glacial habitats.
- `glacial_shard`: short, square-profile ice crystals (spike shape)
  on Snow/Ice, infrequent outside icy habitats and flat supports.
- `alpine_edelweiss`: small pale flower, favoring sheltered snowy
  Alps slopes and avoiding sheer rock faces.
- `alpine_sedge`: wind-animated cold grass, focused on exposed
  rocky Alpine habitats at moderate-to-high elevations.

Authoring controls include allowed solid support blocks, continuous
cluster noise, minimum/maximum height, maximum slope and the
existing habitat weights. No biome terrain/palette/noise changes,
additional tile height, additive structures, or generated fluids
are introduced. The block definitions retain ordinary lower-face
support physics, so destroying the support breaks the plants.

## Gorge dry scrub, roots and small rocky outcrops

Gorge retains its `valley` terrain, dimension height, Stone/Gravel/Dirt/
Stone Cobble palette, habitat bands and biome borders unchanged.
Four deterministic, biome-owned decorators enrich the exposed
surfaces without any new terrain or runtime subsystem:

- `gorge_sagebrush`: dry sage scrub, concentrated on stable valley
  floors and rims rather than unstable rubble;
- `gorge_dry_tuft`: wind-responsive dry grass on sheltered Dirt/
  Gravel pockets, low slope and moderate elevation;
- `gorge_exposed_roots`: horizontal dry roots on Dirt, Gravel and
  Stone Cobble supports using a ground sprite (not unsupported
  cliff-wall attachment);
- `gorge_rock_spur`: sparse, single-voxel, square-section rocky
  formations on Stone, Gravel and Stone Cobble, concentrated
  toward rubble slopes and high rims.

Each object owns a 64×64 pack texture and uses the generic
`surfaceBlocks`, `habitatWeights`, `cluster` and altitude/slope
conditions. The existing block support lifecycle handles removal
of underlying solids. No material, hydrology or terrain-generation
behavior was modified.

## Desert surface enrichment

The default Desert keeps its existing `dunes` terrain, Sand (8) /
Sandstone (4) / Stone palette, three habitat bands and 2D biome
region rules. Four decorations enrich the *air voxels above* actual
Sand support rather than changing the terrain or introducing
random Gravel patches:

- `desert_dune_grass`: wind-animated dry grass on Sand, mainly
  in flat sandy habitats and rare on exposed dunes;
- `desert_thornbush`: sparse woody scrub on low-slope sandy
  pockets, weighted toward `sand_flats` and outcrop habitats;
- `desert_yucca`: stiff-leaved desert vegetation mainly in the
  `sandstone_outcrops` habitat and absent on `open_dunes`;
- `desert_sandstone_spur`: rare, single-voxel, square-section
  Sandstone-like formation favoring outcrop habitats and flat
  Sand support. It uses the generic Spike block shape.

Every new block has a pack-owned 64×64 pixel-art texture and
the same ordinary `support_below` physics. Rules author
`surfaceBlocks`, `habitatWeights`, `cluster`, height and
slope restrictions; their world-space entropy remains stable
across chunks. The Desert intentionally gets no Pebbles,
Gravel patches, generated surface fluid, erosion or terrain
alteration. No Core generator code was changed.

## Biodiversity gap review — stronger biome landmarks (2026-10-09)

The original small-decorator enrichments alone did not provide visual
hierarchy or sufficiently distinctive tall silhouettes. Review of the
default pack found Plains had many tree/boulder placements but only
Grass/Pebble/Stick decorators; Swamp had Willow trees and moss but no
large fungi; Desert had no cactus structures, despite its Sandstone
outcrop/spire roots; Arctic/Alps, Gorge and Ocean already had existing
ice/rock/coral structure groups, not just single block decorators.
Caverns and additive Floating Islands still need a separate
capability-review before adding multi-voxel, volume-anchored structure
placements: the current generated surface structure field is scoped to
surface biomes, not arbitrary 3D volume supports.

Current enriched content:
- Desert: 3 authored cactus families (Saguaro 3 variants, Barrel 2,
  Prickly Pear 2), with multi-block voxel silhouettes, colored
  flowers on supporting cactus tips and separate block textures.
  Each family has its own habitat-weighted generated structure root.
- Gorge: two additional flowering Prickly Pear variants adapted to
  dry canyon ground; these do not introduce Sand into the biome.
- Plains: added blue/yellow wildflowers and taller, wind-swaying
  grasses with independent clustered habitat probabilities.
- Swamp: added ferns favoring groves and fungal ground; irises
  requiring Water proximity; plus 3 giant mushroom templates with
  dry-ground restrictions and strong fungal-habitat weighting.

These additions reuse StructureRegistry groups, SurfaceStructureField's
authoritative conflict resolver, SurfaceDecorationField,
BlockPhysics support tags and existing shape/texture decoding.
No changes to biome terrain shapes, material mosaics, density fields,
dimension sea level, hydrological features or negative Y are included.
All new art is pack-owned 64×64 PNG.

Follow-up candidates after visual validation: grouped ceiling
formations in Caverns, larger canopies/landmarks on additive Floating
Islands with 3D volume-safe placement, denser cold-hardy Arctic/Alps
vegetation, and diverse Ocean reef silhouettes rather than simply
increasing indiscriminate surface spawn probability.

## Volume-owned multiblock landmarks

Author volume-biome `volumeStructures` entries with `structure` group
references, horizontal `spacing` and `chance`, `minY`/`maxY` support
bounds, and `supportSurface: "floor" | "ceiling"`. These are NOT
generatedSurfaceStructures: the 3D volume identity and authoritative
terrain density determine the support, not surface-biome coordinates.
Templates use the existing `structures/` schema; explicit
`groundAnchorY: 0` and positive/negative relative Y select which
side of the support carries the formation. Ground terrain itself is
never part of a template. Validated constraints include a compact
<=6-block footprint radius, <=16-block vertical offsets, unrotated
block-only payload, authored support materials, no fluids/clears,
and consistent support direction.

`VolumeStructureField` selects sparse seeded world-space X/Z anchors,
verifies actual solid-to-void boundaries, matching volume ownership,
support blocks and clearance of every template voxel, then returns
immutable world-space placements. The sole chunk writer remains
`SurfaceChunkMaterializer`. It emits formations before small vertical
sprite decorators and cave spikes, which then respect occupied voxels.
It consults anchors in a horizontal halo and full authored vertical
support band, preserving placements across X/Z and Y chunk seams.
Structure generation respects `spawnStructures`, normal world mode,
and carved-void presence. No modifications to terrain density or
surface/volume block palettes are required.

Caverns now authors 3 families with 7 variants: glow mushrooms
(4–8 blocks high), cyan crystal clusters, and hanging crystalline
chandeliers from ceilings. Floating Islands authors 2 families with
5 variants: branching skywood trees with 3D canopies and violet
crystal arches anchored on actual additive island solids.
Distinct 64×64 pack textures support the new blocks and RGB emission.

## Frozen biome landmark biodiversity — Arctic and Alps

Large-scale authored landmark families now complement the existing
ground decorations and boulder/ice-outcrop rules, without changing
the default palettes, surfaceTerrain shapes or biome borders.

**Arctic**: three variants of short wind-carved, snow-needled pines
on Snow; three woody frost-brush thickets on Snow; and three variants
of multi-height pale rime towers on *existing Ice surfaces only*.
Habitat weighting favors `frozen_plain` vegetation and `ice_fields`
crystal silhouettes, keeping the region recognizable as tundra rather
than covering it with trees.

**Alps**: four tiers of taller conical alpine snow fir (approximately
9–14 blocks high) on snowy ground; three different windswept dwarf
conifers rooted in natural exposed Stone; three split serac formations;
and two tall bridged ice arches. Seracs/arches require *existing*
surface Ice, and strongly prefer `glacial_ice`, while tall firs
prefer `snowfields` and dwarf pines prefer `wind_scoured_rock`.
No new Gravel, Basalt or random surface patches are introduced.

All landmarks reuse the existing `generatedSurfaceStructures`
configuration and StructureRegistry variant groups, normal
support/biome fit, conflict groups, seed-stable placement, chunk-border
rendering and fluid exclusions. Each template writes only one
ground-cell matching its authored groundBlock restriction
(Snow/Ice/Stone), preserving the actual surface material.
Six new block definitions use seven pack-owned 64x64 textures.
