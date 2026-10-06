# Asteria Architecture Canon

This document is normative together with `AGENTS.md` and `ENGINEERING_PRACTICES.md`.
It defines the architectural contracts that implementations and refactors must preserve.
Mineclone `world-systems-rebuild` is a reference for proven invariants, not an authority over Asteria's design.

## 1. One authoritative owner

Every mutable gameplay or world fact has exactly one authoritative owner.

- Do not mirror authoritative state into a second mutable flag, map, queue or cache just to simplify a consumer.
- Derived views may cache results only with explicit invalidation and lifetime rules.
- Callers use capability methods/commands instead of reaching into another subsystem's storage.
- A mutation owner is responsible for all mandatory consequences of that mutation.

For voxel content, the mutation runtime is the canonical boundary. Block and fluid changes must not independently reproduce remesh, lighting, physics, scheduling or revision side effects in adapters.

## 2. Runtime boundaries

### Asteria.Core

`Asteria.Core` owns engine-independent domain/runtime behavior:

- voxel/chunk data and coordinates;
- authored definition models and runtime IDs;
- mutation semantics;
- block physics and fluid simulation;
- lighting propagation;
- streaming state and scheduling policy;
- queue/revision/cache primitives;
- immutable worker snapshots;
- mesh-data generation and other pure render preparation that does not require Godot APIs.

It must not reference Godot.

### Asteria.Godot

Godot is an adapter. It owns:

- input and platform integration;
- Godot nodes, resources, materials, shaders and physics-engine objects;
- conversion of engine-agnostic mesh/presentation data into Godot objects;
- publication/retirement of presentation objects;
- orchestration needed specifically to cross the Godot main-thread boundary.

Godot code must not become a second owner of world simulation state.

### WebUI

WebUI owns presentation and browser-side interaction state.

- Transport remains in `ui/src/bridge`.
- Message/state binding remains in `ui/src/controllers`.
- Reusable presentation follows Atomic Design.
- Components do not own game transport or authoritative gameplay state.
- Godot is the authoritative input boundary for global/gameplay keyboard and mouse actions. WebUI must not register document/window-level keyboard or mouse handlers for gameplay, camera, hotkeys or debug toggles.
- WebUI may consume browser input only for explicit UI interaction (for example clicking a button, typing in a field or navigating a focused menu). It publishes semantic UI intent; it does not forward raw gameplay input.

## 3. Thin orchestration; no god objects

Composition roots coordinate coherent subsystems but must not implement their internals.

Refactor when one class/file repeatedly changes for unrelated domains or begins owning unrelated invariants. In particular, a main Godot node must not permanently own streaming policy, worker lifecycle, fluid simulation, lighting, meshing, physics, interaction and UI transport in one implementation body.

Split by real responsibility, not arbitrary line count. Prefer cohesive runtime/controller objects with narrow dependencies over an everything-context.

Current world-work ownership follows that rule explicitly:

- `FluidSimulationRuntime` owns the fluid worker lifecycle, drained in-flight batch, stale/error recovery, authoritative result application and authored rescheduling.
- `LightingRuntime` owns the lighting worker lifecycle, in-flight batch recovery, stale-result requeue and Core lighting-result integration.
- `TerrainMeshPipeline` and `FluidMeshPipeline` are Godot-side publication pipelines: they own mesh worker start/poll/requeue and enqueue accepted DTOs into `ChunkPresentationController`; they do not own voxel content.
- `ChunkStreamingController` composes `ChunkResidencyRuntime` with `ChunkPresentationController` in pre/post world-work frame phases. Residency policy/materialization stays in Core; Godot only coordinates presentation lifecycle.
- `ChunkResidencyRuntime` depends on the narrow Core `IChunkProvider` materialization boundary instead of hard-coding a generator. The provider may be invoked concurrently and must be deterministic/thread-safe for immutable inputs.
- `BiomeWorldGenerator` is the production surface provider. It owns biome sampling, terrain height/material resolution, authored surface patches and ground decorators; streaming owns only when generation work is scheduled/accepted.
- `BlockEntityFrameController` coordinates tick-gated block-physics wakeups, continuous falling/drop advancement and Godot presentation synchronization. `BlockPhysicsRuntime` and `DroppedBlockRuntime` remain the authoritative Core owners; the controller adds no mirrored gameplay state.
- `Main` remains the composition root and frame/input/bridge orchestrator. It may invoke those owners and report diagnostics, but must not reimplement their scheduling or mutation rules.

## 4. Mutation pipeline

Meaningful world state changes flow through intent-revealing mutation APIs.

A successful voxel mutation must atomically establish the authoritative content change and enqueue/invalidate every derived subsystem that depends on it. Consumers must not manually duplicate these consequences.

Current dependency classes include, as applicable:

- terrain mesh invalidation;
- fluid mesh invalidation;
- content revisions/stale-worker rejection;
- lighting work;
- fluid topology/scheduled work;
- block physics wakeups;
- persistence/session-dirty state.

No-op mutations must not publish revisions or wake derived work.

Block/fluid occupancy is mutually exclusive at the voxel-content boundary.

- Placing or settling a non-empty block into a fluid cell replaces that cell's fluid as part of the same authoritative block mutation.
- Fluid mutation must reject non-empty fluid in a solid block cell.
- Removing a block wakes fluid topology immediately; actual refill/spread still obeys the authored fluid timing instead of bypassing simulation.
- One authoritative mutation publishes one consequence fan-out. A block mutation that also displaces fluid must not double-bump fluid revisions or duplicate remesh/lighting/topology invalidation.
- Cross-chunk mesh halos, revisions and fluid topology must be invalidated from the edited world position, including loaded neighbors whose one-voxel dependency halo contains it.

## 5. Chunks and lifecycle states

Chunk content, residency, simulation readiness and presentation are different facts.

- A resident chunk is not automatically presentation-ready.
- A presentation is derived state and may be rebuilt/discarded without mutating authoritative content.
- Worker snapshots are not authoritative live chunks.
- Eviction must explicitly retire adapter presentation and domain scheduling state that cannot survive residency.
- Restoring authoritative content must re-establish derived work required by the current runtime.

Do not collapse these lifecycle states into one boolean.

## 6. Async work and snapshots

Background work is allowed only when ownership is explicit.

- Prefer immutable snapshots or isolated worker copies.
- Every result whose inputs may change carries revisions/generations and is rejected when stale.
- Stale validation must cover the exact dependency envelope of the snapshot, including residency of chunks that were absent when the snapshot was captured; unrelated residency outside that envelope must not invalidate correct work.
- Worker completion order must not determine gameplay behavior.
- Background tasks are bounded.
- Long-running work that can become irrelevant should be cancellable or cheaply discardable. Dimension retirement uses cooperative quiescence: no new work starts after retirement begins, already-running bounded snapshot jobs are drained, and their owning session remains isolated until completion.
- Draining a queue transfers ownership of that batch to exactly one in-flight worker owner. Start failure, worker exception, null completion, stale dependency validation or publication rejection must either requeue the still-relevant batch or explicitly retire it; drained work must not disappear silently.
- Detached world entities without persistence/pickup ownership must still have explicit population and lifetime bounds; bounded capacity and expiry are required until a higher-level lifecycle owns them.
- Detached block drops also participate in deterministic entity contact resolution. Broadphase/contact state stays bounded by the active-drop cap, pair ordering is deterministic, and separation must respect voxel collision instead of pushing drops through terrain.
- Snapshot creation must not perform hidden O(chunk-volume) reconstruction when a structural copy/share can preserve the same isolation contract more cheaply.
- Terrain/fluid mesh snapshots capture only chunks whose one-voxel halo is observed by the selected meshlets. Their dependency stamp tracks both content revisions for loaded chunks and residency epochs for the complete envelope, including currently absent neighbors, so a newly loaded dependency makes the result stale.

Godot objects stay on the Godot thread unless the API explicitly permits otherwise.

## 7. Queues, scheduling and budgets

Generic queue mechanics are shared primitives.

- Deduplicated FIFO/priority behavior must not be reimplemented as repeated local `Queue + HashSet` pairs.
- Domain queues add only domain rules such as neighbor expansion, due ticks, priority or residency filtering.
- Ordering must have deterministic tie-breakers.
- Predicate scans that can repeatedly miss unchanged work need revision-based miss caching when profiling shows the scan is meaningful.
- Latency-sensitive world loops use a shared frame/work-budget abstraction instead of hand-rolled elapsed-time loops.
- Meshlet-mask queues use the shared deterministic bounded-drain primitive. Terrain and fluid mesh workers both cap work per job; a large fluid remesh backlog must not be drained into one unbounded worker batch.

Consumers enqueue intent; queue owners control deduplication, fairness and draining semantics.

## 8. Definitions own behavior

Authored behavior belongs to definitions/registries instead of hard-coded IDs.

Examples:

- gravity participation is a block capability/tag, not a sand-ID branch;
- fluid spread, visual properties (including optional pack-relative texture paths), lighting behavior, timing and movement response belong to `FluidDefinition`; Godot resolves authored fluid textures through the selected pack rather than special-casing fluid IDs;
- fluid immersion is queried from authoritative voxel/fluid data in Core; engine adapters consume that contact state for swimming instead of inventing a second fluid-occupancy model. Passive immersion sinks; upward velocity requires explicit swim input;
- fluid movement response is authored data: horizontal speed multiplier/acceleration, passive sink speed, swim ascent, surface-exit speed, vertical acceleration and exit margin must not be hard-coded to a fluid ID in the player adapter;
- block rendering/mining/light behavior belongs to block definitions; block visuals are distinct from physical occupancy, so a non-collidable `crossedSprite` can share the normal voxel lifecycle without pretending its render planes are collision geometry;
- placement-face capability is authored data; ground plants restrict placement to the top face instead of branching on grass/mushroom IDs;
- platform adapters must not special-case gameplay IDs to reproduce definition-owned policy.

Derived immutable metadata should be computed once near the registry owner when it avoids repeated hot-path discovery.

### Geometry and collision

`BlockGeometry` is the canonical engine-agnostic occupancy model for authored cube/layer/hollow shapes, orientation and microblock masks.

- Ray targeting, AABB narrow-phase collision, support/attachment checks, fine render geometry and collision-face generation must derive from the same `BlockGeometry` occupancy rules. Do not add a second shape interpretation in Godot or another raycaster.
- `support_below` is a surface-coverage contract: the dependent block's occupied bottom-face footprint must be covered by collidable geometry on the support block's top face. A non-empty voxel or a single touching microblock is not sufficient.
- Voxel-cell ownership is distinct from physical shape occupancy. Until the storage model explicitly supports co-occupancy, a non-empty block still owns its voxel cell even when its physical geometry occupies only part of that cell; systems must not conflate this storage invariant with collision geometry.
- Collision meshes are generated from Core geometry and Godot only publishes the resulting physics shape.
- Crossed-sprite visuals are meshed in Core into the same chunk/meshlet render batches as terrain. They are double-sided cutout presentation, contribute no voxel occlusion/collision, and must remain inside their owning voxel so existing meshlet dependency envelopes stay correct.

## 9. Lighting and rendering

Lighting is authoritative runtime data; meshes are derived presentation data.

- Light propagation and medium rules live in Core.
- Block and fluid light emission are authored RGB data. Fluid emission scales with fill level; block dampening scales with actual authored/microblock geometry; fluid dampening scales with fill level.
- RGB block light propagates per channel with deterministic strongest-channel composition. Asteria deliberately does not inherit Mineclone's HSI-specific source-reset machinery because the RGB field does not require it for convergence.
- Direct skylight is computed only through contiguous loaded vertical chunk segments. An unloaded vertical gap is provisional open sky; chunk residency changes must enqueue reconciliation so newly known blockers/openings converge incrementally.
- A light change should invalidate only the presentation region that can observe it. Terrain refresh work is omitted for block-empty chunks and fluid refresh work is omitted for fluid-empty chunks; lighting reconciliation must not manufacture no-op mesh backlog.
- Applying an accepted lighting worker result is a Core responsibility. Changed voxel positions are coalesced into chunk/meshlet halo masks before terrain/fluid revisions and remesh queues are published, so one relight does not repeatedly bump the same meshlet.
- Terrain and fluid meshes may have distinct invalidation/publication lifecycles.
- Initial chunk presentation is admitted only after every currently desired chunk in its one-chunk mesh dependency halo is resident. This avoids knowingly starting snapshots that active streaming is about to invalidate while still allowing absent chunks outside the desired selection to behave as stable open boundaries.
- Presentation membership alone is not a reason to remesh neighbors. When a newly presented resident chunk can affect an already-presented neighbor, topology invalidation is content-aware at the shared dependency boundary; fluid-empty chunks do not enqueue an initial fluid mesh build.
- Lighting refresh is presentation dirtiness, not a structural voxel mutation. It may enqueue a follow-up remesh but must not invalidate a structurally current mesh result solely because vertex lighting changed while that result was in flight.
- Interactive block edits use a dedicated latency terrain lane with its own bounded worker and priority publication queue. Background streaming/remesh work uses a separate worker, so a player-visible placement/destruction cannot wait for an already-running background batch or sit behind queued background publications.
- Fluid volume sides and bottoms keep back-face culling to avoid alpha-sorted interior walls/triangles. Only an exposed fluid top surface emits dedicated reverse-wound underside geometry so the water surface remains visible from below without making the entire translucent volume double-sided.
- Engine-agnostic mesh vertices, batches, culling/greedy logic, collision-face generation and fluid-surface geometry belong in Core.
- Godot rendering code converts those results into `ArrayMesh`, materials, collision shapes and nodes.

Do not rebuild whole chunks when a smaller stable meshlet/dirty region is sufficient.

## 10. Dimensions, world generation and biomes

Dimensions are the top-level authored world-runtime boundary.

- `DimensionRegistry` owns immutable definitions loaded from `packs/{selected}/data/dimensions/`; `DimensionId` is a validated strong runtime identity rather than an arbitrary string.
- A dimension explicitly selects its biome pool. Biome membership is not discovered by scanning ID prefixes; referenced biomes are validated to belong to that dimension.
- One root world seed deterministically derives a distinct dimension seed from the dimension identity. All generation inside that dimension consumes the derived seed, so equivalent coordinates in different dimensions are independent without requiring unrelated random state.
- Dimension definitions own sea level, world-wide gravity, authored spawn coordinates, optional Sphere Shell floor/roof bounds and environment presentation inputs. In-game these worlds are called **Spheres**; `Dimension*` remains the technical runtime naming. Surface biome terrain authors height offsets relative to the dimension sea level; adapters and individual biomes must not maintain competing absolute baseline heights. Gravity is consumed by both player movement and block/drop physics; adapters must not maintain a competing hard-coded gravity value.
- World Y is non-negative by contract. Generation/streaming must never expand below Y=0. A Sphere may author a shell floor and/or roof; each boundary is a one-voxel plane materialized by the dimension generator, and shell block breakability remains definition-owned rather than special-cased by ID.
- Dimension environment data is engine-agnostic RGB/energy/fog data. Godot's `DimensionEnvironmentPresentation` is only the adapter that maps it into a `WorldEnvironment`.
- An active runtime session owns exactly one dimension and one `VoxelWorld` plus its queues/revisions/archive/workers. Dimension changes never reuse those transient owners by clearing them in place.
- `DimensionRuntimeSession` is the Godot-side session composition boundary. It owns fresh queues, revisions, simulation/mesh workers, streaming controllers and one presentation subtree for exactly one `DimensionSessionState`.
- `DimensionSessionStateStore` is the bounded Core owner of inactive dimension state. It contains at most one state per authored dimension and preserves the dimension's `VoxelWorld` archive, world tick, player position, falling-block snapshot and dropped-block snapshot.
- Transition is cooperative: the active session stops accepting new worker work, drains already in-flight materialization/simulation/lighting/mesh jobs, snapshots detached entities, archives all resident chunks deterministically, retires the complete presentation subtree, then constructs/restores the destination session. No worker result from the retired session can publish into the new one.
- Session retirement drops pristine resident chunks and keeps only dirty chunk state in the in-memory archive; unchanged terrain is rematerialized from the dimension provider on return. This keeps inactive-session memory tied to authored edits/state instead of render distance.
- Dimension transition accepts an explicit destination position. Portal-style callers can preserve exact coordinates across dimensions, while each dimension state also retains its last player position for resume-style activation.

Surface world generation is an engine-agnostic Core domain.

- `BiomeRegistry` owns validated authored biome definitions loaded from `packs/{selected}/data/biomes/`.
- Surface biome IDs are dimension-qualified (for example `asteria:overworld/swamp`) for ownership validation and stable identity. The active dimension definition supplies the exact pool consumed by `BiomeField`.
- `BiomeField` owns deterministic 2D surface-biome placement. Definitions are sorted by canonical ID before spatial rules are built, so JSON/filesystem insertion order cannot change the world.
- Biome layout uses deterministic organic formation seeds rather than chunk/grid ownership. `regionSize` controls target formation span, authored weights affect seed assignment and `cannotBorder` is enforced while assignments are established.
- Sampling returns a primary biome plus normalized nearby influences. Terrain height is blended from those influences; biome boundaries must not introduce a discontinuous height cut merely because the primary ID changed.
- Surface material ownership is chosen deterministically from the influence weights. Authored surface patches are world-space deterministic and cross chunk boundaries without using chunk-local randomness.
- Ground decorators are a separate authored layer from terrain density/materials. Their effective chance is weighted by biome influence and they may restrict allowed supporting surface blocks. Decorators write normal block content and therefore reuse support, rendering, drops and mutation semantics instead of owning a parallel object store.
- Generation entropy is domain-separated and based only on the world seed, stable domain names and world coordinates. Hash/dictionary iteration order, task order and chunk materialization order cannot change generated content.
- `BiomeWorldGenerator` has no mutable generation cache shared between worker calls; chunk materialization is safe to dispatch concurrently.
- Surface profiles may span arbitrary positive vertical chunks. Streaming selection samples the generator-owned minimum/maximum surface height for each desired horizontal chunk column, keeps deterministic padding below/above that range, and also retains a bounded player-local vertical band. Spawn readiness follows the player's actual initial streaming chunk rather than assuming Y=0.
- **Hydrology is explicitly prohibited.** Asteria must not have a hydrology subsystem, pipeline, generation pass, abstraction, owner, or roadmap item. Water-related world features, when explicitly requested, are modeled by their concrete feature/domain and must not be routed through or generalized into a hydrology layer.
- Caves and true 3D/volume biomes are future generation domains. They must extend the same single-owner generation pipeline rather than independently rewriting surface voxels after generation.

`DeterministicChunkProvider` remains only as a QA/test fixture and is not the production world-generation owner.

## 11. Determinism

If behavior depends on order, define the order.

Never rely on:

- `Dictionary` / `HashSet` iteration order;
- filesystem enumeration order;
- task completion order;
- incidental runtime/object identity.

Streaming, simulation, queue priority, content loading and publication must use explicit deterministic tie-breakers where order affects observable results.

## 12. Pack and content boundaries

Asteria packs are engine-agnostic content bundles owned by the game, not by Godot.

- One enabled pack is addressed by one validated name and rooted at `packs/{name}/`.
- A pack contains three explicit domains:
  - `data/`: authored gameplay/content definitions such as blocks, fluids, biomes, structures, recipes, loot and dimensions;
  - `resources/`: textures, audio, fonts, models and other non-UI presentation assets;
  - `ui/`: declarative WebUI theme tokens and UI-specific presentation assets.
- The built-in initial pack is `packs/default/`. `PackSelection.Default` selects it at startup; loaders receive the same selection value rather than hard-coding individual roots.
- Data remains authoritative gameplay input; resources/UI remain presentation input. Presentation content must not become a second gameplay owner.
- UI customization is declarative. Packs may override validated theme tokens and UI assets, but must not ship arbitrary HTML/JavaScript into the trusted WebUI runtime or own bridge/controller behavior.
- Pack-relative authored references remain logical. For example, a block texture may stay `textures/blocks/stone.png`; the resource resolver maps it to `packs/{selected}/resources/textures/blocks/stone.png`.
- External packs use Asteria-owned manifests and namespaced identifiers. Loading resolves definitions/assets into the same immutable registries/presentation models used by built-in content.
- Pack precedence is deterministic. The base/default pack is the lowest layer; future enabled overlays must be applied in an explicit ordered list.
- No pack may depend on Godot import artifacts or project resources. `.import`, `.godot/`, `.tres`, `.res`, `.tscn`, Godot UID files and engine-specific importer state are never part of the external pack contract.
- Repository-local `.import` sidecars may remain beside built-in assets under `packs/default/resources/` because they preserve editor import settings. They are build/editor metadata only.
- Pack parsing, validation, path normalization, dependency/version checks and layering must remain outside gameplay/presentation component logic.
- Data is declarative by default. Executable plugins/scripts are a separate future extension boundary.
- Derived pack caches may exist for performance, but cache format is never authored pack format.
- Pack paths and IDs must be deterministic and portable across operating systems; case/collision rules are validated explicitly.

The authored contract is defined in [PACKS.md](PACKS.md).

## 13. Storage and hot data

Chunk storage is a hot-path data structure.

- Prefer compact contiguous representations.
- Palettes must scale with the number of distinct values, not preallocate one full value slot per voxel without a measured reason.
- Occupancy/frontier indexes should permit sparse iteration when consumers need only occupied cells.
- Cloning/snapshotting should structurally share or directly copy storage when safe rather than reinserting every voxel through general mutation APIs.
- Runtime state that leaves its owning chunk must not retain chunk-local palette identifiers. Detached/falling/dropped blocks carry portable value snapshots and re-intern local palette data when materialized into another chunk.
- Optimize only after correctness, but obvious structural O(volume) work in a repeated hot boundary should not be preserved merely because the world is currently small.

## 14. Refactor gate

A touched area must be reviewed for architecture debt. Refactor or isolate the debt when any of these are true:

- more than one mutable owner exists for a fact;
- a consumer bypasses the intended mutation boundary;
- one orchestrator owns unrelated domains;
- engine/framework types leak into Core;
- engine-agnostic code lives in an adapter layer;
- async results can apply without revision validation;
- repeated queue/budget/cache mechanics are locally duplicated;
- unchanged inputs still trigger expensive repeated work;
- a cache/queue/task registry has no lifecycle bound;
- tests require large unrelated setup because responsibilities are coupled.

New features do not preserve old architecture merely for compatibility unless the user explicitly requests it.
