# Asteria agent rules

Before changing the repository, you MUST read this file, `ARCHITECTURE.md`, and `ENGINEERING_PRACTICES.md`. These three documents are jointly normative. A change is not complete if it knowingly violates them unless the user explicitly requests an exception.

When touching an area that already violates the canon, do not add new code on top of the violation. Fix, reduce, or clearly isolate the violation as part of the same change when practical.

## Architecture

- `Asteria.Core` is engine-agnostic. It must not reference Godot APIs.
- Godot is an adapter for rendering, input, audio and platform integration.
- UI is WebUI. Do not build gameplay UI with Godot `Control` nodes.
- Voxel data is chunk-based. Never create one Godot node per block.
- Heavy world work belongs off the main thread whenever the Godot API is not required.

## Quality gate

- Bad code does not enter the repository. Passing tests or appearing to work is not sufficient if the implementation violates the architecture canon, engineering practices, or creates avoidable technical debt.
- Every coherent change must satisfy established software-engineering principles, including SOLID where applicable, single responsibility, explicit ownership, dependency direction, encapsulation, separation of concerns, composition over monoliths, narrow APIs, deterministic behavior, and testable boundaries.
- SOLID and related principles are engineering constraints, not excuses for ceremony. Do not add interfaces, indirection, inheritance, factories, or abstractions unless they protect a real boundary or invariant. The required outcome is clean, cohesive, maintainable code.
- A knowingly poor implementation must be refactored before acceptance. Do not merge temporary shortcuts into `main` with the intention of cleaning them up later unless the user explicitly authorizes that tradeoff.
- If the clean design requires restructuring existing code, prefer the restructuring now while the system is small rather than preserving weak architecture for compatibility.

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
- Packs are Asteria formats, not Godot formats. No pack, including `packs/default/`, may contain or require Godot sidecars/resources such as `.import`, `.godot/`, `.uid`, `.tres`, `.res`, `.tscn`, or engine-specific import metadata. `packs/.gdignore` keeps the entire authored pack tree outside Godot's import scanner. Pack-owned files are read through validated filesystem paths and decoded explicitly; never load a pack-owned asset through `ResourceLoader`.
- One pack is one selectable unit rooted at `packs/{name}/`, with `data/`, `resources/`, and `ui/` subdirectories. Pack layering and override order must be deterministic and explicit.
- `data/` contains gameplay/content definitions; `resources/` contains world/item/audio/font/model presentation assets; `ui/` contains declarative WebUI theme/assets. UI packs must not inject arbitrary HTML/JS or own bridge/controller logic.
- Built-in authored content lives under `packs/default/`. Runtime code resolves every pack-owned path through the selected `PackSelection`; do not reintroduce separate root-level `data/`, `resources/`, `textures/`, or `content/` hardcodes.
- Prefer small, explicit modules and deterministic data transformations.
- Do not perform broad repository/tree searches when a targeted path or symbol search will work.
- Keep the first implementation measurable: correctness first, then profile before optimizing.
- Add or update focused tests for engine-agnostic logic when behavior changes.
- **Hydrology is prohibited.** Do not implement, plan, introduce, port, or reintroduce a hydrology subsystem/pipeline/abstraction in Asteria. Do not use "hydrology" as an architectural owner or roadmap item. Water-related authored features must be modeled explicitly by their actual feature/system when requested; they must never be grouped under a hydrology layer.
- Dimensions are authored runtime boundaries. In-game, dimensions are called **Spheres**; keep `Dimension*` as the technical runtime/content naming unless a deliberate codebase-wide rename is requested. A dimension explicitly selects separate `surfaceBiomes`, `volumeBiomes` and `undergroundBiomes` pools and owns dimension-wide generation seed derivation inputs, sea level, gravity, spawn policy, optional Sphere Shell floor/roof bounds and environment presentation data. Surface, volume and underground biome identity are distinct domains; do not infer one from another, infer active dimension policy from biome ID prefixes, or hard-code Overworld behavior in runtime systems.
- Generated surface structures use `SurfaceStructureField` as the sole logical placement/conflict owner. Structure placement is world-space and chunk-independent; ground fit, biome-margin anchoring, deterministic conflicts, reserve-space, connector/StructureSet expansion, and generated-field-fluid restrictions must be resolved before `SurfaceChunkMaterializer` rasterizes accepted operations. Structures may author block, source-fluid and explicit clear payloads, but they never write chunks directly and never mutate `GeneratedFluidField`; the materializer remains the single procedural writer. Structure proximity queries are bounded authored restrictions and must query existing material/generated-fluid owners directly; a fluid-proximity rule or Structure fluid payload is not permission to introduce a hydrology owner.
- Untouched generated-world search/destination questions must use generator capabilities: biome search from `BiomeField`, Structure search from `SurfaceStructureField`, and cross-domain safe destination preparation from `GeneratedSurfaceDestinationQuery`/`BiomeWorldGenerator`. Spawn, warp, locate, dimension travel, portals, diagnostics and future tools must not scan/materialize chunks or reconstruct generation rules from seeds/registries. Mutable loaded-world validation may inspect `VoxelWorld` after residency.
- Negative world Y is prohibited. The playable voxel world has a hard lower bound at Y=0; do not add negative-Y generation, streaming, placement or world-expansion behavior. Sphere Shell floors provide the authored lower boundary.
- Mutable world/session state is dimension-isolated. Never switch dimension by clearing/reusing the same live world, queues, revisions, archive or in-flight workers; a transition must retire one dimension session and create/restore another explicit session.
- Dimension retirement is cooperative and explicit: stop starting new async work, drain bounded in-flight work, persist authoritative session state, archive resident chunks, retire the session presentation subtree, and only then activate the destination session. Never allow a retired session's async result to publish into the new session.

## WebUI rules

- Gameplay UI follows Atomic Design under `ui/src/components/{atoms,molecules,organisms,templates,pages}`.
- Keep Godot WRY/IPC transport in `ui/src/bridge`; keep event/state binding in `ui/src/controllers`; components must not own transport logic.
- Global/gameplay input (keyboard presses, mouse buttons, wheel, captured-mouse controls, gameplay hotkeys and debug hotkeys) is owned by Godot/input code, never by WebUI document/window listeners. WebUI may handle pointer/keyboard events only while the user is explicitly interacting with a UI control/surface; gameplay/global actions must cross the bridge as semantic commands/state instead of raw browser input.
- Every reusable WebUI component or page must have a colocated Storybook `*.stories.ts` file covering meaningful visual states.
- Storybook is part of WebUI validation. `npm run build-storybook` must stay green together with `npm run build`.

## Mineclone reference rule

- Before implementing or materially changing a system that already exists in `sarakborges/mineclone`, inspect the equivalent implementation on the `world-systems-rebuild` branch first.
- Use Mineclone as the architecture/behavior reference, then adapt deliberately to Asteria's Godot + C# boundaries rather than copying engine-specific code blindly.
- Mineclone is a reference, not the final authority. For every inspected implementation, identify whether Asteria can achieve the same contract more simply, correctly, efficiently, or with cleaner ownership; prefer the better Asteria design when there is a concrete improvement.
- Preserve the behavioral intent and proven invariants from Mineclone, but do not preserve incidental complexity, engine-driven compromises, duplicated ownership, or legacy constraints when Asteria can avoid them.
- Prefer the rebuild branch over Mineclone `main` for chunk, voxel, world, lighting, meshing, placement, streaming and other world-runtime systems unless the task explicitly concerns old presentation behavior.
