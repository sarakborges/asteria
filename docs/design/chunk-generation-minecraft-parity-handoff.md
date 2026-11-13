# Handoff: Minecraft-style chunk-column generation pipeline

**Status:** implementation plan accepted; first implementation slice in progress  
**Repository:** sarakborges/asteria  
**Working branch:** worldgen-perf-fix  
**Pull request:** [#157 — perf(world): optimize worldgen and voxel rendering](https://github.com/sarakborges/asteria/pull/157)  
**PR base:** worldgen-remake  
**Reference snapshot:** Asteria HEAD 76724eff03559adced92102ff3984ecfee19d5ea before this handoff commit  
**Date:** 2026-11-13

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

However, ChunkResidencyRuntime.ExecuteMaterialization currently loops over Advance() until the complete section is built, then initializes lighting, all inside one Task.Run. This makes each section's resumable state private to one occupied worker for its entire build. It does not give the dispatcher a chance to interleave other ready stages when a job yields.

Also, one Advance() is not guaranteed to be a small time slice. Some stages scan substantial parts of a section. The migration must make work units bounded enough for cooperative scheduling; merely moving the existing whole-stage call into a serial dispatcher would create a long single-thread bottleneck.

### 3.3 Streaming priority is recomputed by scanning pending 3D chunks

ChunkStreamingState currently stores _pending as a HashSet<ChunkCoord> and scans it to select the smallest current ChunkStreamingPriority. This gives deterministic tie-breaking, but it is not a priority queue that can efficiently reorder queued logical column tasks after ticket changes.

The new column dispatcher must:

- deduplicate by logical task key;
- use deterministic priority ordering and deterministic tie-breaks;
- update/rebucket queued work when center, movement direction or ticket/streaming level changes;
- preserve enqueue age/fairness so distant work cannot starve indefinitely;
- remove or invalidate queued work when its target is no longer needed.

Do not simply wrap the existing HashSet scan in a new class and call it a priority queue.

### 3.4 World jobs are started by independent pipeline owners

src/Asteria.Godot/Main.cs currently coordinates several independently owned lanes (fluid simulation, terrain mesh, lighting and fluid mesh) and gates their starts with MaxConcurrentWorldJobs. Materialization starts are owned by ChunkResidencyRuntime. This is not the same abstraction as a single world-generation dispatcher plus a shared execution pool.

The refactor should centralize **worker admission and continuation scheduling**, while allowing domain owners (lighting, fluid simulation, terrain meshing) to retain their authoritative state and narrow APIs. Do not turn Main into the scheduler implementation.

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

### 3.6 The MineClone branch is not the Minecraft scheduling blueprint

The required comparison against sarakborges/mineclone world-systems-rebuild was performed for:

- src/world/streaming/generation.rs;
- src/world/chunk_task_queue.rs;
- src/world/chunk_async_work.rs.

That branch currently schedules one async materialization task per 3D ChunkCoord, polls completed tasks, and uses a bounded async-work limiter. It is useful as a reference for bounded tasks, stale revisions, result collection and frame budgets, but it does **not** implement Minecraft's column-scoped ChunkGenerationTask / ChunkStep / runUntilWait architecture. Do not copy its one-task-per-3D-section orchestration as the final design.

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
- [x] Inspect MineClone world-systems-rebuild equivalents and record where they differ from Minecraft.
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
34. Retire the ExecuteMaterialization loop-to-completion design only after the new scheduler owns the lifecycle.

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

- [x] Correct Minecraft 26.3 execution model verified and recorded.
- [x] Current Asteria pipeline and MineClone comparison inspected.
- [x] Detailed work breakdown committed as handoff.
- [x] Reused the existing `ChunkColumnCoord(X,Z)` in `ChunkContentRevision.cs`; added stable ordering and explicit `ToSection(Y)` conversion.
- [x] Added tests for column identity across section Y, section conversion and deterministic ordering.
- [x] Full CI run `43362338139` passed: Release build, ReadyToRun, Core tests, Debug diagnostics, native Godot smoke, worldgen profiles/renders, terrain A/B and WebUI.
- [ ] Define immutable typed step IDs, execution policy, scope and dependency envelopes.
- [ ] No production scheduling path has been switched to the new dispatcher yet.

**Recent commits:**
- Handoff: `c97184967023ec38c75d40f52a771fb7bd433a42`
- Column identity first attempt: `39cc3646039e30d2110d68937989515ec8267358` (superseded by the corrective commit below; retained in history).
- Reuse existing column identity + tests: `6b7a97415c1025d014cc3f805bd06b4901f34fc3`

**Next action:** add only the immutable Core step/dependency primitives and focused tests. Do not yet add a runnable dispatcher, workers, task lifecycle or production integration.
