# Asteria agent rules

Read this file before changing the repository.

## Architecture

- `Asteria.Core` is engine-agnostic. It must not reference Godot APIs.
- Godot is an adapter for rendering, input, audio and platform integration.
- UI is WebUI. Do not build gameplay UI with Godot `Control` nodes.
- Voxel data is chunk-based. Never create one Godot node per block.
- Heavy world work belongs off the main thread whenever the Godot API is not required.

## Development rules

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
