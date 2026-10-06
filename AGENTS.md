# Asteria agent rules

Before changing the repository, you MUST read this file, `ARCHITECTURE.md`, and `ENGINEERING_PRACTICES.md`. These three documents are jointly normative. A change is not complete if it knowingly violates them unless the user explicitly requests an exception.

When touching an area that already violates the canon, do not add new code on top of the violation. Fix, reduce, or clearly isolate the violation as part of the same change when practical.

## Architecture

- `Asteria.Core` is engine-agnostic. It must not reference Godot APIs.
- Godot is an adapter for rendering, input, audio and platform integration.
- UI is WebUI. Do not build gameplay UI with Godot `Control` nodes.
- Voxel data is chunk-based. Never create one Godot node per block.
- Heavy world work belongs off the main thread whenever the Godot API is not required.

## Development rules

- The architecture and engineering review checklist in `ENGINEERING_PRACTICES.md` is mandatory before accepting a coherent change block.
- Every mutable gameplay/world fact must have one authoritative owner. Consumers request capabilities; they do not mutate another subsystem's storage directly.
- Engine-agnostic calculation, mesh-data generation, scheduling, simulation, validation and snapshot logic belongs in `Asteria.Core`. Godot owns engine objects and publication only.
- Async/background work must use immutable snapshots or isolated worker state and must reject stale results with revisions/generations.
- Repeated queue, budgeting, revision, cache and scheduling mechanics must use shared primitives instead of local copies.
- Prefer change-driven work. Per-frame polling/recomputation requires a concrete lifecycle or performance reason.
- Hash/dictionary/set iteration order must never decide gameplay, streaming, publication or simulation behavior; define deterministic tie-breakers.
- New unbounded queues, caches, histories, task registries or collections require an explicit bounded lifecycle or a documented reason why unbounded growth is safe.
- New features do not preserve compatibility with old implementations unless explicitly requested.
- Prefer small, explicit modules and deterministic data transformations.
- Do not perform broad repository/tree searches when a targeted path or symbol search will work.
- Keep the first implementation measurable: correctness first, then profile before optimizing.
- Add or update focused tests for engine-agnostic logic when behavior changes.

## WebUI rules

- Gameplay UI follows Atomic Design under `ui/src/components/{atoms,molecules,organisms,templates,pages}`.
- Keep Godot WRY/IPC transport in `ui/src/bridge`; keep event/state binding in `ui/src/controllers`; components must not own transport logic.
- Every reusable WebUI component or page must have a colocated Storybook `*.stories.ts` file covering meaningful visual states.
- Storybook is part of WebUI validation. `npm run build-storybook` must stay green together with `npm run build`.

## Mineclone reference rule

- Before implementing or materially changing a system that already exists in `sarakborges/mineclone`, inspect the equivalent implementation on the `world-systems-rebuild` branch first.
- Use Mineclone as the architecture/behavior reference, then adapt deliberately to Asteria's Godot + C# boundaries rather than copying engine-specific code blindly.
- Mineclone is a reference, not the final authority. For every inspected implementation, identify whether Asteria can achieve the same contract more simply, correctly, efficiently, or with cleaner ownership; prefer the better Asteria design when there is a concrete improvement.
- Preserve the behavioral intent and proven invariants from Mineclone, but do not preserve incidental complexity, engine-driven compromises, duplicated ownership, or legacy constraints when Asteria can avoid them.
- Prefer the rebuild branch over Mineclone `main` for chunk, voxel, world, lighting, meshing, placement, streaming and other world-runtime systems unless the task explicitly concerns old presentation behavior.
