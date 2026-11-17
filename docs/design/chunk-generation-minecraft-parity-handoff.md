# Handoff: Minecraft-style chunk-column generation pipeline

**Status:** per-dimension serial worldgen and lighting continuation lanes, shared physical worker pool, column-level generation tasks, executable stage policies, same-column vertical dependency futures, persistent vertical ticket targets, off-view ticket materialization and deferred holder unload lifecycle are implemented. General cross-column holder-status dependency admission, holder-owned dispatch queue-level parity and an executable loading pyramid remain incomplete.  
**Repository:** sarakborges/asteria  
**Working branch:** worldgen-perf-fix  
**Pull request:** [#157 — perf(world): optimize worldgen and voxel rendering](https://github.com/sarakborges/asteria/pull/157)  
**PR base:** worldgen-remake  
**Reference snapshot:** Asteria implementation HEAD 8f1f5c20b3422cc559b7ccd56c763984c54c6504 (2026-11-15)  
**Date:** 2026-11-15

This document is the durable restart point for the Minecraft-parity world-generation refactor. Work must proceed in small, independently reviewable commits. Do not merge the PR unless explicitly requested.

---

## 1. Objective

Rebuild Asteria's world-generation scheduling so its architecture follows Minecraft Java's model as closely as Asteria's engine, persistence rules and 3D content require.

The target is **not** “increase the number of Task.Run calls” and not “put every operation on one thread.” The target is:

1. one serial, per-dimension **world-generation coordinator/dispatcher** that owns task progression and dependency transitions;
2. one **logical generation task per horizontal chunk column** (X,Z) and target status, coordinating the relevant Asteria vertical sections;
3. an explicit ordered **step graph** with direct dependencies, accumulated dependencies, read envelopes, vertical dependencies and write radius;
4. **cooperative suspension and continuations**: a task advances until it encounters an incomplete asynchronous dependency, registers a continuation, returns immediately, and is re-enqueued when that dependency completes;
5. a **shared, bounded worker pool** for expensive independent calculations;
6. serial execution for operations whose write radius or mutation model requires ordered access;
7. a priority dispatcher whose queued work is reprioritized when the player's ticket/streaming priority changes;
8. main-thread integration/publication for authoritative live-world and Godot objects, with immutable worker snapshots and revision validation.

The desired result is Minecraft-like scheduling and throughput while retaining Asteria's deterministic generated content, 16×16×16 section storage, archival guarantees and engine boundaries.

The currently existing scheduler must be migrated incrementally. Do not replace a working subsystem wholesale before its replacement has tests and can be compared against the existing implementation.

---

## 2. Correct Minecraft reference model (26.3)

### 2.1 It is not one thread doing all generation

The accurate model is a **serial coordinator plus parallel forks**:

- A per-dimension worldgen ConsecutiveExecutor advances logical generation tasks one at a time.
- The backing executor is a shared background worker pool. Expensive independent steps can fork work onto that pool; multiple chunks can therefore perform expensive work concurrently.
- A separate light ConsecutiveExecutor coordinates lighting work over the shared backing pool.
- Steps that mutate a wider region or require ordered access are not treated like unrestricted parallel voxel fills.
- Final promotion/integration that must interact with the live level occurs on the server/main thread.

The serial executor is a coordination and ordering mechanism. It must not perform long CPU work or synchronously wait for worker futures.

### 2.2 The important Minecraft objects and contracts

| Minecraft Java 26.3 concept | Contract to reproduce in Asteria |
|---|---|
| ChunkGenerationTask | One logical walk for a chunk-column position and a target generation status. Holds current layer and scheduled futures. |
| runUntilWait() | Advance available steps until complete or blocked by an incomplete future; return without blocking a thread. |
| ChunkStep | Declares target status, direct dependencies, accumulated dependencies, block-state write radius and the step task. |
| ChunkPyramid | Defines ordered steps and the dependency radii required to reach each status. |
| GenerationChunkHolder | Owns status futures and performs exactly-once stage promotion/coordination. |
| ChunkTaskDispatcher | Serializes dispatch bookkeeping and drains one chunk's runnable batch at a time. |
| ChunkTaskPriorityQueue | Prioritizes work by ticket/level and supports priority changes after enqueue. |
| ChunkAccess / LevelChunkSection[] | A horizontal chunk is a column containing vertical 16-block sections. |

A queued continuation is not a blocked worker. If a required future is incomplete, the task registers its resume action and releases the serial executor so another column can progress.

The priority model is live. When a holder's level changes, queued work is reclassified/reordered; closeness to the player is not merely a priority computed once at original enqueue time.

### 2.3 Reference links

Use these as the baseline source references when making design decisions:

- [Minecraft 26.3 ChunkGenerationTask](https://aldak0.ru/javadoc/26.3.x/net/minecraft/server/level/ChunkGenerationTask.html) — runUntilWait, scheduled layer, target status and cancellation.
- [Minecraft 26.3 ChunkStep](https://aldak0.ru/javadoc/26.3.x/net/minecraft/world/level/chunk/status/ChunkStep.html) — direct/accumulated dependencies and write radius.
- [Minecraft 26.3 ChunkTaskDispatcher](https://aldak0.ru/javadoc/26.3.x/net/minecraft/server/level/ChunkTaskDispatcher.html) — serial dispatcher, level-change listener, submit/release/poll.
- [Minecraft 26.3 ChunkTaskPriorityQueue](https://aldak0.ru/javadoc/26.3.x/net/minecraft/server/level/ChunkTaskPriorityQueue.html) — priority buckets and per-chunk runnable batches.
- [Minecraft 26.3 ChunkAccess](https://aldak0.ru/javadoc/26.3.x/net/minecraft/world/level/chunk/ChunkAccess.html) — vertical LevelChunkSection[] storage.
- [Minecraft chunk generation pipeline research](https://minecraftdocs.dev/systems/world/chunk-generation-pipeline) — readable walkthrough of the dispatcher, suspension and dependency flow. Treat source Javadocs as primary when details conflict.
- [Chunk generation execution map](https://jsprismarine.github.io/JSPrismarine/research/java-chunk-generation-pipeline.html) — useful execution-context summary for parallel biome/terrain work, serialized feature mutation and server-thread promotion.

Do not repeat the previous incorrect explanation that the entire generation pipeline runs on one thread. Also do not mistake a serial coordinator for a one-worker pool.

---

## 3. Current Asteria baseline and concrete mismatch

### 3.1 Storage is 3D section-based

- ChunkCoord is currently (X,Y,Z) in src/Asteria.Core/World/VoxelCoordinates.cs.
- Chunk.Size is 16; each stored Chunk is a 16×16×16 volume.
- ChunkStreamingSelection.DesiredSurfaceChunks selects horizontal X/Z positions and inserts a vertical range of independent ChunkCoord values.
- This is a valid storage/persistence choice. **Do not replace Asteria's 16³ chunk storage with a monolithic tall column buffer.**

Minecraft's logical scheduling identity is horizontal X/Z. Asteria needs a separate horizontal column identity to coordinate its existing 3D section units.

### 3.2 Generation job is resumable in shape, but not scheduled cooperatively

Relevant files:

- src/Asteria.Core/World/IChunkProvider.cs
- src/Asteria.Core/World/SurfaceChunkMaterializer.cs
- src/Asteria.Core/World/ChunkResidencyRuntime.cs

Asteria already has:

- IIncrementalChunkProvider.BeginMaterialization(ChunkCoord);
- IChunkMaterializationJob;
- ChunkMaterializationStage;
- IChunkMaterializationJob.Advance();
- a private SurfaceChunkMaterializer.MaterializationJob that retains state between advances.

**Production-path change already committed:** the old ChunkResidencyRuntime.ExecuteMaterialization loop-to-completion was removed. A ChunkMaterializationGenerationTask now captures one IChunkMaterializationJob, submits exactly one Advance() call as a worker operation, waits through the completion mailbox, and resumes on the serial dispatcher. The private job is not accessed concurrently by the coordinator and worker. Stage timings are accumulated across slices; initial lighting is initialized on the final worker slice; the completed Chunk and metrics are returned as a terminal outcome. ChunkResidencyRuntime consumes that outcome on its owning thread, checks the streaming ticket/current residency, then performs InsertChunk and activation.

This is **section-granular cooperative execution, not yet the final Minecraft column task**:

- Each production task still targets one existing 3D ChunkCoord.
- Its key is (X,Z,SectionY,TargetStep), so different Y sections in the same horizontal column no longer collide in the dispatcher.
- There is not yet one column task that coordinates child sections, stages or their spatial futures.
- ChunkGenerationPlan currently describes and validates stages/envelopes, but the runtime adapter still advances the existing materializer's internal stage machine rather than executing that plan directly.
- A single IChunkMaterializationJob.Advance() may still be expensive. The adapter creates a scheduling boundary between existing stages; it does not yet subdivide every heavy stage into rows, candidate batches or other bounded micro-slices.

The stage-size audit and further subdivision remain mandatory before claiming full cooperative parity.

### 3.3 Streaming priority is recomputed by scanning pending 3D chunks

ChunkStreamingState still stores incoming, not-yet-started residency requests in _pending as a HashSet<ChunkCoord> and scans it to select the smallest current ChunkStreamingPriority. That source queue is still 3D-section based and has not yet been replaced.

Separately, the new ChunkGenerationTaskQueue / ChunkGenerationDispatcher own work **after a section has been admitted**. Their keys include X/Z, optional SectionY and target step; they support deterministic ordering, reprioritization and stable enqueue age. This is a real priority queue, but it is not yet the sole authoritative pending queue for all streaming requests. During migration there are two distinct admission layers, which must eventually be unified without losing streaming semantics.

The final column dispatcher must:

- deduplicate by logical task key;
- use deterministic priority ordering and deterministic tie-breaks;
- update/rebucket queued work when center, movement direction or ticket/streaming level changes;
- preserve enqueue age/fairness so distant work cannot starve indefinitely;
- remove or invalidate queued work when its target is no longer needed.

Do not simply wrap the existing HashSet scan in a new class and call it a priority queue.

### 3.4 World jobs are started by independent pipeline owners

src/Asteria.Godot/Main.cs still coordinates several independently owned lanes (fluid simulation, terrain mesh, lighting and fluid mesh) and gates their starts with MaxConcurrentWorldJobs. The new bounded dedicated worker pool is currently owned by each ChunkResidencyRuntime and is used by section materialization only. Arrival lighting/physics reconciliation still uses the old Task.Run compatibility lane; terrain/fluid mesh and simulation admission are not yet connected to this pool.

This is an intermediate state, not the target shared world worker pool. The next migration must move eligible domain work to shared admission while preserving each domain's data ownership, dependency stamps, priority and integration boundary. Do not turn Main into the scheduler implementation.

### 3.5 Some horizontal data is already shared; preserve and extend it

SurfaceTerrainColumnCache already shares immutable surface biome/base-height column data across vertical sections and stores density occupancy in vertical-band entries. VolumeStructureField already caches anchor results by rule and horizontal formation cell.

Do not describe all vertical section work as fully repeated. Profile each pass. The opportunity is to coordinate access and avoid repeated candidate scans, snapshots and dependency checks where the same X/Z column data applies to multiple Y sections.

Candidate hotspots to profile explicitly:

- SurfaceChunkMaterializer.PrepareTerrain;
- vertical structure candidate discovery and voxel filtering;
- cave spikes, vertical decorations, veins and wall layers;
- gravity support child jobs / support-only fallback;
- per-section boundary and lighting reconciliation;
- snapshot capture for terrain/fluid meshlets.

---

## 4. Target Asteria architecture

### 4.1 Logical column versus stored section

Introduce a value type:

- ChunkColumnCoord(X,Z): logical generation/priority identity.
- ChunkCoord(X,Y,Z): unchanged authoritative 16³ storage, archive and presentation identity.

A logical column owns coordination metadata only. It must **not** become a second mutable copy of voxel content, an alternate archive, or a new full-height voxel container.

A column task may coordinate many section-local jobs at different Y values. Sections can become eligible independently as their vertical and horizontal dependencies are satisfied. All section results continue through the existing atomic residency activation and archive contracts.

### 4.2 Step definition / dependency graph

Create a data model analogous to Minecraft's ChunkStep and ChunkPyramid. A step definition must describe, at minimum:

- stable stage ID and deterministic stage order;
- execution policy: serial coordinator, shared worker pool, ordered/write lane, or main-thread integration;
- direct dependency stage(s) and required column/section neighborhood;
- accumulated dependency envelope needed to reach the stage;
- horizontal read radius and vertical section read range/radius;
- horizontal and vertical write envelope/radius;
- whether its work is section-local or column-scoped;
- whether it may fork independent worker work;
- result type / continuation completion contract.

Keep this typed and engine-agnostic in Asteria.Core. Do not store stage names as free-form strings or make Main.cs reconstruct dependency policy.

### 4.3 Column task and suspension

Create a ChunkColumnGenerationTask (final name may change after design review) with:

- column coordinate;
- target stage/status;
- current stage/layer and vertical section work frontier;
- dependency futures for the current layer;
- bounded in-flight worker operations;
- cancellation/retirement token or generation identity;
- deterministic task revision;
- measured queued/running/waiting timestamps;
- completion/failure result.

Its advance contract must be equivalent to runUntilWait:

1. Run the next immediately executable state transition.
2. If a step has a completed dependency, consume it and continue.
3. If a step must fork CPU work, submit it to the shared pool and retain its future.
4. If any required future remains incomplete, register exactly one continuation and return.
5. When a future completes, publish a completion message to the serial coordinator; do not mutate dispatcher state from arbitrary worker continuations.
6. The coordinator re-enqueues the task using its current priority.
7. If the task is no longer needed, cancel/abandon work cooperatively where possible and reject stale results.
8. Complete exactly once, or fail/requeue explicitly. No drained task may disappear silently.

No worker may block on Wait(), Result, synchronous future joins, or a dependency that can be represented as a continuation.

### 4.4 Serial dispatcher and shared worker pool

Build distinct responsibilities:

- **World-generation dispatcher:** one serial progression lane per active dimension/session. Owns ready/waiting task transitions, stage advancement, queue mutations, continuation registration and dynamic priority changes. It performs only short coordinator work.
- **Shared bounded CPU worker pool:** executes independent, immutable-snapshot computations for terrain, biome/volume samples and other steps proven safe to parallelize. It must have explicit maximum active jobs and bounded pending work.
- **Ordered/write lane:** serializes steps whose write envelope overlaps other logical columns or whose behavior depends on ordered cross-column mutation. Prefer generating immutable placement plans and applying deterministic merges where safe; do not serialize all world generation by default.
- **Lighting coordinator:** should follow the same serial-coordinator / parallel-fork principle where appropriate, with its own ordered stage progression if required, while sharing the bounded backing CPU capacity.
- **Main-thread integration:** applies accepted chunks to VoxelWorld, integrates Godot objects, publishes meshes/collision and advances authoritative presentation state.

The number of serial coordinators is not the worker count. The number of logical column tasks in flight is not the number of CPU workers. Track these quantities separately.

### 4.5 Column scheduling and section scheduling

The column dispatcher selects logical columns by horizontal distance/ticket and direction. Each column task then schedules the specific Y sections needed for its target step, using the authored surface range, player-local range, volume biome ranges, shell bounds and portal access rules.

Do not require an entire arbitrarily tall column to finish before the player can use its nearby sections. The column task coordinates statuses; ready sections may complete and publish independently when their own stage and dependency contracts allow it.

Keep current bounded bootstrap semantics: horizontal bootstrap radius is capped at two columns (or configured render distance if smaller), with explicitly required portal vertical-access sections. Distant work must not block world entry after bootstrap readiness.

---

## 5. Proposed Asteria stage mapping (to validate, not blindly freeze)

The first implementation must translate current materializer stages into an explicit step graph, then adjust granularity based on profiles.

| Candidate step group | Current Asteria owners/work | Initial execution intent | Dependencies to model |
|---|---|---|---|
| Column metadata | BiomeField, SurfaceTerrainField, SurfaceTerrainColumnCache | parallel immutable column sampling; cached by X/Z | horizontal halo for biome/height blends |
| Section terrain setup/density | SurfaceChunkMaterializer.PrepareTerrain, density volume/masks | bounded worker work; share column metadata | same X/Z column and relevant vertical Y band |
| Terrain material layers | TerrainLayer stage | section-local worker step, subdivided by bounded Y/row slices | terrain setup and material field |
| Surface decorations | SurfaceDecorations | section/column step based on placement envelope | supporting surface, biome and neighboring slope samples |
| Directional face palettes | DirectionalFacePalettes | worker computation on immutable dependency snapshot | full one-voxel dependency envelope and revisions |
| Volume structures / cave spikes / vertical decorations / veins / wall layers | existing volume feature owners | worker plan generation if read-only; ordered deterministic apply if cross-section/cross-column writes | volume-biome ownership, density and support faces; vertical overlap |
| Generated fluids | GeneratedFluidField and materializer | deterministic plan generation, then mutation-owner integration | relevant terrain and authored feature stages |
| Surface structures | SurfaceStructureField and materializer | resolve placement/conflicts in world space before section rasterization | footprint-wide biome/material/fluid restrictions; write radius |
| Gravity support | AdvanceGravitySupport, support-only child jobs | dependency-aware vertical continuation; avoid redundant rescans | supporting sections below/adjacent; explicit missing-section state |
| Shell/pending objects | shell finalization, structure objects, pending objects | final section-local work | all relevant feature payloads complete |
| Initial lighting | ChunkLightingSolver.Initialize and LightingRuntime | ordered lighting coordinator plus bounded forks where proven safe | resident vertical/horizontal dependencies and arrival revisions |
| Section integration | ChunkResidencyRuntime / VoxelWorld | main-thread authoritative integration | completed task and valid revision |
| Terrain/fluid meshing | TerrainMeshPipeline, FluidMeshPipeline | bounded worker snapshot/build, main-thread publication | meshlet dependency stamps, absent-neighbor residency epochs |

Important: this table is a mapping exercise, not permission to collapse all systems into one oversized worldgen manager. Every step must retain the owner that currently owns the relevant domain invariant.

---

## 6. Correctness invariants that must survive the refactor

1. **Determinism:** same seed, dimension definitions and coordinates produce identical voxel, fluid, biome, structure and lighting inputs independent of worker count, completion order, queue order, cache warmth and streaming route.
2. **One authoritative owner:** column task metadata cannot become a second owner of voxel data, archive state, biome ownership, structure placements, physics or fluid state.
3. **Atomic section activation:** no partially materialized Chunk enters VoxelWorld; do not publish a section before all steps required for that section are complete.
4. **Archive semantics:** every first-materialized section, including pristine/empty sections, remains authoritative persisted state on retirement; restoring must not regenerate it.
5. **Immutable worker inputs:** workers receive immutable snapshots or isolated state; no unsynchronized live VoxelWorld reads/writes.
6. **Revision rejection:** every async result has task/session/request identity plus the exact dependency stamp needed to reject stale results.
7. **Retirement isolation:** once a dimension session retires, no new work starts for it; existing bounded work drains or is abandoned safely; no result can publish into a new session.
8. **Dependency correctness:** dependencies include all horizontal and vertical sections that a step reads or writes, including absent neighbors whose later residency would invalidate a snapshot.
9. **Write radius:** cross-column/section writes are serialized or converted to deterministic immutable plans with one authoritative mutation/merge owner.
10. **Negative Y remains prohibited.**
11. **Bounded lifecycle:** pending tasks, in-flight workers, wait registrations, continuations, dependency snapshots and cached section/column metadata all have explicit bounds and cleanup.
12. **No unbounded wait:** the serial dispatcher never blocks on an incomplete future.
13. **No starvation:** continuously arriving near-player work cannot permanently starve existing background tasks; queued task age participates in fairness without overruling urgent local work.
14. **Stable ordering:** all same-priority work has deterministic stable tie-breakers. Hash/set enumeration must not decide generation or publication order.
15. **Godot ownership:** Godot engine objects remain on the Godot thread; Core remains engine-agnostic.
16. **Loading truthfulness:** WorldLoadingState still observes actual resident and presented sections; no fake progress or second loading state.
17. **Existing gameplay behavior:** support/falling blocks, fluids, lighting, collision, attached layers, generated structures and all authored biome edge cases remain correct.

---

## 7. Commit-by-commit execution plan

Each item should normally be one small commit. If an item is too large, split it further before coding. Push every coherent commit directly to worldgen-perf-fix; run focused tests after each slice and the appropriate CI gate before proceeding.

### Phase 0 — Baseline and design lock

- [x] Record branch, PR, base and HEAD.
- [x] Inspect AGENTS.md, ARCHITECTURE.md and ENGINEERING_PRACTICES.md.
- [x] Inspect Asteria residency, materializer, selection, main-thread worker admission and mesh pipeline.
- [x] Verify Minecraft 26.3 dispatcher, step, task, priority queue and section-storage contracts.
- [ ] Preserve baseline benchmark artifacts and document their runner/seed/route details next to new measurements.
- [x] Commit this handoff.

### Phase 1 — Domain identity and tests

1. Add ChunkColumnCoord(X,Z) as a small Core value type with stable comparison and explicit conversion to/from a 3D section coordinate.
2. Add focused tests: same X/Z across Y maps to the same column; converting back preserves requested section Y; deterministic ordering; coordinate boundary behavior.
3. Add explicit column-vs-section naming in APIs where accidental mixing is possible.
4. Do not yet change the production streaming path.

### Phase 2 — Step graph model

5. Define typed ChunkGenerationStepId / status values (names should reflect Asteria's real domains, not copy vanilla labels blindly).
6. Define immutable step metadata: direct dependencies, accumulated dependencies, horizontal/vertical read envelopes, write radius, execution policy and section/column scope.
7. Define a small immutable ChunkGenerationPlan / pyramid for the current Asteria pipeline.
8. Add graph validation: unique stage IDs, valid dependency ordering, no cycles, valid radii, stable deterministic order.
9. Add tests for the dependency envelope and stage ordering independently from materialization.

### Phase 3 — Priority queue and dispatcher foundations

10. Implement a deduplicated deterministic priority queue keyed by logical column task identity and target stage.
11. Support priority updates/rebucketing when center, movement vector or ticket level changes; do not require remove/reinsert from every consumer.
12. Include stable tie-break and bounded age/fairness policy.
13. Test enqueue deduplication, cancellation, priority updates after enqueue, deterministic equal-priority order and no starvation.
14. Implement the per-session serial generation dispatcher that owns queue/task state transitions only. It must have no worker-blocking calls.
15. Test that a long-running/awaiting task yields the dispatcher so another ready column advances.

### Phase 4 — Cooperative task lifecycle

16. Define explicit task states: queued, advancing, waiting-for-dependency, ready-to-resume, completed, cancelled, failed.
17. Add a completion mailbox/channel from workers to the serial dispatcher. Worker continuations may publish completion messages but must not mutate dispatcher collections.
18. Implement a ChunkGenerationTask-equivalent state machine with RunUntilWait.
19. Register each continuation once; guard duplicate completion and stale task/session generations.
20. Handle exceptions, null result, worker-start failure, cancellation and session retirement explicitly; all work must be requeued or retired, never silently lost.
21. Add tests for incomplete future suspension, completion/resume, multiple dependencies, one failed dependency, duplicate completion, cancellation while waiting and completion after retirement.

### Phase 5 — Shared worker pool and step admission

22. Introduce one bounded CPU work scheduler for expensive world steps. Track queued work separately from active worker execution.
23. Make worker admission injectable/configurable for tests and deterministic benchmark variants; production default derives from actual runtime CPU capacity and is measured, not a magic fixed worker count.
24. Route only eligible CPU forks to the pool; keep the serial coordinator lightweight.
25. Add separate bounded admission for section/column tasks in flight, worker jobs active, and results waiting for main-thread integration.
26. Add tests proving a waiting continuation consumes no active worker permit and that active worker count never exceeds the configured cap.
27. Do not independently spawn materialization, lighting and other expensive jobs in ways that bypass shared admission.

### Phase 6 — Make materialization genuinely cooperative

28. Audit every ChunkMaterializationStage and classify its worst-case work size.
29. Split any unbounded/long stage into deterministic bounded slices (row, column batch, Y band, candidate batch or other domain-correct unit).
30. Preserve the existing IChunkMaterializationJob state between slices; each slice must leave state valid for a later resume.
31. Move stage timing/diagnostic capture to the scheduler so each unit's queue wait, run duration and continuation wait can be measured.
32. Keep a section private until all required materializer stages complete.
33. Add equivalence tests comparing old and new materialization output for representative seeds and all stage boundaries.
34. [x] Retire the ExecuteMaterialization loop-to-completion design; the serial dispatcher now owns each production materialization task's lifecycle. Remaining work: subdivide expensive stages and finish column orchestration.

### Phase 7 — Column orchestration over vertical sections

35. Implement a column task that coordinates child section jobs by ChunkColumnCoord and section Y.
36. Reuse horizontal column metadata once per X/Z; avoid rebuilding/serializing the same biome/height/structure candidate inputs for every vertical section.
37. Represent vertical dependencies explicitly: required lower/upper sections, bounded vertical access corridors and missing-section semantics.
38. Allow sections with complete local dependencies to finish and integrate without waiting for an arbitrarily tall column to finish.
39. Model gravity support as a dependency/continuation over the actual support sections; eliminate redundant child support materializations when the required result is already available from another in-flight section task.
40. Query VolumeStructureField/other spatial owners in a column-aware way so vertical sections do not repeatedly scan unrelated X/Z candidates.
41. Preserve section-level persistence, mesh identity and archival; the column task is orchestration only.
42. Test very tall terrain, floating formations, deep caves, caves crossing section boundaries, structures crossing vertical section boundaries, Sphere Shells and portal access.

### Phase 8 — Explicit worldgen dependency graph and ordered writes

43. Map each current materializer/field operation to the step graph and assign read/write envelopes.
44. Identify all operations that can mutate another section/column; give them an ordered lane or convert them to immutable deterministic placement plans.
45. Ensure a feature's write radius includes every neighboring section whose state it may modify.
46. Add deterministic conflict resolution before applying generated structure/feature operations.
47. Keep generator owners (BiomeField, SurfaceTerrainField, SurfaceStructureField, GeneratedFluidField, VolumeStructureField) authoritative; scheduler only orders their work.
48. Test cross-boundary feature placement under different scheduling orders and worker counts.

### Phase 9 — Lighting, fluid and mesh work integration

49. Integrate lighting progression with the shared worker-admission mechanism and an ordered lighting coordinator; preserve incremental neighbor reconciliation and revision rejection.
50. Integrate fluid activation/simulation as bounded work under shared admission without changing its authoritative simulation semantics.
51. Integrate terrain/fluid mesh workers with shared CPU capacity; preserve their independent priorities and meshlet dependency stamps.
52. Keep Godot mesh/collision object creation and publication on the main thread.
53. Remove the fixed four-lane start loop from Main.cs only after all relevant owners use the new admission API.
54. Re-evaluate the previous early-priority-mesh-during-bootstrap optimization under the new dependency graph. Keep it only if first-visible-terrain and correctness measurements support it.

### Phase 10 — Retire old scheduling and validate parity

55. Remove the one-Task.Run-per-section materialization lifecycle and redundant per-domain admission code.
56. Remove obsolete pending/priority state only after all consumers have migrated; do not maintain dual authoritative queues.
57. Run the full Core, Godot native, Debug diagnostics, rendering, Storybook and benchmark CI suite.
58. Run fixed-seed output-digest comparisons against the baseline implementation.
59. Run worker-count variants (1, 2, 3, 4 and machine-derived production setting) and verify identical generated content.
60. Benchmark cold and warm startup, initial bootstrap readiness, first visible terrain, fixed-seed movement route, tall vertical terrain, cave/floating volume terrain and portal arrival.
61. Report median and p95, frame gaps, queue latency, worker utilization, active/waiting task counts, cancelled stale work, allocations and section throughput.
62. Keep the PR open for user review. Do not merge.

---

## 8. Benchmark baseline and success criteria

### 8.1 Existing measurements (context, not final acceptance)

The latest controlled CI route run before this handoff was run 43356895366 (all three jobs succeeded). The benchmark used a fixed seed 181960897289965, Overworld, 8 movement steps and radius 4. The worker-cap comparison preserved the same digest for 235 activated 3D sections.

Observed approximate results:

| Global worker cap | Route elapsed | Frames with player-local section gaps |
|---:|---:|---:|
| 2 | 1.162 s | 633.5 |
| 3 | 1.224 s | 382 |
| 4 | 1.228 s | 382.5 |

Interpretation: the smallest total route elapsed time did **not** provide the best continuous local section availability. Three workers were materially better than two for local readiness; four did not materially improve local gap frames or total elapsed time. This says nothing about the best design once workers are coordinated through a single scheduler.

An earlier bootstrap profile at spawn around Y=95 selected 67 sections across 13 horizontal columns. Cold materialization was reported at ~199 ms CPU / ~82.5 MiB allocated versus warm ~84.9 ms / ~15.3 MiB. Treat these as diagnostic samples; rerun them from artifacts before using them as a performance claim.

### 8.2 Required benchmark dimensions

- Cold world startup versus warm cache startup.
- First playable bootstrap readiness and first visible terrain.
- Section count and unique horizontal column count.
- Route movement with fixed seed, fixed speed and fixed radius.
- Vertical movement through tall/multisection terrain.
- Surface, ocean, swamp, deep-cave and floating-formation terrain.
- Portal arrival with explicit vertical access.
- Heavy feature/structure columns with cross-section footprints.
- Archive restore versus first generation.
- Worker caps 1/2/3/4 and machine-derived production pool.

### 8.3 Acceptance criteria

- Identical voxel/fluid/biome/structure/lighting source digests to the baseline for unchanged worldgen rules.
- No task loss, duplicate publication or stale session publication under stress tests.
- No worker blocks while awaiting a future.
- Serial coordinator utilization is low and bounded; CPU-heavy tasks execute in worker pool.
- Local section readiness is no worse than baseline and improves in p95 for representative routes.
- First-visible-terrain and bootstrap readiness do not regress.
- Frame-time p95 and allocations are measured; no optimization accepted solely from aggregate route elapsed.
- Pending work and memory remain bounded when moving rapidly or changing dimensions.
- All CI and focused regression suites pass.

---

## 9. Commit discipline and restart instructions

This is a long-running refactor. Preserve progress in Git after each coherent slice.

- One commit should represent one reviewable behavior/model/test change.
- Prefer a code commit plus focused tests in the same commit.
- Update this handoff's checkboxes and current-state section with a separate small documentation commit when a phase milestone completes.
- Never leave several uncommitted subsystems in a working tree while moving to another phase.
- After each commit: inspect the diff, run focused tests, record commit SHA, confirm the PR head points to the expected commit, and continue.
- Do not squash or rewrite already-pushed commits during the work.
- If a CI failure occurs, stop feature progression, identify whether the failure is from the new slice or pre-existing, fix it with a small commit, then resume.
- Before each session, reread AGENTS.md, ARCHITECTURE.md, this handoff and the latest handoff status.
- Reconfirm PR #157 head/base before writing; do not write to main or merge worldgen-remake.
- Keep a short summary of the last completed commit and the exact next task below.

### Current execution status

**Implementation boundary:** the production materialization path now uses a serial continuation dispatcher and bounded shared worker pool. This is not yet a one-task-per-X/Z column pipeline. Production task identity remains one task per Asteria 16³ section, but the task key explicitly contains the horizontal column plus SectionY.

#### Completed foundations and production migration

- [x] Correct Minecraft 26.3 execution model verified: serial per-dimension coordination, parallel worker forks, ordered/main-thread integration; not one thread for all generation.
- [x] Current Asteria pipeline inspected against Minecraft Java 26.3 scheduling and status contracts.
- [x] Detailed work breakdown committed as this handoff.
- [x] Reused the existing ChunkColumnCoord(X,Z) in ChunkContentRevision.cs; kept ChunkCoord(X,Y,Z) as authoritative section storage.
- [x] Added stable ordering and explicit ToSection(Y) conversion.
- [x] Added tests for column identity across section Y, section conversion and deterministic ordering.
- [x] Added immutable typed step IDs, execution policies, column/section work scopes and direct dependency envelopes.
- [x] Added read/write envelopes and transitive dependency accumulation.
- [x] Added graph validation for duplicate IDs, missing prerequisites, cycles and stable topological ordering.
- [x] Added deterministic ChunkGenerationTaskQueue with deduplication, removal, stable FIFO ties, dynamic priority update and preservation of enqueue age.
- [x] Extended task identity to distinguish (X,Z,SectionY,Step) from column-scope (X,Z,Step).
- [x] Added ChunkGenerationTaskLifecycle states, wait tokens, duplicate/stale dependency rejection, cooperative yield, cancellation and failure.
- [x] Added bounded ChunkGenerationCompletionMailbox with reservation-before-dispatch, exactly-once publication and worker result/error payloads.
- [x] Added a bounded per-session ChunkGenerationWorkerPool with dedicated worker threads, bounded pending queue, completion reservations, cancellation and draining shutdown.
- [x] Added a serial ChunkGenerationDispatcher with bounded per-pump completion/advance/worker budgets, dynamic priority updates, Yield, wait/resume, cancellation, stale completion rejection, outcomes and deterministic task retirement.
- [x] Added ChunkMaterializationGenerationTask adapter: exactly one existing IChunkMaterializationJob.Advance() per worker operation; accumulated stage timing and detached terminal Chunk output.
- [x] Removed the old ExecuteMaterialization loop that advanced one job to completion inside one Task.Run.
- [x] Wired production section materialization in ChunkResidencyRuntime through the dispatcher and worker pool; chunk insertion and activation still occur on the owning world thread after ticket validation.
- [x] Added session retirement: cancel current generation tasks, close worker admission, collect late completions, discard cancelled results and include pool shutdown in DimensionRuntimeSession.IsQuiescent.
- [x] Added terminal CancelledTasks reporting so a cancelled task is not mistaken for a successful chunk or silently lost.
- [x] Added a regression fix so a result is never dequeued and discarded when the frame budget expires.
- [x] Added diagnostic counters for ready tasks, waiting tasks, pending worker operations, active workers and pending completions.
- [x] Updated residency test fixtures to retire and drain their worker pools at test completion.

#### Validation state (2026-11-13)

- CI run 43315371273 passed before production runtime integration; it validated the column-key model and deterministic step queue.
- CI run 43417536369 exposed five compile-time integration mismatches. These were corrected in commit cb8ca3241c96fe815cc15ae58f2bae106f787731.
- CI run 43428753210 had Core tests pass at the f50dc78 revision, but its terrain A/B job timed out with one materialization job stranded. That run did not validate the later fixture cleanup or lost-outcome fix.
- The terrain A/B source also compiles against a historical baseline commit. Candidate-only scheduler diagnostics initially broke baseline compilation; commit 8d584f87f95af13e1fb8767c9ef75a50e36d76a5 changes the benchmark to read these optional counters reflectively.
- **Current head:** 8d584f87f95af13e1fb8767c9ef75a50e36d76a5.
- **Current CI:** validate run 43429660331 and Windows build 43429653110 are in progress at the time of this handoff update. Do not treat the runtime migration as green until those jobs finish, the A/B benchmark completes, and its digest matches.

#### Not yet implemented / still required

- [ ] One logical ChunkColumnGenerationTask per X/Z and target status coordinating multiple SectionY child work items. Current SectionReady task remains section-scoped.
- [ ] Connect ChunkGenerationPlan / step graph to production task progression. The typed plan is currently a validated model, not the runtime's executable step scheduler.
- [ ] Audit every existing materializer stage's worst-case duration; subdivide expensive stages into deterministic bounded slices. One Advance() call can still be long.
- [ ] Add explicit horizontal and vertical dependency futures/read-write envelopes to production tasks. Current adapter resumes its own incremental job; cross-section dependencies are not yet modeled as status futures.
- [ ] Turn GravitySupport's nested support-section materialization into a first-class vertical continuation/dependency shared across in-flight section work.
- [ ] Replace ChunkStreamingState's scanned HashSet<ChunkCoord> incoming pending queue with the unified reprioritizable column/task admission model.
- [ ] Move arrival lighting/physics reconciliation off the compatibility Task.Run lane and into the shared worker admission mechanism.
- [ ] Integrate LightingRuntime, FluidSimulationRuntime, TerrainMeshPipeline and FluidMeshPipeline with shared CPU admission without changing domain ownership or revision validation.
- [ ] Remove or redesign Main.cs's fixed four-lane worker-start coordination only after all relevant owners use shared admission.
- [ ] Add end-to-end equivalence tests across worker counts, section order, cold/warm caches and scheduling/completion order.
- [ ] Rerun deterministic fixed-seed digests and compare the new cooperative runtime against the old baseline.
- [ ] Complete cold/warm, bootstrap, route, high-elevation, cave/floating-volume and portal-arrival benchmarks with median/p95, local gap frames, allocations, active/waiting work and throughput.
- [ ] Run full Core, Godot native smoke, Debug diagnostics, biome/worldgen rendering, terrain A/B and WebUI CI successfully on the same final head.
- [ ] Keep PR #157 open; do not merge.

**Recent implementation commits (chronological):**
- Handoff and graph foundation: c97184967023ec38c75d40f52a771fb7bd433a42
- Reused column identity and tests: 6b7a97415c1025d014cc3f805bd06b4901f34fc3
- Typed step/dependency graph: b4f6f4cbcdb4ebd0ea760194131fbf324a32d83a
- Deterministic reprioritizable task queue: 26f687037bde7179713dbcb75a7f096482c09a68
- Bounded completion mailbox: 842c851ec77cd8d4a60f88269e3ec0489e4b9847
- Completion result/error payload: 18236e3551c3c0db2d157e779fd618ddb8631df4
- Bounded worker pool: 7c192f66ba9c75e8ab6f407f52217133d46a7494
- Dedicated worker threads and pool tests: f40881ef1130084633d1744910e80cec410d358b
- Cooperative task yield: 4ca3769de89db4f0f7362f8f3d65f47ff098fa5e
- Cooperative task advance contract: 1b923eddd57b3e4dbbf68576da0790868efaaf36
- Serial task dispatcher: b7bc78b07b1a421302c2aeb1431cf4d99e13de8d
- Terminal task outcomes for world-loop publication: 2ee6d67318906117dfe810f2ef6685f719c60239
- Materializer cooperative adapter: 4a48fa895788fe0a9c2eaffe87c254b753abc2ee
- Adapter completion/child-coordinate correction: e0c924d5bf4f409b5667d346f7319e664544c1b2
- SectionY task identity: 02754f31420bb7dcdb415eccb06f34924f73a8c6
- Runtime materialization scheduler integration: 0ef73ee2bb21a3a8b2917610022df90eb2a9c6d0
- Dispatcher/runtime contract and timing fixes: cb8ca3241c96fe815cc15ae58f2bae106f787731
- Retirement, quiescence and cancellation test semantics: f50dc78db0c9930820a8be0f5cc48af5df9e2029
- Fixture worker cleanup and reconciliation quiescence: 451a537245fbe65d7d74e9c390b7481c653206c9
- Preserve outcomes when frame budget expires: f2612460a98a7349df0e05e1f5ef9f3630aa0128
- Baseline-compatible scheduler diagnostics: 8d584f87f95af13e1fb8767c9ef75a50e36d76a5

**Exact next action:** validate head 8d584f87f95af13e1fb8767c9ef75a50e36d76a5. If the terrain A/B route still stalls, inspect its new ready/waiting/workerOps/activeWorkers/completions counters and reproduce the exact state in a focused Core test. Do not advance to column orchestration or migrate other domain lanes until the production materialization path is stable and the benchmark reaches its drain condition.


## Latest continuation state — 2026-11-13

This section supersedes the older validation snapshot above. It records the state after the column-admission, cooperative slicing, and directional-occupancy work.

- **Repository branch:** `worldgen-perf-fix`
- **Code head at this documentation update:** `49e86b98521cf78f9db684b62b2199c549fa57fa`
- **PR #157:** open; base `unknown`. Keep the PR open; do not merge.
- **Validation for code head:** run 43480419355: in_progress
- **Windows build for code head:** run 43480403306: in_progress

### Implemented since the previous handoff snapshot

- Pending section requests are indexed by X/Z column. Each Y section retains its own enqueue timestamp and deterministic tie-break order.
- Runtime admission removes a bounded batch of sections from the best column, but each section still owns a separate `SectionReady` generation task, result, revision check, cancellation state and publication.
- Normal `TerrainLayer` advances through four Z rows at a time while retaining the original Y/Z/X write order. The sparse gravity-support face path remains on its original layer-sized quantum.
- One materialization worker operation executes up to 32 job advances or 6 ms, whichever is reached first. Individual stage timings are still retained inside the returned batch.
- `VolumeStructureField` exposes a resumable chunk cursor. The materializer consumes at most four candidates and up to 64 emitted voxels per job advance; the bulk `VoxelsForChunk` API remains as a compatibility wrapper.
- A parity test compares the cursor's one-candidate/one-voxel resumes with the bulk query on a fixed seed.
- Directional-face occupancy mask construction has been extracted into a phased builder. Production `TerrainSetup` now persists that builder on the materialization job and yields between the vertical slab phase, the four horizontal boundary phases, occupancy-row assembly and candidate-mask construction. The synchronous path used by non-materialization callers drives the same builder to completion.

### Performance findings (fixed-seed CI A/B)

- Baseline median route: **1,400 ms**.
- Cooperative candidate, two workers: **2,149 ms**.
- Cooperative candidate, four workers: **1,630 ms**.
- Fixed-seed chunk-synthesis digests remained equal to baseline. The four-worker route activated 237 unique chunks versus 235 in the baseline, so do not claim identical route activation sets.
- Four workers improved local-gap frames versus the baseline, but total route time remained slower. More worker threads alone are not an adequate fix.
- The directional occupancy profile identified approximately **103 ms** of horizontal boundary reads across 67 chunks, including approximately **89 ms** in density-column sampling. The next optimization should subdivide individual boundary strips only if the per-face phase still exceeds the worker-slice budget.
- The volume-structure cursor improves resumability, but its profile still reports roughly **56.5 MB** of allocations. A single candidate's current `TryAnchor` search remains a possible long quantum and should be split by Y scan / template-voxel validation after TerrainSetup is stable.

### Architecture still not complete

- There is still no single logical `ChunkColumnGenerationTask` coordinating all vertical sections and target statuses. Column grouping currently applies to pending admission only.
- The typed `ChunkGenerationPlan` dependency graph is not yet the executable production step graph.
- Cross-section and neighbor-region status futures / read-write envelopes are not yet wired into materialization.
- GravitySupport nested section work is not yet a first-class shared vertical continuation.
- Arrival reconciliation, lighting, fluids, terrain meshes and fluid meshes are not yet all admitted through one shared world-generation worker scheduler.
- Main.cs still owns separate fixed-lane start coordination for downstream domains; this must be retired only after those owners migrate to shared admission.

### Exact next action

1. Confirm the two CI runs listed above finish successfully for code head `49e86b98521cf78f9db684b62b2199c549fa57fa`.
2. If they pass, inspect the terrain A/B artifact for digest parity, maximum per-phase duration, local-gap frames and allocations.
3. If a single face phase still exceeds the worker budget, split that X/Z boundary reader into smaller contiguous strips while preserving its existing traversal and write order.
4. Add a focused test comparing synchronous and phased directional-mask results, then run the fixed-seed render and A/B suite.
5. Only after materialization remains stable, proceed to the logical column parent task and partial per-section completion publication. Keep PR #157 open.


---

## Latest continuation state — 2026-11-14 (authoritative; supersedes earlier status snapshots)

**Repository:** `sarakborges/asteria`  
**Branch:** `worldgen-perf-fix`  
**PR:** #157, open, base `worldgen-remake`; do not merge  
**Code HEAD before this update:** `c8e735aac3131c3fb9902aa57ea398981f4845cd`  
**Current validation:** validate run [43619524465](https://github.com/sarakborges/asteria/actions/runs/43619524465) was still in progress when checked. WebUI succeeded; Godot C# build and ReadyToRun succeeded; Core tests and terrain A/B were still running. Windows x64 build/export run [43619521391](https://github.com/sarakborges/asteria/actions/runs/43619521391) succeeded. Do not claim final validation until run 43619524465 completes.

### 2026-11-14 source audit: what is actually implemented

- [x] A shared `ChunkTicketStorage` feeds independent loading and simulation distance graphs.
- [x] `ChunkDistanceTracker.SynchronizeSources` recomputes bounded affected regions after source changes instead of rebuilding the entire graph.
- [x] `ChunkHolder` stores a ticket-derived generation ceiling, attained generation status, three full-status futures and dependency-claim count.
- [x] Player-loading admission is separately throttled to four columns awaiting FULL, releasing an admission slot when all required vertical sections in that column are materialized.
- [x] A column-level `ChunkColumnMaterializationGenerationTask` now owns multiple vertical section tasks. Production task identity is `(X,Z,SectionReady)`; each section is a child.
- [x] The generation worker pool is shared with downstream `WorldWorkerPool` work. Task priorities can be updated as player position/direction changes.
- [x] Worker results return through a completion mailbox. The owner thread validates generations/tickets before publishing resident chunks. Cancellation, late-result rejection, retirement and holder leases exist.
- [x] Simulation-distance checks are wired to major block/fluid/entity systems. The natural-spawn candidate tracker has its own fixed 8-chunk radius.
- [x] Ticket expiration is driven by logical world ticks. The current timeout values (Portal 300, Ender Pearl 40, Player Spawn 20, Spawn Search 1, Unknown 1) match the 26.3 reference.

### Critical corrections to previous status wording

The column parent task is now implemented, so the older statement “there is no logical task per X/Z” is stale. However, it is **only a parent aggregating FULL section jobs**; it is not yet the Minecraft-style task that owns a target status and walks a generation pyramid.

The remaining architecture gap is more specific:

1. `AsteriaChunkGenerationPlan.Default` is still a declarative DAG. `ChunkGenerationDispatcher` does not consult it to decide which step is runnable, what status a neighboring holder must reach, or which execution lane to use.
2. `ChunkMaterializationGenerationTask.AdvanceUntilWait` currently loops over `IChunkMaterializationJob.Advance()` until the job completes or reports `PendingDependency`. One worker operation can therefore run many materialization stages. This is not the required step-by-step continuation model.
3. `ChunkGenerationExecutionPolicy` is metadata only. It is not yet used to choose between a serial worldgen executor, shared-pool fork, ordered mutation, serial lighting executor or main-thread integration.
4. `ChunkHolder` generation futures and full-status futures exist, but production generation does not request neighboring holders' status futures. `AcquireDependencyClaims` only retains holders inside the target's horizontal dependency envelope; it does not ensure those holders reach the prerequisite status.
5. Dependency claims currently retain horizontal holders and do not represent the full set of section-Y requirements as independent status requests. GravitySupport remains a special nested child-job dependency, not a general graph edge resolved through holder futures.
6. Loading and generation are not yet represented by separate executable pyramids. Existing restore-from-archive behavior exists, but the task path does not first request the loading-pyramid status and then fall back to the generation pyramid when stored data is insufficient.
7. The materializer has no `WorldGenRegion`-equivalent capability boundary enforcing the current step's allowed reads and write radius. Read/write envelopes are plan metadata; the provider still owns direct field access.
8. Arrival lighting/physics reconciliation still uses compatibility scheduling directly against the shared pool. There is no separate serial per-dimension lighting executor yet. Fluid/mesh pipelines retain their own domain-owned admission paths.
9. Natural spawn has the fixed radius-8 candidate tracker and entity-ticking gate, but must also filter candidates by a non-spectating player within 128 blocks.
10. Ticket identity currently includes an optional `Key`, while vanilla identity is `(type, level)`. Persistence and dimension-active semantics are represented by flags but still need an end-to-end lifecycle audit.

### Important reference nuance: do not copy vanilla's radius 11 blindly

Minecraft 26.3's FULL generation pyramid accumulates a radius of 11: structure-start requirements reach radius 8, then terrain/features/light dependencies add three more rings. That is why vanilla's loading graph reaches level 44. This is derived from the accumulated requirements, not a tunable worker or render-distance constant.

Asteria's current DAG has a maximum horizontal dependency radius of 1. Its world-space `SurfaceStructureField` and other generator-owned fields can answer queries without requiring distant chunks to become resident. The correct Asteria loading radius must be derived from actual chunk/section dependencies and permitted read/write regions. Do **not** change the radius to 11 or maximum level to 44 merely to copy Minecraft when the required data can be read from an immutable generator field without a chunk holder.

A concrete inconsistency is already visible: the current plan's only horizontal halo is `DirectionalFacePalettes -> SurfaceTerrainColumn` at radius 1, but `ChunkLevel.GetGenerationStatus(34)` maps that ring to `InitializeLight`. In Asteria, that neighbor prerequisite is `Terrain`, not lighting. The level-to-status mapping must be derived from the target step's accumulated prerequisites, not copied from vanilla's status table.

### Correct Minecraft 26.3 execution model to preserve

- A per-dimension serial worldgen `ConsecutiveExecutor` advances one logical chunk task at a time over a shared worker pool.
- A task runs until it completes or reaches an incomplete future. It returns immediately when blocked; it does not wait synchronously for a neighbor or worker.
- Only explicitly parallelizable steps fork to the shared worker pool. The biome and terrain forks are key examples; feature application is serialized because it can write into neighboring chunks.
- A separate serial lighting executor uses the same physical worker pool.
- FULL integration/publication occurs on the server/main thread.
- A task claims the holders inside the target status's **accumulated** dependency radius before starting, and those holders cannot unload/save away while the task still owns the claims.
- The loading graph determines whether a holder exists and its highest allowed generation status. The requested status is a separate target. Dropping the ceiling fails pending futures above the new ceiling and reschedules the task at the highest status still requested.
- The loading pyramid and generation pyramid are distinct. Existing archived chunks should follow the loading path; generation stages are run only when the requested status is not present in storage.
- Simulation is a separate graph and separate gate. The natural-spawn radius-8 tracker is independent of both, with an additional non-spectator-player-within-128-blocks check.

Reference: Minecraft Java 26.3 source analysis at [chunk generation pipeline](https://minecraftdocs.dev/systems/world/chunk-generation-pipeline) and [tickets and loading](https://minecraftdocs.dev/systems/world/tickets-and-loading).

### Updated implementation phases

**Phase A — finish the ticket/status contract**
- [ ] Derive the required status for each distance ring from `ChunkGenerationPlan.GetAccumulatedDependencies(target)`; add tests proving the Asteria radius-1 palette neighbor needs Terrain rather than InitializeLight.
- [ ] Model a column's vertical-section status requirements explicitly. A horizontal holder must know which section-Y children satisfy a target and which section offsets a dependent step requires.
- [ ] Make the holder's generation futures authoritative for production scheduling. Requests are keyed by target status (and section set where required); duplicate requests share the same in-flight work.
- [ ] Make ticket-ceiling demotion fail only disallowed pending futures and schedule the highest remaining requested status.
- [ ] Remove `ChunkTicket.Key` unless a concrete Asteria requirement demonstrates why the vanilla `(type, level)` identity is insufficient.
- [ ] Connect Persist and KeepDimensionActive flags to actual persistence and dimension idle lifecycle. Confirm ticket re-addition resets its timeout without duplicate identities.
- [ ] Add the non-spectator 128-block natural-spawn player filter.

**Phase B — make step policy executable**
- [ ] Map each existing Asteria materialization stage to one plan step and one execution policy. Do not use the same policy for every step by default.
- [ ] Replace `AdvanceUntilWait`'s multi-stage loop with a single-stage continuation. One advance must either complete one serial step, schedule one explicitly parallelizable worker fork, or yield on an incomplete dependency.
- [ ] Introduce a per-dimension serial worldgen executor over the shared pool. The serial task-advance lane must not be the Godot/main thread; it must never block on worker or holder futures.
- [ ] Ensure worker forks use detached inputs/results and revision validation. A fork may not access mutable live world/holder/dispatcher state.
- [ ] Keep stages with cross-chunk writes behind a serialized/ordered lane and enforce their declared write radius.
- [ ] Add a separate serial lighting executor over the same physical pool. Route initial lighting and arrival reconciliation through it.
- [ ] Keep final chunk publication and engine-facing integration on the owner/main thread.

**Phase C — generation/loading pyramids and region access**
- [ ] Build a generation pyramid from transitive step dependencies, retaining exact required status by horizontal and vertical offset, not only one bounding radius.
- [ ] Build a separate loading pyramid for disk/archive load, parse/upgrade and FULL integration.
- [ ] Acquire and release generation holder claims from the exact accumulated envelope before task execution, including vertical sections. Save/unload must wait for claims.
- [ ] Add an Asteria generation-region view that exposes only permitted neighbor reads and rejects writes outside the active step's write envelope. Adapt field-based generator inputs without forcing unnecessary chunk materialization.
- [ ] Preserve Asteria's 16³ section storage and deterministic world-coordinate generation; one logical horizontal column task coordinates the set of relevant vertical sections.

**Phase D — unify resource admission, validate and retire compatibility paths**
- [ ] Move lighting/arrival reconciliation and other relevant CPU world work under shared priority admission while preserving FluidSimulationRuntime and mesh-pipeline ownership.
- [ ] Remove obsolete fixed-lane worker-start coordination only after all owning subsystems use shared admission.
- [ ] Validate identical voxel/fluid/biome/structure outputs across worker counts, task order, cache state and completion order.
- [ ] Validate cancellation, stale completions, status demotion, holder claims, dimension retirement and save/eviction while generation is in flight.
- [ ] Benchmark cold/warm generation, first playable bootstrap, fixed-seed routes, vertical movement, high terrain, caves/structures and portal arrival. Record median/p95, local readiness gaps, queue wait, worker utilization, allocations and active/waiting tasks.
- [ ] Run Core, Godot native, terrain A/B and WebUI CI on the final same HEAD.

### Exact next action

1. Check validate run 43619524465 before any scheduler migration. If Core tests or terrain A/B fail, fix the regression in a small commit and rerun.
2. Implement and test the derived ring-to-generation-status mapping. This is the smallest confirmed status-contract error and prevents the current vanilla status mapping from over-generating neighbor chunks.
3. Then begin Phase B with one small slice: introduce the single-stage continuation contract and tests for a job that has multiple stages and a pending dependency. Do not route every stage to workers; the plan's execution policy must determine the lane.
4. Keep PR #157 open. Do not merge.



---

## Follow-up implementation — 2026-11-14

- **Docs/handoff commit:** `7279e9dcb8cd2128c0e97f7d4c5a5dcde4bc4575` (the original source-audit update).
- **Derived status mapping:** `b528c24801f624ed83253f192048130eb8669477`.
- **Ticket level integration:** `2b0e4fbe83478866a33ede0609413e0ed9a5a0d0`.
- **Focused coverage:** `222315a4ec8d600d562fc65558c6cc5d93fb6165`.

### Completed from Phase A

- [x] Added `AsteriaChunkGenerationPlan.GetRequiredStatusAtHorizontalDistance`. It derives the deepest status needed at each horizontal ring from the target step's accumulated prerequisite envelopes.
- [x] Replaced the hard-coded vanilla level-to-generation-status switch in `ChunkLevel.GetGenerationStatus` with the derived Asteria plan mapping.
- [x] Added tests proving the current FULL target has horizontal radius 1, that its first neighbor ring needs `Terrain`, that a second ring is outside the declared dependency envelope, and that negative distance is rejected.

**Verified implication:** Asteria's current first halo is `DirectionalFacePalettes -> SurfaceTerrainColumn`. A holder one ring away is therefore allowed through `Terrain`, not `InitializeLight`. The full generation distance remains derived from the Asteria graph; no vanilla radius of 11 was copied.

### CI for this follow-up

- Validate run [43622622606](https://github.com/sarakborges/asteria/actions/runs/43622622606), HEAD `222315a4ec8d600d562fc65558c6cc5d93fb6165`: WebUI passed; Godot C# build and ReadyToRun publish/verification passed; Core tests and terrain A/B were still running at the latest check.
- Windows build run [43622617724](https://github.com/sarakborges/asteria/actions/runs/43622617724), same HEAD: was still in progress at the latest check.
- The commit is not fully validated until the latest runs finish successfully. If any Core test fails, stop Phase B and fix that failure first.

### Next implementation boundary

After run 43622622606 is green:

1. Introduce a per-dimension serial worldgen continuation lane backed by the existing shared `WorldWorkerPool`. It must execute at most one logical task-advance operation at a time for that dimension, never block on an incomplete dependency, and allow independent worker forks to execute concurrently on the same physical pool.
2. Keep dispatcher bookkeeping, ticket updates, cancellation requests and publication under their current authoritative owner. Worker continuations must publish detached completion messages; they must not mutate the dispatcher's collections directly.
3. Add tests for single-lane serialization, different worker thread from the world/main owner, cooperative release on incomplete dependencies, reprioritization while queued, cancellation while an advance is in flight, and retirement after late completion.
4. Only then split task progression by executable step policy. Do not put every stage on a worker automatically: the plan policy must control whether a stage runs inline on the serial worldgen lane, forks to the shared pool, uses the serial lighting lane, or requires main-thread integration.
5. Preserve the current task's target status and generation identity while this lane is introduced; do not conflate one worker operation with one generation status.


## Scope correction — 2026-11-14

The architectural target for this work is **exclusively Minecraft Java 26.3**. All behavior, terminology, concurrency, ticket/status and generation-pyramid decisions in this handoff must be verified against Minecraft Java 26.3 and then adapted to Asteria's own storage and ownership contracts.



---

## Latest continuation state — 2026-11-14 (serial worldgen lane)

**Branch:** `worldgen-perf-fix`  
**PR:** #157, open, base `worldgen-remake`; do not merge  
**Code HEAD:** `6dbd9ef305693a53636375acc85cf2b7bb2def65`  
**Validation:** run [43624226523](https://github.com/sarakborges/asteria/actions/runs/43624226523) is running against this HEAD. C# build, ReadyToRun and WebUI build/Storybook passed; Core tests and terrain A/B were still running at the latest check. Windows export run [43624220194](https://github.com/sarakborges/asteria/actions/runs/43624220194) is also running. Do not treat this lane as validated until the Core tests pass.

### Implemented in this continuation

- [x] Removed all out-of-scope project comparisons from this handoff. Minecraft Java 26.3 is the sole architecture reference.
- [x] Added `ChunkGenerationTaskAdvanceMailbox`, a one-slot worker-to-owner mailbox. One in-flight advance per dimension makes the one-result bound explicit.
- [x] Extended `ChunkGenerationWorkerPool` to schedule task-advance continuations and ordinary dependency forks through the same existing `WorldWorkerPool`.
- [x] Changed `ChunkGenerationDispatcher` so `IChunkGenerationTask.Advance` executes asynchronously on the shared worker pool, with only one advance in flight per dimension.
- [x] Kept lifecycle transitions, priority queues, cancellation, task identity, holder/ticket interactions and final result publication under the dispatcher's existing owner thread.
- [x] Kept dependency forks independently parallel. When an advance returns `WaitForWorkers`, the serial lane becomes available immediately; the task is resumed only after all dependencies return through the completion mailbox.
- [x] Priority changes now update a queued/in-flight task advance as well as its queued/running dependency work.
- [x] Added a regression test proving that two available physical workers still cannot execute two task advances concurrently for one dimension, and that advances run off the world/owner thread.

### Exact relationship with Minecraft 26.3

This change reproduces the central `ConsecutiveExecutor` contract: a logical serial lane schedules one continuation at a time onto the shared backing executor. Consecutive advances may run on different physical pool threads. It does **not** create one dedicated generation thread per dimension and does not serialize independent worker forks.

The dispatcher is still pumped by the owner thread to admit work and consume result messages. This owner-thread coordination is deliberate for Asteria's current state ownership. Only the bounded `IChunkGenerationTask.Advance` callback has moved onto the serial lane. The next refactor will make the plan's execution policy decide which individual step is a serial-lane transition and which is a worker fork; until then, the materialization adapter still hides multiple provider-stage advances behind its worker operation.

### Remaining work after validation

1. Confirm Core tests, A/B and Windows export for HEAD `6dbd9ef`.
2. Update `IChunkGenerationTask` and executor documentation to distinguish the serial logical lane from the owner thread precisely.
3. Make `ChunkGenerationPlan.ExecutionPolicy` executable: one stage transition per serial-lane advance, only declared CPU steps forked, write-sensitive steps use an ordered lane, lighting receives its own serial executor over the same pool, and final integration remains on the world/main owner.
4. Convert neighbor generation prerequisites into real holder-status future requests. A spatial holder claim must also request the required status for the exact horizontal/vertical offset.
5. Build distinct executable generation and loading pyramids and connect archive restoration to the loading path.
6. Replace the special gravity-support child-task path with the same reusable status-future dependency mechanism.
7. Validate serial-lane cancellation, demotion, priority changes, late completions, worker-count determinism and dimension retirement.

**Next code task:** after the current Core test suite passes, map the existing `ChunkMaterializationStage` transitions to the plan's step IDs and execution policies. Start with a single-stage serial transition contract; do not move every provider stage to workers by default.



### Worker-capacity audit correction — 2026-11-14

The worker-capacity path was checked end to end before changing it:

- `Main.MaxConcurrentWorldJobs` calls `WorldWorkerCapacity.Resolve(Environment.ProcessorCount, ReadMaximumBackgroundThreads())`.
- `WorldWorkerCapacity.Resolve` returns `min(max(1, processorCount - 1), maximumBackgroundThreads)`, with a default configurable ceiling of 255.
- `ASTERIA_MAX_BACKGROUND_THREADS` is validated and supplied as the ceiling.
- One `WorldWorkerPool` is created for the active world session and injected into the dimension-session controller; dimension sessions share that physical pool.

This matches Minecraft 26.3's processor-minus-one background-pool capacity policy with a configurable maximum. **Do not replace this with an arbitrary fixed worker count or create separate per-dimension physical pools.**

A separate constant, `WorldWorkerLaneCount = 4`, in `Main.DispatchReadyWorldWorkers` is not the physical worker count. It caps per-frame admission attempts across four domain-owned queues (fluid simulation, terrain mesh, lighting, fluid mesh). It is not yet equivalent to Minecraft's worldgen/light `ChunkTaskDispatcher` pair. Audit this domain admission separately when moving lighting and other world CPU operations to their proper serial executors.



---

## Authoritative continuation update — 2026-11-14 (HEAD 49df11f)

**Reference:** Minecraft Java 26.3 only.  
**Branch:** `worldgen-perf-fix`  
**PR:** #157, open, base `worldgen-remake`; no merge.  
**Code HEAD:** `49df11ff3eb79b3949828fe52ef003fefb9db850`.

### Current implementation

- [x] The physical world worker pool remains shared by all dimensions. Capacity is `min(max(1, processorCount - 1), configuredMaximum)`; do not replace it with per-dimension physical pools.
- [x] Each dimension has a serial worldgen continuation executor backed by that shared pool. At most one task advance for that dimension is in flight. A continuation can resume on another pool thread; no thread is pinned.
- [x] Each dimension has a separate serial lighting executor on the same physical pool. Initial lighting, dynamic relighting and chunk-arrival lighting reconciliation use that same logical light lane.
- [x] The materialization task executes one stage quantum per operation. Worker completion makes the next stage eligible and yields to the column scheduler instead of chaining another stage in the same worker operation.
- [x] The plan's execution policy is consulted by the materialization adapter: terrain setup/layers fork to the shared pool; FEATURES-like mutation stages advance on the serial worldgen lane; lighting uses the serial light lane; completed chunks are returned for owner-thread publication.
- [x] A column task fans independent section worker forks out together. It can start all ready vertical terrain forks in one continuation, but advances at most one ordered mutation stage before yielding. This preserves Asteria's parallelism across its 16³ vertical storage sections without allowing a section's mutation sequence to monopolize the serial lane.
- [x] Holder generation progress is published through BIOMES, TERRAIN, FEATURES and lighting stages. Column progress is the minimum status across the currently required vertical sections. FULL is completed only after the detached section results have been validated and inserted by the owning world thread.
- [x] Expanding the required vertical section set invalidates the previous generation-progress revision and rearms the relevant futures. Results from superseded batches are ignored.
- [x] The generation plan now distinguishes `ImmutableWorldData` dependencies from `HolderStatus` dependencies. This is important: neighbor biome/density queries do not automatically imply that neighbor chunks must be loaded.
- [x] The plan declares horizontal immutable-data read halos for SurfaceDecorations, DirectionalFacePalettes, WallLayers and WallObjects. The palette step also declares its vertical read halo.
- [x] Tests now assert the difference between `FullSectionAccessEnvelope` and `FullSectionEnvelope`, and that immutable neighbor reads do not expand the loading-ticket radius.

### Important envelope distinction

The latest DAG describes two different regions:

- `FullSectionAccessEnvelope`: horizontal radius 1, section offsets -1..+1. This is the conservative reach of immutable procedural data reads.
- `FullSectionEnvelope`: horizontal radius 0, section offsets -1..0. This includes the current section and the lower-section gravity-support prerequisite, which is a real generated-section dependency.

Therefore `ChunkLevel.MaximumLoading` is currently `ChunkLevel.Full`, not FULL+1 or FULL+11. Do not widen the loading graph just because a stage samples immutable generator fields in a neighboring X/Z column. Those fields are not chunk holders.

**Remaining correctness issue:** `ChunkGenerationPlan.GetHolderDependencyEnvelope` retains vertical offsets, but `ChunkDistanceManager.AcquireDependencyClaims` currently acquires claims by horizontal column only. `ChunkHolder.GetGenerationFuture` is keyed by status only, not by vertical section Y, and the production materialization path does not request those holder futures. Gravity support still exposes its lower section as a nested `PendingDependency` job owned by the parent materialization task. This preserves correctness for the current special case, but it is not yet a reusable Minecraft-style holder-status dependency graph.

### Remaining gaps, in priority order

1. **Real holder-status dependency requests.** Replace the special nested gravity-support task with a general dependency request keyed to the Asteria section requirement. Asteria's storage is 16³ per `ChunkCoord`, unlike a single full-height vertical chunk column; define a section-aware future/claim contract rather than pretending an X/Z holder future alone identifies the lower section. Identical dependency requests must share in-flight work. The parent must suspend on a future and resume through its normal continuation, never synchronously wait.
2. **Make column-scope DAG nodes real shared products.** `ColumnBiomeData` and `SurfaceTerrainColumn` are currently represented in the plan and cached by generator-owned data structures, but the initial materializer still enters through each section's TerrainSetup stage. Promote column data preparation to a single X/Z-scoped task/product shared by all vertical section tasks, with immutable lifetime/revision semantics. Avoid sampling the same column data independently per section.
3. **Enforce read/write envelopes.** The plan now records immutable-data read halos and holder-status dependencies, but `SurfaceChunkMaterializer` still reads generator fields directly. Add a generation-region/capability boundary that validates every read against the active step's read envelope and every mutation against its write envelope. Do not convert immutable reads into resident-holder requirements.
4. **Executable loading pyramid.** Archive restoration already precedes first materialization, but it is not represented by a distinct loading task/status pyramid equivalent to Minecraft's load path. Separate restore/upgrade/section integration from generation so saved sections do not enter provider generation.
5. **Complete status/promotion parity.** Audit each Asteria status against Minecraft 26.3 semantics, including failed future rearming, ticket demotion, concurrent status requests, and completion of FULL/BLOCK_TICKING/ENTITY_TICKING only after owner-thread publication.
6. **Finish lighting task admission.** Initial, dynamic and arrival lighting share the serial light executor. Continue by auditing snapshot capture, result validation, stale revision cancellation, retry and fair priority admission so all light work obeys the same executor contract without moving live-world snapshot capture off the owner thread.
7. **Validation.** Core tests, Godot C# build, ReadyToRun, Windows export, WebUI screen parity and fixed-seed terrain A/B must all pass on one final HEAD. Compare deterministic outputs across worker counts and task/completion orders; benchmark median/p95, player-local readiness gaps, queue wait and worker utilization.

### Recent implementation commits

- `4f5e7fd6ee209b7b9b343983ebcb6b5b69ff7b79` — yield after each completed worker stage.
- `d39a33ed206dd1c15f50b345807c6c2e27b2ef41` — complete section after the serial light future resolves.
- `50ac4416de2caf8d047a93c4bfb4ba2d0fce3957` — test the yield between worker completion and the next stage.
- `056b667c59be49546038de889e0bbee6e37afc66` — correct the task continuation contract documentation.
- `4ac9981122634f1a1e6e707c25d9db196df16d3d` — expose each section task's next execution lane.
- `dd3ec43ce9e4533dd84fa2e25b88037f70bcf230` — batch independent vertical-section forks.
- `45d6e635a1495323259773697b5966079c115573` — batch initial section forks.
- `78e20ff1bc3d4633699827ba642490e6826ee411` — test that vertical terrain forks remain parallel.
- `193f1d3e5a86765dac7da269589b76440761b1bf` — declare immutable horizontal data-read halos.
- `49df11ff3eb79b3949828fe52ef003fefb9db850` — test read-halo versus holder-ticket envelopes.

### CI at this HEAD

- Validate run [43654978196](https://github.com/sarakborges/asteria/actions/runs/43654978196), HEAD `49df11f`: pending/in progress at the last check. The C# build and WebUI build/Storybook had passed on the immediately preceding HEAD `0b7f830`; its Core tests and terrain A/B were not yet complete.
- Windows export run [43654973317](https://github.com/sarakborges/asteria/actions/runs/43654973317), same HEAD: queued at the last check.
- Earlier runs were cancelled because newer commits arrived. A cancelled run is not a pass. Do not mark this implementation validated until the current run's Core tests, terrain A/B and Windows export complete successfully on the same HEAD.

**Next implementation slice:** implement a section-aware, deduplicated generation dependency future for the gravity-support lower-section requirement. Do not add a generic future framework before the section-Y identity and ownership semantics are defined; the present X/Z-only holder futures cannot safely represent two different vertical sections.


---

## Authoritative continuation update — 2026-11-15 (HEAD e84b4bc)

**Architecture reference:** Minecraft Java 26.3 only. Do not introduce other projects or Minecraft clones as comparison targets. Asteria's existing 16×16×16 storage, deterministic world generation, archive format and Godot ownership boundaries remain authoritative constraints.

**Branch:** `worldgen-perf-fix`  
**PR:** [#157](https://github.com/sarakborges/asteria/pull/157), open, base `worldgen-remake`; no merge.  
**Code HEAD:** `e84b4bc13af19cbdfc22ef6ec3de1ebc0350244d`.

### Current implementation beyond HEAD 49df11f

- [x] The serial worldgen continuation lane and separate serial lighting lane both use the same shared physical worker pool.
- [x] The materialization adapter observes the step execution policy and yields between completed stage quanta.
- [x] A column task coordinates the vertical Asteria sections and batches independent worker forks.
- [x] Materialization tasks can expose a pending dependency's exact `ChunkCoord` to the owning column scheduler instead of unconditionally creating a nested task.
- [x] The column scheduler maintains section-generation futures keyed by exact `ChunkCoord` and achieved `ChunkGenerationStatus.Light`. When a lower section is already part of the same requested column batch, its completed detached chunk resolves the upper section's gravity-support dependency.
- [x] A completed lower section's top face is read only after that section has completed its own gravity-support stage. The redundant support-only job created by the upper section is disposed without being advanced.
- [x] The old nested support-only continuation remains as a fallback when the required lower section is outside the requested vertical batch.
- [x] A regression test covers the same-column case and asserts the duplicate support-only job receives zero advances.

### Exact limitation of the new section future

This is deliberately a first section-aware dependency step, **not yet a generic Minecraft-equivalent holder-future system**:

- The futures are currently scoped to one `ChunkColumnMaterializationGenerationTask`.
- The current key is `(ChunkCoord, Light)`; intermediate status futures such as `Terrain` and `Features` are not yet independently addressable per vertical section.
- The future carries the detached `ChunkMaterializationTaskOutput`, not a reusable holder-managed product for arbitrary consumers.
- Dependencies outside the current vertical batch still use the nested support-only job.
- `ChunkHolder.GetGenerationFuture` remains keyed only by `ChunkGenerationStatus`, and `ChunkDependencyClaim` still retains horizontal holders rather than exposing exact section claims.

Do not describe these futures as global, cross-column, or fully holder-managed until those contracts are implemented and tested.

### Recent commits

- `11cbe55` — define the materialization dependency-receiver contract.
- `089993c` — allow column-owned materialization tasks to defer pending dependencies to the column scheduler.
- `a3d4f5e` — resolve gravity support from an already completed lower section.
- `d71f932` — add exact-section generation futures and sibling-section waiter handling to the column task.
- `e84b4bc` — test reuse of the lower section result without advancing a duplicate support-only job.

### CI at HEAD e84b4bc

- Validate run [43680665080](https://github.com/sarakborges/asteria/actions/runs/43680665080): `build`, `terrain-ab` and `webui` were in progress at the latest check. The Godot C# build step had started; Core tests had not yet run.
- Windows export run [43680660552](https://github.com/sarakborges/asteria/actions/runs/43680660552): in progress.
- Earlier runs on intermediate commits were cancelled by subsequent pushes. Do not treat them as successful validation.
- This HEAD is **not validated** until Core tests, A/B and Windows export complete successfully.

### Next implementation slices

1. Finish the current CI. Fix any compile or behavioral failures before adding another scheduler feature.
2. Extend tests for lower-section dependency completion before and after waiter registration, multiple waiters sharing one section future, cancellation while waiting, dependency failure, and a lower section outside the requested batch.
3. Move section/status future ownership into the holder/generation coordination model. The identity must include the full `ChunkCoord` (including vertical Y) and the requested `ChunkGenerationStatus`; do not overload the existing X/Z-only holder future.
4. Extend dependency claims to retain the exact vertical sections in the plan envelope while continuing to deduplicate the shared X/Z holder lifetime.
5. Connect `ChunkGenerationPlan.GetAccumulatedDependencies` and `GetHolderDependencyEnvelope` to real status requests. A step must suspend on the exact future it requires and resume through the normal serial continuation; it must not merely hold a spatial claim.
6. Only after the reusable section/status futures work should the special gravity-support nested-job fallback be retired.
7. Continue with the shared X/Z `ColumnBiomeData` / `SurfaceTerrainColumn` products, enforced read/write envelopes, executable loading pyramid, and remaining lighting admission/validation.

**Immediate next action:** validate HEAD e84b4bc. If green, add the section/status future tests from item 2 and promote the current column-scoped future into an owner-managed section-aware generation future. Keep the commits small and do not merge PR #157.


---

## Authoritative continuation update — 2026-11-15 (HEAD 1c4e7c7)

**Reference scope:** Minecraft Java Edition 26.3 only. Other projects and clones are not architecture targets for this work.

**Branch:** `worldgen-perf-fix`  
**PR:** #157, base `worldgen-remake`, still open and unmerged.  
**HEAD:** `1c4e7c7b952d57e66513f84dacd82b1b63e99335`.

### Implementation completed after e84b4bc

- [x] Materialization tasks infer the status already achieved from their initial stage. A section beginning after `TerrainSetup` or `TerrainLayer` no longer waits for a status stage it cannot execute.
- [x] The column task seeds its per-section progress from that initial status and publishes the initial status transition to the holder.
- [x] The aggregate Terrain test now asserts that status on the coordinator turn that processes the final section's Terrain completion. It no longer assumes an extra yield.
- [x] Ordered section mutation advances rotate between ready vertical sections.
- [x] The column scheduler exposes and consults the current generation-plan step for a section.
- [x] The `GravitySupport` step's transitive holder requirements are read through `AsteriaChunkGenerationPlan.GetHolderStatusRequirements`. When the lower section is part of the current column task, the upper section parks until the lower section reaches the required Terrain status; it resumes through the normal column ready queue.
- [x] Out-of-batch gravity support remains a dynamic section task owned by the same column scheduler. The pending dependency product and the status prerequisite remain distinct: reaching Terrain wakes status waiters; finishing the detached support section resolves product waiters.
- [x] Two upper sections requesting the same out-of-batch lower support section have a regression test proving that only one dependency job is advanced and the same resulting chunk is delivered to both.
- [x] A dependency claim can now be acquired for multiple target sections. It unions the spatial envelopes, deduplicates exact 3D section coordinates and retains each horizontal holder exactly once.
- [x] `ChunkResidencyRuntime` now creates the claim from every section in the column batch, not only from the player-centre Y.
- [x] Section readiness is derived from the column's single authoritative `_sectionProgressStatuses` array. Local section futures retain waiting continuations and the detached product; they no longer keep a second mutable completed-status field.
- [x] The scheduler explicitly rejects a cross-column `HolderStatus` requirement instead of silently skipping a missing local future. A plan regression test asserts that every current holder-status requirement is column-local and that GravitySupport requires the immediate lower section at Terrain.

### Important boundary: cross-column status dependency admission is not implemented

The dispatcher already supports non-blocking `WaitForFutures`, and `ChunkDependencyClaim` exposes holder-owned futures for exact `(ChunkCoord, ChunkGenerationStatus)` requirements. That is not yet an end-to-end cross-column admission path.

A future cross-column dependency needs all of the following before it can be used by the plan:

1. The owner of the dependency request must ensure a producer task is admitted or reused for the requested holder/status. A claim only retains the holder; it does not enqueue work or raise a ticket ceiling.
2. The dependent task must suspend on the exact holder future using `ChunkGenerationFutureOperation`, without occupying a physical worker.
3. The completion must be checked for success, requested status and current section progress revision before the dependent resumes.
4. Cancellation, ticket demotion, reset and producer failure must release the waiter without reviving stale generation.
5. The request must stay within the exact section set and holder lease owned by the task.

The current plan declares no horizontal `HolderStatus` dependencies. Horizontal procedural reads are `ImmutableWorldData` dependencies and do not create/retain neighboring chunk holders. The only current holder-status dependency is the vertical GravitySupport prerequisite in the same X/Z column. The scheduler fails explicitly if this contract changes without corresponding cross-column admission.

### Minecraft 26.3 comparison — corrected interpretation

The 26.3 pipeline has ten statuses: EMPTY, STRUCTURE_STARTS, STRUCTURE_REFERENCES, BIOMES, TERRAIN, FEATURES, INITIALIZE_LIGHT, LIGHT, SPAWN and FULL. `ChunkPyramid` supplies separate generation and loading pyramids; `ChunkStep` owns direct/accumulated dependencies and write radius; `ChunkGenerationTask` walks a target; `GenerationChunkHolder` owns per-status futures and a ticket-derived ceiling; `WorldGenRegion` enforces read and write boundaries. The generation sweep radius of 11 for FULL is derived from the accumulated dependency list, not an isolated tuning constant. The loading sweep is narrower and may load persisted data before deciding whether full generation is necessary.

Reference reading checked during this continuation:  
- https://minecraftdocs.dev/systems/chunk-generation-pipeline (verified against 26.3; generation/loading pyramids, claims, futures, executors, access checks and load-or-generate walk).
- https://minecraftdocs.dev/systems/world/tickets-and-loading (verified against 26.3; independent loading/simulation graphs, ticket levels and player view).

Do not copy radius 11 into Asteria mechanically. Asteria's current DAG explicitly marks horizontal terrain/biome sampling as immutable generator data, not resident-holder prerequisites. Its current holder dependency envelope therefore has horizontal radius 0 and a vertical lower-section halo. If a later step genuinely needs a neighboring chunk's mutable generation product, declare a `HolderStatus` dependency and implement runtime admission before widening the loading graph.

Asteria's archive is an in-memory authoritative chunk archive, not Minecraft's region-file I/O path. `RestoreChunk` restores an already materialized chunk; it should not be described as region-file loading. The current runtime correctly bypasses terrain regeneration for archived chunks, then rebuilds applicable derived lighting/presentation work.

### Remaining gaps, in priority order

1. **Validate HEAD 1c4e7c7.** Do not accept another scheduler slice until Core tests, WebUI/Storybook, terrain A/B and Windows x64 export finish on this exact HEAD.
2. **Cross-column dependency admission.** Build an owner-level request path that can admit/reuse a producer for a required `(section, status)`, then connect it to non-blocking future waits with revision validation. The current fail-fast boundary must remain until this exists.
3. **Executable read/write envelopes.** The plan stores each step's read and write envelopes, but materialization jobs do not yet receive a region-like access capability that enforces those bounds at each read/write. Add an explicit bounded access context rather than treating metadata as enforcement.
4. **Separate loading vs generation plan semantics.** Asteria's saved-chunk restore path differs from Minecraft's disk EMPTY stage. Model the load path and generation path separately only where their actual work/status requirements differ; do not create a fake disk executor or duplicate persistence queue.
5. **Shared column products.** `ColumnBiomeData` and `SurfaceTerrainColumn` are represented in the DAG, but materialization still needs full integration of shared per-column products/futures across vertical sections, with immutable ownership and cancellation/revision rules.
6. **Exactly-once step lifecycle.** Validate duplicate request coalescing and step completion semantics against status futures across multiple requesting sections/tasks.
7. **Lighting boundary.** Verify INITIALIZE_LIGHT and LIGHT scheduling separately from generation and ensure required neighboring lighting/status prerequisites are represented where the solver truly consumes them.
8. **Ticket parity.** Re-check status ceilings, level mappings, priority resorting and ticket lifetimes against 26.3 source rather than relying on historical constants. Do not assume simulation and loading distances share one graph.
9. **Final validation.** Benchmark and visual fixtures must cover ocean, elevated mountains, swamp, and the known geology/strata regressions. A/B evidence must be from the same validated HEAD.

### Recent commits after e84b4bc

- `6fafb822` — preserve achieved status on a resumed materialization task.
- `5fe2c334` — publish initial vertical-section progress.
- `a2535d10` — correct aggregate Terrain assertion timing.
- `884f6b00` — verify multiple waiters can reuse one dynamic gravity dependency.
- `08ece755` — expose current plan step to column scheduling.
- `ad9a2f8f` — gate GravitySupport on the plan-derived lower-section status.
- `7cecdd66` — test the lower Terrain prerequisite before gravity.
- `e7860fb1` — acquire dependency claims across all target sections.
- `93caed51` — apply multi-section claims from the residency runtime.
- `1998e15f` — test unique horizontal-holder retention across vertical envelopes.
- `e8dc91c5` / `d4a41f4d` — consolidate section progress ownership in the column task.
- `21c42706` — reject unadmitted cross-column holder dependencies.
- `1c4e7c7b` — assert all current holder requirements are column-local.

### CI at HEAD 1c4e7c7

- Validate run: https://github.com/sarakborges/asteria/actions/runs/43736038535 — in progress at the last check; build, terrain A/B and WebUI jobs were still running.
- Windows export run: https://github.com/sarakborges/asteria/actions/runs/43736032358 — in progress at the last check.
- The preceding HEAD `d4a41f4d` had WebUI passed, Windows export passed, and the Core test step completed successfully before its validation workflow was cancelled during the subsequent baseline/benchmark steps. Those results do not substitute for validation of 1c4e7c7.
- No merge or deployment has been requested or performed.

**Immediate next action:** finish and inspect the CI for HEAD 1c4e7c7. If green, implement the explicit bounded generation-region read/write contract in a small independently tested slice; keep cross-column dependencies fail-fast until their producer-admission lifecycle exists.


---

## Ticket lifetime parity follow-up — 2026-11-15

The 26.3 ticket audit found that the timeout values were already correct, but the expiry behavior and flags were not.

- [x] `PORTAL`: 300 ticks, loading + simulation + persistence + keep-dimension-active; no `CAN_EXPIRE_IF_UNLOADED`.
- [x] `ENDER_PEARL`: 40 ticks, loading + simulation + keep-dimension-active; non-persistent and no `CAN_EXPIRE_IF_UNLOADED`.
- [x] `PLAYER_SPAWN`: 20 ticks, loading only; no `CAN_EXPIRE_IF_UNLOADED`.
- [x] `SPAWN_SEARCH`: 1 tick, loading only; no `CAN_EXPIRE_IF_UNLOADED`.
- [x] `UNKNOWN`: 1 tick, loading and the only ticket type whose countdown continues while the holder is still loading.
- [x] `PLAYER_LOADING`, `PLAYER_SIMULATION`, `FORCED`, and `DRAGON` retain their no-timeout and graph/persistence flags from the 26.3 ticket table.
- [x] Expiry now tracks elapsed logical ticks and pauses the countdown of non-UNKNOWN timed tickets while the holder exists but is not ready for saving. The countdown advances when the holder is absent or FULL promotion has completed and no generation dependency claim remains.
- [x] Re-adding an identical timed ticket resets its expiry tick, matching the vanilla reset-ticks-left behavior.
- [x] Regression tests cover the nine ticket definitions, portal pause/resume, UNKNOWN expiry while not save-ready, TTL reset, monotonic world ticks and holder save-readiness under dependency claims.

Reference: https://minecraftdocs.dev/systems/world/tickets-and-loading (verified against Minecraft 26.3; ticket table at lines 54–68 and expiry rules at lines 168–175).

**Current caveat:** Asteria's `IsReadyForSaving` approximation is `FULL` completed with zero generation claims. This is the correct domain boundary available in the current runtime, but should be revisited if save/unload acquires additional in-flight ownership not represented by holder dependency claims.

**Implementation commits:** `980269ac` (ticket flags/countdown), `da16a7c7` (holder save-readiness), `957e76be` (ticket lifetime tests), `44baa214` (ticket semantics comments), plus the follow-up assertion correction recorded in git history.

**Validation:** CI for these ticket changes is pending on the current branch. Do not mark the new ticket tests or the combined branch validated until Core, terrain A/B, WebUI and Windows export complete on the same final HEAD.


### Ticket expiry integration test — 2026-11-15

- [x] Added a `ChunkDistanceManager` integration test proving that a Portal ticket remains while its holder is not save-ready, pauses the remaining TTL, then expires at tick 700 after becoming FULL/save-ready.
- [x] The test also verifies that expiry removes the loading and simulation graph sources and drops the now-unretained holder.
- Implementation/test commit: `75288c77`.

The current ticket lifetime change set is **not yet validated**. CI run for HEAD `75288c77`: https://github.com/sarakborges/asteria/actions/runs/43736824818 (validate queued); Windows export: https://github.com/sarakborges/asteria/actions/runs/43736819605 (in progress).


---

## Continuation checkpoint — 2026-11-15 (bounded access region)

### Implementation commit

The first executable generation-region contract is implemented in commit
`502b0132dbda24f45fc701a2e88146244f3bea6c` on the continuation branch.

- `ChunkGenerationAccessRegion` binds a target section to one generation-plan step.
- Immutable generator-data reads are checked against that step's `ReadEnvelope`.
- Chunk writes are checked independently against its `WriteEnvelope`.
- World voxel coordinates are translated to their owning 16³ section before access is admitted; negative world Y is rejected.
- Envelope comparisons use widened coordinate deltas so extreme X/Z values cannot wrap.
- Holder-status reads are deliberately not authorized by this region. Existing section futures and dependency claims remain the separate mechanism for resident chunk products.

### First runtime integration

`DirectionalFacePalettes` now uses the region for its procedural column lookups and validates the destination section before applying a palette write. The generation path is guarded; arrival-time palette reconciliation remains a separate operation over saved world state and does not pretend to be a generation step.

This is an initial integration, not complete enforcement across the materialization pipeline. Other stages still need their actual field reads and mutations audited before they receive an access region or wider envelopes. Do not replace missing declarations with a blanket halo.

The default authored biome palettes have a maximum directional paint depth of six blocks; the existing horizontal radius-one and vertical section offsets -1..+1 cover those reads. The Gorge biome's `surfaceTerrain.depth: 42` is valley geometry, not a material-palette depth, and does not widen this access envelope.

### Validation checkpoint

On parent HEAD `7140b07b53d73fc96e7a0d914b1103c6f436e9c5`, the validate workflow had completed the C# build, Core tests, ReadyToRun verification, Godot 4.7.2 headless smoke validation, WebUI build, Storybook build and WebUI screen-parity tests. The Windows export workflow also completed successfully.

The parent validation run `43738792004` was still executing its fixed-seed worldgen and terrain A/B benchmarks when this continuation was prepared. Those results are not claimed for the new implementation commit; validation of the updated PR head remains required.

### Next ordered work

1. Validate this commit on the PR and inspect the fixed-seed benchmark evidence before making another performance change.
2. Extend `ChunkGenerationAccessRegion` stage by stage. Audit the concrete generator-owned reads and writes first, then declare the smallest correct envelopes and route those accesses through the region.
3. Keep immutable field sampling separate from holder-status dependencies and ticket claims. Cross-column holder-status prerequisites remain unsupported until their producer can be admitted/reused non-blockingly with revision-safe future ownership.
4. After access enforcement is established, continue the shared column-product work for `ColumnBiomeData` and `SurfaceTerrainColumn`, preserving one owner and deterministic immutable snapshots.
5. Keep lighting, ordered mutations, worker execution and main-thread publication in their existing separate lanes; do not merge these responsibilities into the access-region abstraction.


---

## SurfaceDecorations radius-2 checkpoint — 2026-11-15

### Contract correction and runtime enforcement

The authored swamp cattail decoration uses a nearby-water requirement with
`maxDistance: 2`. The former `SurfaceDecorations` plan envelope of horizontal
radius 1 did not describe that read. Slope conditions also inspect the four
cardinal surface columns.

The plan now declares horizontal radius 2 for `SurfaceDecorations` and its
`SurfaceTerrainColumn` immutable-data prerequisite. This increases the full
section **access** envelope, but does not create adjacent holder-status
requirements or increase the ticket/dependency-claim radius.

`ChunkGenerationAccessRegion` now distinguishes 2D column-data reads from
3D section-position reads. The surface terrain sampler carries the optional
region and validates contextual slope samples; the nearby-fluid query validates
each probed column before reading it. Surface decoration voxel writes and
pending object placements are checked against the stage's write envelope.
Directional palette restoration is also guarded before its first mutation.

### Validation

The validate run for the preceding head was still executing fixed-seed
benchmarks and the bounded worldgen query baseline when this checkpoint was
prepared. Its results do not validate these changes. The Windows export for
that preceding head completed successfully. Revalidation of the new head is
required; do not claim performance improvement without the A/B report.

### Next work

Continue with one stage at a time. Audit every procedural field and resident
chunk read separately, derive the narrowest correct read/write envelope, then
route the actual access through the corresponding region. Keep immutable
column/field data reads separate from holder futures and ticket claims.


---

## VolumeStructures bounded access checkpoint — 2026-11-15

### Audited access bounds

`BiomeVolumeStructureDefinition` permits a support range of up to 512
blocks. Authored templates are bounded to a horizontal radius of six blocks
and a vertical reach of sixteen blocks on one side of their support face.
`VolumeStructureField.TryAnchor` samples the biome and density over the
whole configured support-Y range, then validates every template voxel and
its supporting foot.

The production `VolumeStructures` step now declares an immutable generator
read envelope with horizontal radius 1 and section offsets [-34, +34]. The
vertical bound covers the maximum authored support range plus template reach;
the horizontal bound covers an anchor whose footprint intersects the target
section and the farthest voxel of that same template. This is a field-data
envelope, not a chunk-residency radius.

`ChunkGenerationAccessRegion` is passed into the resumable structure cursor.
Anchor biome/density reads, template occupancy/support reads, column material
sampling, and writes into the target chunk are checked against that region.
The volume-structure cursor parity test now runs its incremental cursor with
the region enabled before comparing against the reference result.

The plan keeps `ColumnBiomeData` as an `ImmutableWorldData` prerequisite
for this range. It does not create any horizontal `HolderStatus` dependency
or widen ticket/neighbor-holder admission. The full section access envelope
therefore includes the volume-structure vertical range, while the holder
envelope remains independent.

### Follow-up architecture work

The structure anchor cache is shared across vertical sections, but anchor
resolution is still entered from the first section cursor that requests a
candidate. The next structural improvement is to make that shared immutable
anchor assignment an explicit column-scoped product/future, then let each
section cursor consume the resolved placements. Do not narrow the vertical
read envelope until that ownership split removes the full-range anchor scan
from the section stage.

### Validation

This checkpoint requires a fresh Core test run, Windows export, fixed-seed
terrain A/B result, and the existing Godot/WebUI checks on the new HEAD.
No performance improvement is claimed from this architectural enforcement
change.


---

## GeneratedFluids access checkpoint — 2026-11-15

### Runtime enforcement

The generated-fluid stage consumes the current section's
`ColumnMaterializationCache` (surface biome, base height, cut depth and
resolved fluid policy) and the current section's `TerrainDensityVolume`.
It does not sample neighboring columns or read resident neighboring chunks.

The stage now creates a `ChunkGenerationAccessRegion` for
`GeneratedFluids`. Each cached column policy read is checked as a column
read; each local occupancy/density lookup is checked as a position read; and
a non-empty fluid write is checked against the stage's write envelope before
the initial-fluid buffer is mutated.

The plan declares the current `SurfaceTerrainColumn` as an explicit
`ImmutableWorldData` prerequisite and keeps both read and write envelopes
at `None`. The preceding `VolumeStructures` step remains a status
prerequisite. No neighboring holder or ticket radius is introduced.

### Validation

The Windows export for the preceding HEAD completed successfully. Its
validation workflow is still running the Core query baseline and fixed-seed
terrain A/B checks. This new stage integration requires a fresh full
validation run; no benchmark improvement is claimed.

---

## Continuation checkpoint — ticketed residency and pending unload — 2026-11-15

### Implementation on worldgen-perf-fix

Current PR: [#157](https://github.com/sarakborges/asteria/pull/157), base worldgen-remake. Do not merge or deploy without an explicit request.

The ticket/holder lifecycle was extended to preserve Asteria's 16³ vertical sections while retaining Minecraft 26.3's horizontal holder identity:

- ChunkTicket now carries optional SectionY. Asteria's ticket identity is (type, level, SectionY) within an X/Z holder column; this intentionally extends vanilla's (type, level) identity because Asteria stores independently materialized vertical sections.
- Persistent FORCED and PORTAL tickets serialize their vertical targets in gameplay save version 9. Readers continue to accept versions 1–8; version 8 ticket extensions load without a vertical target.
- ChunkTicketStorage.FullMaterializationTargets derives the exact 3D sections whose loading tickets permit FULL. ChunkResidencyRuntime reconciles those into ChunkStreamingState.TicketedSections.
- Ticketed sections remain resident and can be materialized outside player view, but do not join player presentation selection. Ticket-only pending/materializing work is excluded from the loading-screen/bootstrap counters; those counters are maintained incrementally.
- Player-loading admission remains a separate four-slot throttle. Its slots are released when the player's requested sections have reached the holder's ENTITY_TICKING promotion, or when the player no longer requests that column. It does not directly complete holder futures.
- A changed vertical target set increments the holder's generation-request revision even when no generation stage has completed yet. Detached work carrying the older revision is rejected. Cancellation, invalid results and stale outcomes rebuild the full currently requested section set (Desired + Ticketed) for retry rather than requeueing only the old batch.
- ChunkHolderMap now retains a holder in PendingUnloadColumns when the last loading source disappears. A returning ticket rescues and reuses the same holder. Dependency claims also defer removal. ChunkResidencyRuntime finalizes removal only after all vertical sections in the column have been archived, no section remains requested and no materialization task still owns the column.
- VoxelWorld.HasResidentChunksInColumn uses the existing resident-column count index, so pending-unload completion checks residency in O(1) instead of rebuilding a set from all loaded sections.

### Regression coverage added or extended

- Off-view FORCED ticket materializes its exact SectionY and does not enqueue player presentation.
- A new vertical ticket arriving during a worker slice invalidates the earlier holder generation request; all still-live section demand is retried.
- A claim released after a loading ticket arrives leaves the same holder alive.
- A holder survives PendingUnload, is rescued by a returning ticket, and is only removed after its last dependency claim is released.
- Runtime integration: removing the last ticket leaves a resident section and holder pending; RetireDistantChunks archives the section, then the holder is removed.
- VoxelWorld resident-column indexing is tested across insert, archive and restore.

### Minecraft 26.3 queue-level audit

Minecraft 26.3's ChunkHolder has distinct ticketLevel and queueLevel state. ChunkHolder.LevelChangeListener.onLevelChange connects level changes to ChunkTaskDispatcher; the worldgen and lighting dispatchers keep per-chunk runnable batches in priority queues keyed by holder queue level. ChunkMap also provides getChunkQueueLevel and owns the dispatchers.

Primary references:
- [ChunkHolder 26.3](https://aldak0.ru/javadoc/26.3.x/net/minecraft/server/level/ChunkHolder.html) — ticketLevel, queueLevel, getQueueLevel, setQueueLevel, updateFutures.
- [ChunkHolder.LevelChangeListener 26.3](https://aldak0.ru/javadoc/26.3.x/net/minecraft/server/level/ChunkHolder.LevelChangeListener.html) — callback contract carrying old level, new level and a queue-level setter.
- [ChunkTaskDispatcher 26.3](https://aldak.netlify.app/javadoc/26.3.x/net/minecraft/server/level/chunktaskdispatcher) — onLevelChange, submit/release/poll and per-chunk scheduling.
- [ChunkMap 26.3](https://aldak.netlify.app/javadoc/26.3.x/net/minecraft/server/level/chunkmap) — separate worldgen/light dispatchers and getChunkQueueLevel.
- [26.3 pipeline walkthrough](https://minecraftdocs.dev/systems/world/chunk-generation-pipeline) — explains live queue rebucketing when holder levels change.

Asteria currently stores the ticket-derived generation ceiling on GenerationChunkHolder; active work stores a composite ChunkGenerationTaskPriority (loading level, horizontal/total distance, and deterministic coordinate tie-breaks). ChunkGenerationDispatcher.UpdatePriority reorders ready and worker-admission queues, while ChunkColumnPendingQueue maintains a separate dynamic priority context for not-yet-admitted columns. Runtime paths that change player view, add/remove/expire tickets, or admit player-loading tickets refresh active work priorities.

This is a deliberate richer adaptation rather than a literal integer QueueLevel field, but it is not yet the same ownership contract as Minecraft's holder-owned queueLevel plus LevelChangeListener. Do not add a shadow integer without a consumer. The next priority pass should decide whether to centralize Asteria's composite priority behind a holder/column level-change notification, and then test that both pending-column and admitted dispatcher work are reprioritized from every source change without preempting an already admitted worker slice.

### Validation on this checkpoint

- Validate run for implementation HEAD 8f1f5c20: [#43783973288](https://github.com/sarakborges/asteria/actions/runs/43783973288). At the last check the Core test step had passed and the build job had advanced to ReadyToRun verification; WebUI Storybook/screen parity and terrain A/B were still running.
- Windows export: [#43783963371](https://github.com/sarakborges/asteria/actions/runs/43783963371), still running at the last check.
- No terrain performance improvement is claimed until the fixed-seed A/B report completes on the same HEAD.

### Next ordered work

1. Finish validation for the current HEAD and inspect the terrain A/B artifacts.
2. Formalize the composite queue-priority/level-change contract against the 26.3 ticketLevel/queueLevel split; preserve a single source of truth and avoid a decorative QueueLevel property.
3. Continue the bounded generation access-region audit one stage at a time. Cross-column holder-status requirements remain fail-fast until their producer can be admitted/reused non-blockingly with revision-safe future ownership.
4. After access enforcement, make shared immutable column products (especially volume-structure anchor resolution) explicit column-scoped products/futures; preserve deterministic results and the existing 16³ storage/archive ownership.



---

## Continuation checkpoint — bounded access enforcement and holder queue priority — 2026-11-16

### Current branch state

- Branch: `worldgen-perf-fix`
- PR: [#157](https://github.com/sarakborges/asteria/pull/157), base `worldgen-remake`
- Current implementation HEAD: `aae80031506d904ba14dbdd851164cc2a468d5d8`
- Do not merge or deploy without an explicit request.

### Generation access enforcement added

The `ChunkGenerationAccessRegion` audit now covers additional materialization accesses:

- `SectionTerrainSetup` creates a region before column/density setup and checks column reads and shell writes.
- Directional occupancy preparation uses the same region. Its maximum configured palette depth is 64 voxels, so the setup read envelope is horizontal radius 4 and vertical offsets [-4, +4]. The same conservative envelope is declared by `DirectionalFacePalettes`.
- `SectionTerrainLayers` checks column reads and terrain writes. Its immutable-data envelope includes the upper section because submerged-surface selection may sample generated fluid at surfaceY + 1.
- `CaveSpikes` checks column reads, the conservative 64-voxel clearance interval and every emitted segment write. The declared immutable-data envelope remains vertical offsets [-5, +5].
- `ShellFinalization` checks the local shell/lining reads and writes.
- The standalone directional occupancy builder also receives an explicit region; this covers its utility/test path as well as the normal terrain setup path.

These are generator-field/data-access contracts, not holder-status dependencies.

### Loading plan separated from generation plan

Added `AsteriaChunkLoadingPlan` as the explicit projection of the generation DAG's `HolderStatus` requirements. `AsteriaChunkGenerationPlan` continues to describe both immutable generator-data reads and holder prerequisites. Ticket level limits, materialization dependency claims and holder-status expansion now use the loading plan.

Regression coverage asserts that the full generation access envelope (including directional palette data reads) is distinct from the holder-retention envelope. The current data halo does not enlarge the resident-holder radius.

### Queue-priority ownership work

The holder now stores a composite `ChunkGenerationTaskPriority`, a context-initialized flag and a revision. Ticket-level updates preserve the spatial part of the key while replacing its urgency band. Runtime reprioritization refreshes every changed holder even when it has no active materialization task; active worldgen dispatch and lighting/reconciliation workers derive their priority through the same holder-aware path.

The old duplicate `ChunkStreamingPriority` type was removed and remaining streaming consumers were migrated to `ChunkGenerationTaskPriority`. A stale `FromStreaming` adapter and its call sites were removed/migrated after CI exposed them.

**Remaining architectural question:** the dispatcher and pending-column queue still maintain their own ordered queue entries, updated from the holder's composite key. The generation dispatch key is intentionally ticket-level + horizontal/total distance + deterministic coordinate tie-breaks; it does not include movement direction. An earlier `StreamingPriority` signature accepted movement direction but ignored it, and the pending-column queue stored that unused value. The cleanup removes that dead context and avoids reprioritizing active generation solely because heading changed. The remaining pass is to decide whether to introduce a direct holder queue-level change notification instead of retaining parallel update paths, and to verify that ticket-level and player-center changes refresh all pending and admitted queues. Do not add a separate integer `QueueLevel` unless the dispatch queues consume it as their source of truth.

### Validation status

- A previous Windows export run passed on HEAD `3d42e2af`, but its PR validation (including the terrain A/B run) was canceled when later commits arrived. It does not validate this checkpoint.
- The intermediate queue-priority commit `539b0ace` failed compilation because several call sites still referenced the removed `ChunkStreamingPriority`. Those call sites were migrated in subsequent commits.
- The latest HEAD `aae80031506d904ba14dbdd851164cc2a468d5d8` has a fresh validation run queued/in progress. Do not claim final build/test success or a terrain performance improvement until Core, Godot headless, WebUI, Windows export and fixed-seed A/B checks complete on this exact HEAD.

### Next ordered work

1. Validate the current HEAD and inspect the first non-cancelled compiler/test failure, if any.
2. Finish the holder queue-priority audit: make queue ownership/event propagation explicit, and test reprioritization of pending columns, admitted worldgen, lighting and reconciliation work after ticket-level and player-view changes.
3. Continue the bounded-access audit for remaining generation stages, especially surface decorations, directional palettes, structures, wall generation and gravity support. Keep field-data reads separate from resident-holder reads.
4. Make volume-structure anchor assignment an explicit column-scoped immutable product/future; only then reconsider the current [-34, +34] section-stage read range.
5. Inspect fixed-seed terrain A/B and materialized geology artifacts before making any new performance claim.


---

## Continuation checkpoint — surface structure access product — 2026-11-16

### Current branch state

- Branch: `worldgen-perf-fix`
- PR: [#157](https://github.com/sarakborges/asteria/pull/157), base `worldgen-remake`
- Implementation HEAD before this checkpoint: `96a1204b6aaaac17718207e43046bbdba0bca0e5`
- Do not merge or deploy without an explicit request.

### Surface structure read contract

- `SurfaceStructureField` computes a conservative immutable-column read radius from each configured rule's spacing, connector/placement extent and maximum authored proximity distance. The runtime `ChunkGenerationAccessRegion` accepts this content-derived read envelope without expanding the write envelope or holder-retention requirements.
- Structure placement resolution installs the access region for the duration of the query. Both biome and base-surface helpers check the column envelope before serving a cached value, so a warm cache cannot bypass the guard.
- `SurfaceStructures` now publishes its resolved, immutable placement list on the materialization job. The later gravity-support fast path and `StructureObjects` stage consume this product rather than invoking `PlacementsForChunk` again. Jobs fail fast if either consumer runs before the producer stage.
- Regression coverage verifies that a runtime read-envelope override expands immutable reads while writes remain restricted to the target section.

### Remaining audit items

- Inspect any remaining generation entrypoint that can resolve surface placements outside `SurfaceStructures`, especially `ExpandSurfaceRange`; keep explicit user/manual structure queries separate from generation-stage access enforcement.
- Continue checking all dynamic field reads by stage and confirm late consumers use products emitted by their owning stage.
- Keep the VolumeStructures anchor/support-Y search as a separate follow-up: it still needs a column-scoped immutable anchor product/future before the large vertical data halo can be narrowed.

### Validation

- Validate workflow: [#43810247289](https://github.com/sarakborges/asteria/actions/runs/43810247289), on HEAD `96a1204b6aaaac17718207e43046bbdba0bca0e5`, running when this checkpoint was written. Core/headless results not yet reported; WebUI build and Storybook had passed, screen parity was running; fixed-seed terrain A/B was running.
- Windows export workflow: [#43810237876](https://github.com/sarakborges/asteria/actions/runs/43810237876), on the same HEAD and still running.
- No performance improvement is claimed until the fixed-seed A/B report completes on the same implementation HEAD.


---

## Continuation checkpoint — single-owner surface placement product — 2026-11-16

### Current branch state

- Branch: `worldgen-perf-fix`
- PR: [#157](https://github.com/sarakborges/asteria/pull/157), base `worldgen-remake`
- Latest implementation commits:
  - `8a59596c7e` — support-face and fallback child jobs inherit resolved surface placements.
  - `2594d1bd64` — `MaterializeStructures` reuses the inherited product instead of resolving the column again.
- PR remains open and unmerged. Do not merge or deploy without an explicit request.

### Surface placement producer/consumer contract

- The main section's `SurfaceStructures` pass is the only path that resolves placements for that job.
- Gravity-support child jobs receive the immutable `SurfaceStructurePlacements` product from their parent.
- `CanResolveSupportFaceFromTerrain` consumes that product when deciding whether a terrain-derived support face is safe.
- The child `SurfaceStructures` stage also reuses the inherited placements; it no longer calls the column resolver again.
- Both the initial support-face fast path and the sparse fallback child receive the same product, so neither can silently become a second producer.

This fixes an access/lifecycle violation, but the column-scoped step is still represented by a shared immutable field cache and a job product rather than a separately admitted column task/future. A proper column producer/future is still a follow-up if strict execution-scope parity is required.

### Validation on the previous implementation commit

- Windows export on `8a59596c7e`: passed.
- WebUI build, Storybook and screen parity: passed.
- Core suite: 1,892 passed / 2 failed / 1,894 total. Both failures were five-second worker-pool completion waits:
  - `ChunkGenerationWorkerPoolTests.WorkerPublishesDetachedResultToCompletionMailbox`
  - `WorldWorkerPoolTests.CancellationAndFailuresArePublishedAsOperationResults`
- No C# compilation errors were reported. The failures may be ThreadPool/test-runner scheduling contention, but this has not yet been proven; retry the failed job once the parent workflow finishes before classifying them as flakes.
- Fixed-seed terrain A/B was still running when the validation job was inspected. No performance result is claimed.

### Next work

1. Validate the new `2594d1bd64` commit, including the inherited-product path.
2. Retry the two timeout tests only after the previous workflow is terminal; investigate if they reproduce.
3. Complete the fixed-seed terrain A/B and inspect its uploaded evidence before making performance claims.
4. Continue the column-scoped product/future design for `SurfaceStructurePlacements` and `VolumeStructures`; do not narrow the current `VolumeStructureReadHalo` of horizontal radius 1 / vertical offsets [-34, +34] until the true producer has explicit immutable inputs and consumers use the product.
5. Finish auditing the remaining stage access contracts and holder queue-priority propagation.


---

## SurfaceDecorations cross-section read envelope — 2026-11-16

### Runtime access contract

The latest access guards exposed two legitimate cross-section reads in surface decoration materialization:

- Ground placement can inspect density at `surfaceY + 1` when the support voxel is at the upper edge of the target section.
- A nearby-fluid placement rule can inspect the support voxel at `surfaceY` when its decoration cell is at the lower edge.

The stage's immutable data envelope is therefore horizontal radius 2 and vertical section offsets [-1, +1]. Its dependency on `SurfaceTerrainColumn` remains `ImmutableWorldData`; this does not create neighboring `HolderStatus` requirements or widen holder retention/loading.

Regression coverage now asserts the full surface-decoration read envelope while retaining the existing holder-envelope assertions.

### Validation

The prior Core run on HEAD `3649fe4795` reported two failures in `OceanObjectGenerationTests`: ocean-floor decorations at the top section boundary read density in section Y+1 while the stage declared [0, 0]. This checkpoint updates that contract and requires a fresh Core validation. No terrain performance result is claimed until the in-flight fixed-seed A/B evidence is inspected.


### Surface-decoration envelope correction — 2026-11-16

The first enforcement commit renamed the SurfaceDecorations halo and changed its expected test values but left the runtime numeric offsets at [0, 0]. The follow-up commit corrects the actual plan declaration to horizontal radius 2 / vertical offsets [-1, +1], with the runtime reason documented above. The failing ocean-decoration tests therefore remain an explicit validation gate until this corrected HEAD passes.

---

## Gravity-support write guard and current validation — 2026-11-16

- GravitySupport now validates its density read at worldY - 1; follow-up commits corrected the declared immutable-data envelope to horizontal radius 0 / vertical offsets [-1, 0].
- The audit found one remaining unguarded mutation in the same stage: replacing an unsupported gravity voxel with its biome core material. Commit c56caae0c5 now calls EnsureCanWriteWorldPosition(worldX, worldY, worldZ) immediately before the replacement.
- The other SurfaceChunkMaterializer mutation sites inspected in this pass already validate their destination positions before writing.
- The latest validation run observed before this write-guard commit was run 43917182761 on HEAD fddf2c54a235f206588eef9da32c4b6fe2d5658a: WebUI build, Storybook and rendered screen parity passed; Windows export passed on the same HEAD; Core build/test and fixed-seed terrain A/B were still running when inspected.
- The write-guard commit supersedes that validation HEAD. Wait for its new workflow run before treating the guard as validated. No A/B performance conclusion is available yet.
- Next audit target: trace the VolumeStructures cursor's complete read path and determine which field queries can be moved behind a column-scoped immutable anchor product/future. Keep the current horizontal radius 1 / vertical offsets [-34, +34] until that producer boundary is explicit.

---

## Volume-structure column anchor product — 2026-11-16

- VolumeStructureField now resolves candidate X/Z cells and support-Y anchors once per horizontal column into a bounded, resumable product. Vertical section cursors share that product and only rasterize placements intersecting their own 16³ section.
- The existing per-candidate support search cache remains bounded and deterministic; the new per-column product cache is also bounded. Eviction may cause recomputation but cannot change generated placement results.
- The column producer has a separate access region anchored at section Y=0. Its vertical maximum is derived from the active volume-biome support range and template reach; it is not constrained by the old section-relative [-34,+34] halo. The section VolumeStructures stage now consumes the immutable product and retains only its local write boundary.
- The plan now distinguishes VolumeStructurePlacements (column-scoped immutable derivation) from VolumeStructures (section-scoped rasterization). The product's dependencies remain immutable generator data and do not expand holder retention.
- Regression tests cover cross-section product reuse, section-request-order determinism, the authored access boundary, and the tiny-budget resumable cursor.
- Validation for the previous HEAD 68d33e3 remains in workflow [#43930507103](https://github.com/sarakborges/asteria/actions/runs/43930507103): Core/native build steps and WebUI passed; the bounded query baseline and fixed-seed terrain A/B were still running at the last inspection. The new implementation requires fresh validation on its own commit. No performance gain is claimed until that A/B evidence completes.
- This remains an incremental product cache inside the existing column coordinator; it is not yet a separately admitted column future. Cross-column holder dependency admission, holder-owned dispatcher queues and the executable loading pyramid remain open.


## Holder-driven queue reprioritization — 2026-11-16

**Implementation:** `8e6cfa9` with a follow-up batching correction; validation is pending on the follow-up HEAD.

Minecraft Java Edition 26.3's `ChunkTaskDispatcher` is a `ChunkHolder.LevelChangeListener`; its `onLevelChange` path updates the task queue when the holder's effective level changes. The 26.3 API reference is [ChunkTaskDispatcher](https://aldak0.ru/javadoc/26.3.x/net/minecraft/server/level/ChunkTaskDispatcher.html).

Asteria previously stored the composite key on `GenerationChunkHolder`, but `ChunkResidencyRuntime` collected ticket-level changes into a `HashSet` and later scanned active worldgen, lighting and directional-reconciliation maps to propagate them. The holder map now emits `ChunkHolderQueuePriorityChange` with the new composite priority and monotonic revision whenever either ticket urgency or spatial priority changes. `ChunkDistanceManager` forwards the event directly to the residency coordinator.

The residency coordinator applies each holder event directly to the matching column task in `ChunkGenerationDispatcher` (with an O(1) key lookup). Auxiliary lighting/reconciliation workers consume the existing settled ticket-level batch and are reclassified in one pass, avoiding one full task-map scan per changed holder. The pending-column queue also consumes that settled batch in `ChunkStreamingState`, while player-center changes rebuild its distance ordering. The intermediate `_pendingHolderPriorityChanges` set and its deferred second-pass scan were removed.

The active worker priority calculation is now section-local and side-effect free. Only the horizontal column generation task updates the holder's canonical composite key; lighting and reconciliation workers no longer overwrite that holder key with a different vertical section's Y-distance.

**Validation required:** focused Core tests, the full Core suite, Windows export and fixed-seed A/B must complete on the exact new HEAD. This change does not implement cross-column producer admission or the full loading/generation pyramids; those remain separate architecture gaps.


### Priority-event compile correction

The first validation of `99a7102` found a C# local-name collision between the active-holder and unloaded-holder branches in `ChunkHolderMap.SynchronizeLoadingLevels` (CS0136). The variables now have branch-specific names; the next validation must run on the correction commit. The benchmark tool build failed only because it builds the same Core project and hit this compiler error.


### Ticket-propagation test correction

On `f7c07fa`, Core compilation passed and 1,900 of 1,901 tests passed. The new priority-event test initially counted every holder affected by ticket propagation (26) while asserting only the target holder's two transitions. The assertion now filters by holder identity and validates exactly those two transitions. The next CI run must confirm the corrected test on its exact HEAD.


## Deferred holder-future producer admission — 2026-11-16

Minecraft Java Edition 26.3's `ChunkGenerationTask.runUntilWait` yields on an incomplete layer future; the chunk-map continuation resumes the task when that future completes. The layer scheduler has already called `GenerationChunkHolder.applyStep` for the dependent holders, which claims or reuses each status producer before the future wait is installed. See the 26.3 [generation pipeline](https://minecraftdocs.dev/systems/world/chunk-generation-pipeline) and the `ChunkGenerationTask.scheduleLayer`, `scheduleChunkInLayer`, and `runUntilWait` method descriptions in the [26.3 Javadoc](https://aldak0.ru/javadoc/26.3.x/net/minecraft/server/level/ChunkGenerationTask.html).

Asteria's dispatcher already supported non-blocking `WaitForFutures`, but `ChunkGenerationFutureOperation` carried only the future. A dependent could park forever on an allowed holder/status future without requesting the producer that would complete it. The operation now optionally carries an idempotent `RequestProducer` callback. `ChunkGenerationDispatcher.BeginFutureWait` invokes that callback on the serial coordinator before attaching the future continuation. The callback is restricted to producer admission/enqueue and must not block; future completion still returns through the completion mailbox and resumes the waiting task on a later coordinator pump.

A regression test now has one column task request a status future, admit a second column's producer through the same dispatcher, and complete only when that producer runs. It verifies the producer request occurs exactly once on the owner thread, both tasks complete, and no worker remains occupied by the parked wait.

**Boundary:** this installs the producer-admission contract at the dispatcher. The residency runtime still needs to provide a concrete status-target producer factory and the column task must translate cross-column `HolderStatus` requirements into these operations. Same-column section dependencies remain locally coordinated to avoid parent/child scheduler deadlocks.


### Producer-admission failure path

The producer-admission callback is guarded: if the coordinator cannot admit a producer, the dependent task transitions to Failed, its active queue entry and future waits are cleared, and it is retired when no worker/future operations remain. Added a regression test with an incomplete future and a throwing admission callback to verify there is no orphaned waiting task.


### Cross-column holder-status waits in the column coordinator

`ChunkColumnMaterializationGenerationTask` no longer rejects a cross-column `HolderStatus` prerequisite outright. It can now use an injected status-future resolver and producer-admission callback, park through `WaitForFutures`, validate the exact returned status, and resume the dependent section on the parent coordinator. A shared (holder coordinate, status) future is deduplicated across all section consumers; the same task can defer CPU forks from other sections while it awaits external holder status, then dispatch those forks after resumption. Existing same-column vertical status waiters and dynamic GravitySupport dependency scheduling remain unchanged.

A focused test injects one cross-column status requirement for two vertical section consumers and verifies they share a single future and one producer admission before both sections resume.

**Runtime integration boundary:** the default Asteria DAG currently has no cross-column `HolderStatus` dependency; its horizontal procedural read halos are immutable field data and should not retain chunk holders. `ChunkResidencyRuntime` does not yet inject the status-future resolver/producer callback into this task. A future plan that adds a true cross-column holder dependency must supply those two delegates from its active `ChunkDependencyClaim` and status-target producer coordinator. The task layer now supports that contract and fails fast if the producer resolver is absent.


## Target-status parking and promotion on one column task — 2026-11-16

`ChunkColumnMaterializationGenerationTask` now accepts an initial generation target rather than assuming every logical request is FULL. It stops advancing each section once that section has reached the requested stage and parks non-blockingly on a task-owned status-upgrade future. `TryUpgradeTargetStatus` raises the target without replacing the holder task; completing the internal future resumes the same task and preserves its section jobs, completed products and progress revisions.

Status thresholds are normalized to Asteria's actual stage boundaries: structure-start/reference requests can be satisfied at Biomes, InitializeLight continues through Light because Asteria's light-executor operation is part of the same resumable job, and Spawn is satisfied at Full. The task's progress future can now carry a status transition while it parks.

A regression test requests Biomes, verifies the materializer does not advance beyond terrain setup, upgrades the same task to Full, and verifies it resumes and completes without reconstructing the job.

**Runtime boundary:** `ChunkResidencyRuntime` still creates FULL-target tasks from player-loading admissions. Wiring non-FULL holder ticket levels into this target API and reusing an active task when target changes is the next integration step; this commit establishes the task-level lifecycle without claiming runtime pyramid completion.


### Target-promotion test assertion clarification

The first target-promotion test run compiled and reached 1,904/1,905 passing. The only mismatch was test instrumentation: the helper counts one LightExecutor operation and the injected lighting initializer independently incremented the same counter, so one correctly scheduled operation produced a count of two. The test now tracks those two distinct facts separately and asserts one of each.


## Holder-owned generation task identity — 2026-11-16

Minecraft Java Edition 26.3's `GenerationChunkHolder` owns one active `ChunkGenerationTask` reference. `scheduleChunkGenerationTask` admits work against the holder's ticket-derived ceiling; `removeTask` clears that reference only for the task being retired. A ticket-level change updates the ceiling and the generation scheduler retires work whose target is no longer permitted. See the 26.3 [GenerationChunkHolder API](https://aldak.netlify.app/javadoc/26.3.x/net/minecraft/server/level/generationchunkholder) and [chunk generation pipeline](https://minecraftdocs.dev/systems/world/chunk-generation-pipeline).

Asteria now mirrors the holder ownership boundary for its column-scoped `SectionReady` task:

- `GenerationChunkHolder` stores the active `ChunkGenerationTaskInstance` and its admitted target status.
- Admission validates that the task belongs to that holder, is the column-level generation lane, and does not exceed `HighestAllowedGenerationStatus`.
- The runtime registers the task before dispatcher admission and rolls that registration back if the dispatcher rejects it.
- Terminal collection removes holder ownership using the exact task generation. A stale result cannot clear a newer retry.
- When a settled ticket update lowers the holder ceiling below the active task target, the runtime cancels that task. Its holder reference remains until the dispatcher drains and publishes the terminal outcome.

`_materializationTasks` remains runtime bookkeeping for pending timestamps, section sets, dependency claims and result publication; the authoritative active-task identity now lives on the holder, as in Minecraft.

**Remaining boundary:** the default Asteria generation DAG deliberately has no cross-column `HolderStatus` dependency because horizontal terrain/biome/structure reads use immutable generator fields. Consequently, no neighboring holder needs an earlier generation task today. Full future-driven producer scheduling and rescheduling to the highest still-requested status remain unimplemented; do not create artificial resident dependencies merely to imitate Minecraft's 2D chunk terrain reads.


## Lazy holder status futures — 2026-11-17

Minecraft Java Edition 26.3 stores nullable per-status futures. `GenerationChunkHolder.getOrCreateFuture` creates a future only when a consumer requests that status; `findHighestStatusWithPendingFuture` derives the next generation target from incomplete requests. When the ticket ceiling drops, `failAndClearPendingFuturesBetween` completes and removes requests above that ceiling. A later ticket promotion does not resurrect those requests: consumers must request the status again. See the 26.3 [GenerationChunkHolder API](https://aldak.netlify.app/javadoc/26.3.x/net/minecraft/server/level/generationchunkholder).

Asteria now follows the same demand semantics for its column-level status futures:

- Holder construction creates no generation-status futures.
- `GetGenerationFuture(status)` creates only that requested status, or returns immediate success when that status has already been achieved.
- `HighestPendingGenerationStatus` reports the highest incomplete, permitted request. Unrequested statuses are invisible to scheduling.
- Ticket demotion completes and removes pending futures above the new ceiling; a later promotion does not recreate them without another request.
- Generation-progress invalidation fails current waiters and clears old requests so future consumers receive fresh futures under the new revision.

This prepares the holder for vanilla-style rescheduling to the highest still-requested status rather than inferring work from every possible status or ticket level alone. **The producer/scheduler integration is still pending:** `GetGenerationFuture` does not automatically admit a producer yet, and the default Asteria DAG deliberately has no cross-column `HolderStatus` prerequisites while horizontal procedural reads are supplied by immutable generator fields. Do not create artificial resident dependencies to imitate Minecraft's terrain-field reads.


### Task admission creates the holder's requested-status future — 2026-11-17

Minecraft 26.3's `GenerationChunkHolder.scheduleChunkGenerationTask(status, scheduler)` obtains the future for the requested status before admitting/rescheduling the task. Asteria's `TryRegisterGenerationTask` now applies the same contract: successful task admission creates/reuses the matching lazy holder future, so `HighestPendingGenerationStatus` reflects actual scheduled work instead of remaining an observational property. If that status is already completed, admission is rejected because no generation producer is needed.

The column-task registration test verifies the target becomes the holder's highest pending request, completion resolves that exact future, and the pending target disappears when the generation reaches the requested status.


### Status futures use one synchronized slot per status — 2026-11-17

Minecraft Java Edition 26.3 stores status futures in a fixed `AtomicReferenceArray` indexed by `ChunkStatus`, rather than a dynamically growing map. Asteria's holder now uses the equivalent fixed status-indexed slots and one narrow state gate to serialize get-or-create, status completion, ticket-ceiling reconciliation, active-task identity and section-progress mutation. The section-local progress states remain keyed by exact 3D section, but access to that holder-owned map is covered by the same gate.

Regression tests issue concurrent requests for one status and require every caller to receive the same future. A second race test overlaps a status request with ticket demotion and verifies that no incomplete future remains orphaned. This mirrors the synchronization contract of the 26.3 holder while using .NET primitives and preserving Asteria's separate vertical-section state. See [GenerationChunkHolder 26.3](https://aldak.netlify.app/javadoc/26.3.x/net/minecraft/server/level/generationchunkholder).
