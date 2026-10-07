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
| 3 — Biome Layout | Organic formation field, `regionSize`, weights, `cannotBorder`, primary + normalized influences, deterministic search, biome-map sampling. | **Ported / Adapted** | Add the optional debug biome-map renderer if still useful. The semantic layout itself is already ported. |
| 4 — Terrain | Continuous surface field plus authoritative 3D density, caves/floating terrain, bounded queries, seam/order independence. | **Ported / Adapted** | Continue only through the single terrain-density owner; no parallel terrain generators. |
| 5 — Surface/material/generated fluids | Deterministic layers, patches, generated natural fluids and runtime handoff. | **Ported / Adapted** | Ocean generation plus bounded authored swamp water puddles and volcano lava pools are owned by `GeneratedFluidField`; `SurfaceTerrainField` applies only the owner-provided shallow cut and runtime simulation takes over after residency. |
| 6 — Structures/features | One authoritative Structure placement/query owner; StructureSets, variants, conflicts, connectors/chains, biome/terrain/fluid restrictions and cross-chunk materialization. | **Ported / Adapted** | Authoritative queries, deterministic multi-piece StructureSets, connector/chains, fluid/clear payloads, biome-margin roots, and the lake/river/mountain-pond/mountain-waterfall content path are implemented through generic Structures. No hydrology subsystem exists. |
| 7 — Chunk synthesis | One procedural writer composes density/materials/features/structures/generated fluids into runtime chunks. | **Ported for current content** | Keep `SurfaceChunkMaterializer` as the single writer while Phase 5/6 capabilities expand. |
| 8 — Consumer integration | Streaming, biome/Structure locate, spawn, warp and dimension travel consume narrow generator query capabilities; no hidden chunk generation. | **Ported / Adapted for active consumers** | Biome and Structure search plus exact-first/surface destination preparation are generator-owned and query-only. Initial spawn and dimension travel use destination preparation. Asteria currently has no local-warp or /locate gameplay consumer; when added, they must call these existing capabilities rather than scan/materialize chunks. |
| 9 — Persistence | First materialization makes a chunk authoritative persisted spatial state, including unedited/empty chunks; query-only access does not persist. | **Ported / Adapted for current persistence owner** | Session archive now retains every materialized chunk zero-copy, including pristine/empty chunks; dirty state is tracked separately. Query-only generator access never enters persistence. Asteria still has no disk-save subsystem, so this phase applies to the current per-dimension session persistence owner rather than inventing one. |
| 10 — Loading pipeline | New world, save load and dimension travel share one loading path using real required-residency work. | **Missing parity** | Introduce the shared loading/residency pipeline after Phase 8/9 semantics are settled. |
| 11 — Loading screen | UI renders only authoritative loading phase/progress; no fake timers or duplicate loading state. | **Missing parity** | Add only after the loading owner exists; WebUI must remain presentation-only. |
| 12 — End-to-end/performance | Fixed-seed fixtures, near/far/order tests, visual probes and evidence-based performance baselines. | **In progress, but not a substitute for missing phases** | Keep benchmarking, but close Phases 6/8/9/10/11 before calling the rebuild migrated. |

## Immediate migration order

Until parity is closed, worldgen work should follow this order unless the user explicitly changes priority:

1. **Phases 10–11 — shared loading + presentation**
   - one loading pipeline for world entry/load/dimension travel;
   - progress is real residency/materialization work;
   - WebUI renders authoritative progress and owns no world-loading state.

2. **Phase 12 — close validation/performance**
   - deterministic fixed-seed fixtures;
   - cold/warm scalar and bounded queries;
   - near/far destination paths;
   - request-order and concurrent-query equivalence;
   - biome boundaries/junctions, ocean/coast, caves, floating terrain and Structure-heavy fixtures;
   - measured budgets only after the correct path exists.

## Generated-world consumer audit

- **Streaming/residency** — preserved. `ChunkStreamingController`/Core residency consume `IChunkSurfaceRangeProvider` and `IChunkProvider`; they do not own biome/terrain/Structure truth.
- **Biome search / future locate** — `BiomeField.FindNearestSurfaceBiome` owns deterministic formation-aware search and `BiomeWorldGenerator.FindNearestSurfaceBiome` exposes it. Asteria currently has no `/locate biome` command.
- **Structure search / future locate** — `SurfaceStructureField.FindNearest` remains the authoritative bounded search and `BiomeWorldGenerator.FindNearestSurfaceStructure` exposes it. Asteria currently has no `/locate structure` command.
- **Initial spawn** — `DimensionRuntimeSession` uses `BiomeWorldGenerator.FindGeneratedSurfaceDestination` around authored spawn X/Z with a 64-block radius. A restored session position remains authoritative.
- **Local warp** — no current Asteria gameplay consumer. `FindGeneratedDestination` and `FindGeneratedSurfaceDestination` are the required generated-world preparation capabilities when warp is introduced.
- **Dimension travel** — explicit destination requests are transient, not written into saved session position. The target session tries the exact generated 3D destination first and falls back to a safe generated surface destination near the same X/Z.
- **Loading** — current streaming readiness consumes the resulting destination/streaming center, but a unified new/load/travel loading pipeline is still Phase 10.
- **Persistence** — every resident materialized chunk is archived on retirement, including pristine/empty chunks; dirty is independent mutation metadata. Generated queries never persist or materialize chunks.
- **Debug/map tooling** — F3/HUD does not reconstruct worldgen. The optional standalone biome-map renderer remains absent and is not a semantic owner.

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

The standalone MineClone PNG/legend renderer has not been ported as a required runtime system. Its absence does **not** mean the biome-distribution algorithm is absent.

### Deliberate Biome Layout difference

MineClone currently uses `BLEND_SCORE_BAND = 0.18`. Asteria uses a wider `0.50` band so tall biome profiles and visible tint gradients transition without artificial walls. This is an intentional Asteria adaptation, not a separate biome-layout algorithm.

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
- `SurfaceStructureField` owns generated surface-Structure placement/conflicts.
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

From the repository root:

```sh
dotnet run -c Release --project tools/WorldgenBenchmark -- \
  --seed 181960897289965 --dimension asteria:overworld \
  --center-x 0 --center-z 0 --samples 16 --area-size 16 \
  --chunks 2 --output worldgen-benchmark.json
```

The CLI is a query/CPU baseline, not proof that the rebuild is complete. Cold and warm runs must preserve identical content digests. Performance work must not redefine generated output, cache semantics or request-order behavior.

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
