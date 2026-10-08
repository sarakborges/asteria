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
| 5 — Surface/material/generated fluids | Deterministic layers, patches, generated natural fluids and runtime handoff. | **Ported / Adapted** | `GeneratedFluidField` owns ocean/static swamp fill, biome-authored volcano-crater lava + spill channels, and the reusable bounded `generatedSurfaceFluids` capability. Swamp depressions and volcano crater geometry come from `SurfaceTerrainField`; runtime simulation takes over after residency. |
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

MineClone currently uses `BLEND_SCORE_BAND = 0.18`. Asteria uses a wider `0.50` band so tall biome profiles and visible tint gradients transition without artificial walls. This is an intentional Asteria adaptation, not a separate biome-layout algorithm.

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

## Authored special terrain features (data-driven)

Special surface appearances are authored as **geometry + optional features**, rather than branching by biome ID:

- `surfaceTerrain.type: "cone"` is a reusable cone shape. The default volcano uses it, but the generator has no special volcano identity.
- `surfaceTerrain.crater` is independent of the base shape. Any surface terrain type can author a formation-centered depression using `depth`, `radius`, `irregularity`, `noiseScale`, `transitionWidth`.
- `surfaceTerrain.crater.fluidFill` optionally fills the depression. `fluid` names any registered fluid, `topLevel` is relative to the **dimension's sea level**, `minimumStrength` limits fill to the formation core. An optional `spill` defines `minimumStrength`, `maximumStrength`, `scale`, `width` and `level`. SurfaceTerrainField owns crater geometry, GeneratedFluidField owns generation-time fluid policy, and SurfaceChunkMaterializer alone writes voxels.
- `surfaceTerrain.influenceMode`: `blend` (default), `lowerOnly` (blended terrain constrained below unaffected neighbors) and `primary` (formation primary shape overrides neighboring heights) are authored data, replacing special volcano, ocean and swamp blending checks.
- `surfaceTerrain.fillToSeaLevel: true` opts the biome into the dimension's authored `generatedOcean` fill. There is no swamp-type special case; a dimension must still author `generatedOcean` for this to do anything.
- Primitive profiles remain useful geometry algorithms, but their shaping constants are now authored: dunes (`waveDirectionZ`, `broadScaleMultiplier`, `waveWeight`); swamp depressions (`pondBroadScaleMultiplier`, `pondDetailScaleMultiplier`, `pondBroadWeight`, `pondBias`, `pondTransitionWidth`, `pondSharpness`); gorge (`rimFalloff`, `floorFalloff`); alps (`detailSharpness`); cone (`slopeNoiseGain`).

The obsolete `surfaceFluid: { type: "volcano_crater" }` and `surfaceTerrain.type: "volcano"` contracts are deliberately removed, not kept as aliases. The default volcano's crater-fluid top stays 54 blocks above dimension sea level. Spill noise uses a new generic generation domain and may produce a different deterministic pattern.

### Generic terrain consolidation

The content-specific surface shape types are eliminated. Biomes are compositions of reusable shapes and modifiers:

| Default biome | Authored shape | Extra authored behavior |
| --- | --- | --- |
| ocean | `rolling` with negative `baseHeight` | `influenceMode: lowerOnly`, dimension ocean-shore/fill |
| swamp | `rolling` | `depressions` modifier, `fillToSeaLevel`, `lowerOnly` |
| mountains | `ridges` | optional `cliffs` modifier |
| alps | `ridges` | `detailMode: ridged` and `detailSharpness` |
| mountain_belt | `ridges` | `detailMode: modulated` (detail follows ridge strength) |
| gorge | `valley` | `rimFalloff`, `floorFalloff` |
| volcano | `cone` | optional `crater` + `fluidFill` + `spill` |

The `depressions` modifier can be added to **any** surface shape with `depth`, `broadScale`, `detailScale`, `broadWeight`, `bias`, `transitionWidth`, and `sharpness`. This replaces the special swamp height algorithm. Every modifier has a deterministic per-biome/index noise domain. Existing cliff and height-offset modifiers remain usable on any profile.

The former `ocean`, `swamp`, `mountains`, `gorge`, `alps`, `mountain_belt`, and `volcano` surfaceTerrain types are deliberately not supported: migrate content into the generic shapes rather than carrying aliases. Generalizing the swamp depression's noise domain changes its deterministic layout while retaining the authored shape contract.
