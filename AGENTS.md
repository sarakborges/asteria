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
- Built-in authored content lives under `packs/default/` in the source and is embedded in the managed client assembly for exports (not shipped as a loose default pack). External overlays live in the user data `packs/` directory or in `packs/` inside the local source checkout during development; the user-data copy wins when folder names collide. Runtime resolves pack-owned paths through the selected `PackSelection`; do not reintroduce separate root-level `data/`, `resources/`, `textures/`, or `content/` hardcodes.
- Runtime world saves use `worlds/` under the source project root in Godot/editor and `worlds/` beside the executable in exported desktop builds. World creation, catalog, load, recreation, save, thumbnails, and delete must share the single resolved directory; never route save files through `user://worlds/` or another opaque OS-specific directory.
- Compiled C# mods and their `selection.json` live in `mods/` within that same project/executable directory. The ModLoader must not read DLLs or selection state from `user://`. Keep `mods/` excluded from Godot imports and Git.
- Prefer small, explicit modules and deterministic data transformations.
- Do not perform broad repository/tree searches when a targeted path or symbol search will work.
- Keep the first implementation measurable: correctness first, then profile before optimizing.
- Add or update focused tests for engine-agnostic logic when behavior changes.
- **Hydrology is prohibited.** Do not implement, plan, introduce, port, or reintroduce a hydrology subsystem/pipeline/abstraction in Asteria. Do not use "hydrology" as an architectural owner or roadmap item. Water-related authored features must be modeled explicitly by their actual feature/system when requested; they must never be grouped under a hydrology layer.
- Dimensions are authored runtime boundaries. In-game, dimensions are called **Spheres**; keep `Dimension*` as the technical runtime/content naming. A Sphere explicitly selects `surfaceBiomes` and `volumeBiomes`. Both require data-driven `palette` and `decorations`; volume ownership is determined by `volumeLayout.placement` (`additive` or `carvedVoid`) and the authoritative terrain density. Do not restore an `undergroundBiomes` pool, infer placement from biome IDs, or hard-code Overworld policy.
- Generated surface structures use `SurfaceStructureField` as the sole logical placement/conflict owner. Structure placement is world-space and chunk-independent; ground fit, biome-margin anchoring, deterministic conflicts, reserve-space, connector/StructureSet expansion, and generated-field-fluid restrictions must be resolved before `SurfaceChunkMaterializer` rasterizes accepted operations. Structures may author block, source-fluid and explicit clear payloads, but they never write chunks directly and never mutate `GeneratedFluidField`; the materializer remains the single procedural writer. Structure proximity queries are bounded authored restrictions and must query existing material/generated-fluid owners directly; a fluid-proximity rule or Structure fluid payload is not permission to introduce a hydrology owner.
- Untouched generated-world search/destination questions must use generator capabilities: biome search from `BiomeField`, Structure search from `SurfaceStructureField`, and cross-domain safe destination preparation from `GeneratedSurfaceDestinationQuery`/`BiomeWorldGenerator`. Spawn, warp, locate, dimension travel, portals, diagnostics and future tools must not scan/materialize chunks or reconstruct generation rules from seeds/registries. Mutable loaded-world validation may inspect `VoxelWorld` after residency.
- Every materialized chunk is authoritative persisted spatial state for its dimension, including pristine and empty chunks. Residency eviction/session retirement must archive it rather than regenerate it later. Dirty/mutated is separate metadata. Query-only generator access must not create persistence.
- Loading progress must come only from `WorldLoadingState` observing real streaming/residency/presentation work. World entry, restored session entry and dimension travel reuse the same loading path. First stabilize the complete current bootstrap selection at a horizontal radius capped to two chunks (or the configured render distance if smaller), including explicitly required portal vertical-access chunks; then release gameplay and expand to the configured render distance through ordinary incremental streaming. Distant chunk/fluid work must not block entry once the bootstrap is ready. WebUI is presentation-only: no loading timers, fake percentages, duplicate queues or parallel readiness state.
- Negative world Y is prohibited. The playable voxel world has a hard lower bound at Y=0; do not add negative-Y generation, streaming, placement or world-expansion behavior. Sphere Shell floors provide the authored lower boundary.
- Mutable world/session state is dimension-isolated. Never switch dimension by clearing/reusing the same live world, queues, revisions, archive or in-flight workers; a transition must retire one dimension session and create/restore another explicit session.
- Dimension retirement is cooperative and explicit: stop starting new async work, drain bounded in-flight work, persist authoritative session state, archive resident chunks, retire the session presentation subtree, and only then activate the destination session. Never allow a retired session's async result to publish into the new session.

## WebUI rules

- Gameplay UI follows Atomic Design under `ui/src/components/{atoms,molecules,organisms,templates,pages}`.
- Every reusable WebUI component owns a directory named after the component, containing its `*.tsx`, colocated CSS and Storybook story. Do not place component implementation files flat in an Atomic Design tier.
- React is the WebUI rendering layer. The presentation flow is `bridge -> controllers -> UiStore -> React`. Controllers parse/bind Godot messages and update presentation state; React components render snapshots and emit explicit semantic UI actions through injected callbacks. Components must never import or call the Godot bridge directly.
- Preserve Asteria's current `Winky Rough Variable` font family in WebUI. Do not replace it with a platform/system font unless the user explicitly requests that change.
- Keep Godot WRY/IPC transport in `ui/src/bridge`; keep event/state binding in `ui/src/controllers`; components must not own transport logic.
- Global/gameplay input (keyboard presses, mouse buttons, wheel, captured-mouse controls, gameplay hotkeys and debug hotkeys) is owned by Godot/input code, never by WebUI document/window listeners. WebUI may handle pointer/keyboard events only while the user is explicitly interacting with a UI control/surface; gameplay/global actions must cross the bridge as semantic commands/state instead of raw browser input.
- Every reusable WebUI component or page must have a colocated Storybook `*.stories.ts` file covering meaningful visual states.
- Storybook is part of WebUI validation. `npm run build-storybook` must stay green together with `npm run build`.

## Minecraft Java Edition 26.3 reference rule

- Minecraft Java Edition 26.3 is the sole external reference version for world/chunk lifecycle, ticketing, distance propagation, generation stages, lighting and streaming architecture.
- Before materially changing one of those systems, inspect the corresponding 26.3 behavior and record the source/version used. Adapt its contracts to Asteria's C# and Godot ownership boundaries instead of copying engine-specific code.
- Minecraft is a behavioral and architectural reference, not the final authority. Prefer an Asteria design when it is demonstrably simpler, more correct, more efficient, or has cleaner ownership while preserving the relevant contract.
- Preserve Asteria-specific improvements and its section-based world model; do not reintroduce constraints that belong only to Minecraft's engine or implementation.
- World-creation controls exposed by UI must map to real authored options and runtime behavior. Never add interactive settings that the generator silently ignores.

## Execution continuity and progress protocol

These rules apply to every development session and take precedence over an improvised workflow.

### Session start
- Read `AGENTS.md`, `ARCHITECTURE.md`, `ENGINEERING_PRACTICES.md`, and the active workstream handoff before inspecting or changing implementation code.
- Verify the repository, current branch, working-tree state, latest commit, and associated PR head/base. Never assume the previous session's branch or SHA is still current.
- Continue on the established working branch. For the Minecraft-parity worldgen work, that is `worldgen-perf-fix` / PR #157 unless the user explicitly changes the target. Never switch to `main`, merge, rebase, force-push, or retarget the PR without explicit authorization.
- Read the handoff's completed work, measurements, decisions, blockers and unchecked tasks. Do not repeat a completed investigation or reopen a settled decision without new evidence.

### Scope and execution
- Work on one high-impact, independently verifiable task at a time. State the expected result and acceptance criteria before implementation.
- Prefer a small code-and-test slice over broad speculative analysis. Do not launch unrelated investigations or parallel workstreams.
- Set a bounded investigation/implementation step. A normal step must produce a concrete finding, code change, test result or documented blocker; stop and report if it cannot.
- Do not retry the same failed command or approach indefinitely. After one failure, diagnose; after a second equivalent failure, stop that path and report the blocker and alternatives.
- Long-running tests/benchmarks must have explicit timeouts and progress output. Existing worldgen benchmark process timeout is 15 minutes; do not silently extend it.
- Never leave a task in an ambiguous state. Record whether it is complete, partially complete, blocked, or not started.

### Progress communication
- Send a concise update at the start of a work session describing the branch, current task and acceptance criteria.
- Report after every meaningful checkpoint: inspection complete, implementation slice complete, test/build result, commit created, and PR/CI status.
- Do not remain silent while doing a sequence of work. If work continues without a discrete result, provide a brief status update at least every 5–10 minutes where the execution environment permits.
- If a tool, network request, build or test is blocked, report that promptly instead of waiting silently or implying that work is continuing.
- Updates must contain evidence: files/areas inspected or changed, commands/tests and their outcomes, commit SHA when applicable, and the next bounded action. Do not send empty progress messages.

### Validation and completion
- Define acceptance criteria before coding. For worldgen changes, include correctness/determinism and relevant Minecraft Java Edition 26.3 contract parity; performance changes also require comparable measurements.
- Add focused regression tests for changed behavior. Run the narrowest relevant tests first, then broader validation when justified.
- Do not claim a build, test, benchmark, in-game check, CI run or parity result unless it was actually executed and its result inspected.
- Before committing, inspect the complete diff and check architecture/engineering rules. Keep commits small and reviewable; do not rewrite already-pushed history.
- After each coherent commit, record its SHA, tests and result in the active handoff. Verify the branch head and PR head match before continuing.
- Keep the PR open and do not merge unless explicitly requested.

### Handoff maintenance
- The active workstream handoff is the durable source of truth for current implementation state and next action. Update it after each coherent milestone, not only at the end of a long session.
- Keep completed items, incomplete items, superseded decisions, measurements and blockers distinct. Never mark a roadmap item complete based only on design discussion or code presence.
- End each session by recording: current branch/HEAD, last completed commit, exact unfinished task, acceptance criteria, last test results, blockers, and the first action for the next session.
- On continuation, reconcile the handoff against the actual branch/PR before coding. If documentation and implementation disagree, inspect evidence and correct the handoff; do not blindly trust stale status text.
