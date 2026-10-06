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

## 3. Thin orchestration; no god objects

Composition roots coordinate coherent subsystems but must not implement their internals.

Refactor when one class/file repeatedly changes for unrelated domains or begins owning unrelated invariants. In particular, a main Godot node must not permanently own streaming policy, worker lifecycle, fluid simulation, lighting, meshing, physics, interaction and UI transport in one implementation body.

Split by real responsibility, not arbitrary line count. Prefer cohesive runtime/controller objects with narrow dependencies over an everything-context.

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
- Worker completion order must not determine gameplay behavior.
- Background tasks are bounded.
- Long-running work that can become irrelevant should be cancellable or cheaply discardable.
- Snapshot creation must not perform hidden O(chunk-volume) reconstruction when a structural copy/share can preserve the same isolation contract more cheaply.

Godot objects stay on the Godot thread unless the API explicitly permits otherwise.

## 7. Queues, scheduling and budgets

Generic queue mechanics are shared primitives.

- Deduplicated FIFO/priority behavior must not be reimplemented as repeated local `Queue + HashSet` pairs.
- Domain queues add only domain rules such as neighbor expansion, due ticks, priority or residency filtering.
- Ordering must have deterministic tie-breakers.
- Predicate scans that can repeatedly miss unchanged work need revision-based miss caching when profiling shows the scan is meaningful.
- Latency-sensitive world loops use a shared frame/work-budget abstraction instead of hand-rolled elapsed-time loops.

Consumers enqueue intent; queue owners control deduplication, fairness and draining semantics.

## 8. Definitions own behavior

Authored behavior belongs to definitions/registries instead of hard-coded IDs.

Examples:

- gravity participation is a block capability/tag, not a sand-ID branch;
- fluid spread, visual properties, lighting behavior and timing belong to `FluidDefinition`;
- block rendering/mining/light behavior belongs to block definitions;
- platform adapters must not special-case gameplay IDs to reproduce definition-owned policy.

Derived immutable metadata should be computed once near the registry owner when it avoids repeated hot-path discovery.

## 9. Lighting and rendering

Lighting is authoritative runtime data; meshes are derived presentation data.

- Light propagation and medium rules live in Core.
- A light change should invalidate only the presentation region that can observe it.
- Terrain and fluid meshes may have distinct invalidation/publication lifecycles.
- Engine-agnostic mesh vertices, batches, culling/greedy logic, collision-face generation and fluid-surface geometry belong in Core.
- Godot rendering code converts those results into `ArrayMesh`, materials, collision shapes and nodes.

Do not rebuild whole chunks when a smaller stable meshlet/dirty region is sufficient.

## 10. Determinism

If behavior depends on order, define the order.

Never rely on:

- `Dictionary` / `HashSet` iteration order;
- filesystem enumeration order;
- task completion order;
- incidental runtime/object identity.

Streaming, simulation, queue priority, content loading and publication must use explicit deterministic tie-breakers where order affects observable results.

## 11. Storage and hot data

Chunk storage is a hot-path data structure.

- Prefer compact contiguous representations.
- Palettes must scale with the number of distinct values, not preallocate one full value slot per voxel without a measured reason.
- Occupancy/frontier indexes should permit sparse iteration when consumers need only occupied cells.
- Cloning/snapshotting should structurally share or directly copy storage when safe rather than reinserting every voxel through general mutation APIs.
- Optimize only after correctness, but obvious structural O(volume) work in a repeated hot boundary should not be preserved merely because the world is currently small.

## 12. Refactor gate

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
