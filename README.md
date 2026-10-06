# Asteria

Fresh Godot + C# rewrite of Asteria.

## Architecture canon

Repository changes must follow [AGENTS.md](AGENTS.md), [ARCHITECTURE.md](ARCHITECTURE.md), and [ENGINEERING_PRACTICES.md](ENGINEERING_PRACTICES.md). These documents are jointly normative for implementation and refactoring. External content packaging follows [PACKS.md](PACKS.md).

## Current runtime foundation

The current milestone proves the base runtime architecture without introducing world generation yet:

- engine-agnostic voxel runtime in `Asteria.Core`;
- immutable, data-driven block definitions with namespaced IDs and compact runtime IDs;
- `VoxelCell` runtime state separated from block definitions;
- 16×16×16 chunks with compact palette storage: `ushort` voxel indices, dynamic palettes, O(1) value lookup, reusable palette slots and sparse occupancy bitsets;
- multi-chunk `VoxelWorld` runtime with world-space reads/writes across chunk boundaries;
- chunk residency is a Core lifecycle concern: selection, archive-first restore, bounded materialization and eviction are owned by `ChunkResidencyRuntime`, while Godot owns only presentation nodes and publication;
- residency and presentation are distinct facts; meshlet publications are deduplicated per chunk/meshlet and stale publications are revision-checked before touching Godot objects;
- block interaction decisions live in Core: break/place resolve against the authoritative target, loaded/occupied state, authored support rules and the placed block's real collision geometry before the mutation boundary is invoked;
- `BlockGeometry` is the single shape-occupancy oracle for cube/layer/hollow/oriented/microblock geometry; world raycast, AABB collision, support footprints, fine meshing and collision-face generation share it, and the obsolete duplicate chunk-local fine raycaster was removed;
- `support_below` now requires actual bottom-footprint coverage by the support block's collidable top face, so thin layers or isolated microblocks cannot falsely support a full-width dependent block;
- voxel targeting uses macro-voxel DDA and enters the 32³ narrow phase only for partial or microblock geometry, preserving exact layer/hollow/microblock hits without fine-stepping through ordinary empty/cube space;
- one authoritative voxel-mutation runtime for block and fluid edits: successful mutations enforce block/fluid co-occupancy rules and publish all required terrain/fluid mesh, lighting, topology and block-physics consequences through one owner;
- shared deterministic work primitives own deduplicated FIFO scheduling and adaptive frame budgets instead of local queue/set and stopwatch patterns;
- resident chunks carry a globally unique residency epoch paired with their local content revision; async work captures dependency stamps and rejects results from changed or reloaded chunks;
- worker snapshots structurally clone compact chunk storage instead of rebuilding 4096 voxels through mutation APIs; lighting snapshots capture only relevant horizontal chunk columns plus column-residency stamps so unrelated world edits do not invalidate correct work;
- fluid simulation, lighting, terrain meshing and fluid meshing each have a bounded single-flight Core worker owner; Godot orchestrates publication but no longer owns their background task implementation;
- `FluidSimulationRuntime` and `LightingRuntime` own drained batch lifecycle around those workers, including requeue on start failure, worker exception and stale completion; drained simulation/lighting work can no longer vanish because the adapter only logged an error;
- terrain and fluid mesh start/poll/stale-requeue/publication moved into dedicated Godot pipelines; both consume bounded meshlet batches and preserve pending work on worker failure; worker snapshots are meshlet-envelope-aware instead of blindly cloning a 3×3×3 chunk cube, and dependency stamps include absent-neighbor residency so newly loaded halo chunks invalidate stale results;
- block physics is capability-driven: `gravity` blocks use continuous falling state, while `support_below` blocks detach through the mutation pipeline when support disappears; voxel edits wake the changed cell and its dependent cell above through one deduplicated physics queue; resident-chunk physics seeding walks the chunk's sparse occupancy bitset instead of scanning all 4,096 voxels; falling state is bounded to 2,048 active blocks by default, saturation keeps additional blocks voxelized and queued instead of dropping work, and active simulation/presentation reuse bounded scratch storage instead of allocating per-frame snapshots;
- detached block state is portable across chunk palettes: falling blocks and block drops preserve orientation/state plus microblock masks by value, and re-intern chunk-local mask IDs only when materialized back into a chunk;
- broken/self-dropping and unsupported blocks spawn a bounded Core `DroppedBlockRuntime` with deterministic IDs, voxel collision, gravity, exact support wakeups, a 2,048-entity population cap and 300-second lifetime; Godot owns only their mesh/node presentation, while pickup waits for the future inventory owner;
- dropped blocks resolve drop↔drop overlap through a deterministic 3D broadphase and bounded contact passes; horizontal separation is swept against voxel collision so clustered drops no longer clip through one another or terrain;
- `BlockEntityFrameController` owns the Godot-side frame lifecycle for falling blocks and drops—tick-gated wake processing, Core runtime advancement and presentation sync—so `Main` no longer implements block-entity lifecycle mechanics; authoritative simulation remains in Core, and presentation consumes the runtimes' already ID-ordered streams without copying/resorting them every frame;
- player-centered chunk streaming with desired/retained residency, async deterministic QA materialization, prioritized load queues and visibility hysteresis; pending/presentation priority selection is allocation-free, while retired chunks use a deterministic bounded scan instead of sorting the entire retirement backlog on each eviction;
- `ChunkStreamingController` now coordinates selection/materialization/presentation/eviction frame phases around the Core residency owner, keeping streaming lifecycle mechanics out of the main Godot node;
- zero-copy in-memory session archive for edited chunks: eviction moves dirty chunks out of residency and restore happens before provider materialization; pristine deterministic chunks are dropped and regenerated instead of consuming archive memory;
- adaptive world-work budgets modeled after Mineclone: roughly 2 ms under frame pressure, 3 ms at normal cadence and 4 ms when frames are fast;
- 8³ chunk meshlets with dirty-halo remesh masks and revision-stale async work rejection; terrain/fluid pending mesh maps stay incrementally ordered by chunk coordinate, so bounded worker drains no longer sort/materialize the whole backlog on every dispatch; meshlet content revisions are stored as eight fixed slots per chunk, making chunk retirement O(1) instead of scanning every tracked meshlet;
- interactive voxel edits use an independent terrain fast lane: up to 8 interactive meshlets run on a dedicated worker and publish ahead of background terrain results, while the normal terrain worker remains capped at 16 meshlets; placement/destruction therefore no longer waits for an already-running streaming/remesh batch, and lighting refreshes still enqueue a later vertex-light remesh without invalidating a structurally current result;
- terrain render batches split by opaque/cutout/translucent + shadow policy, with collision geometry kept independent from visual surfaces;
- terrain/fluid mesh DTOs, culling, greedy geometry, AO/light sampling and mesh-data generation live in `Asteria.Core`; Godot only converts those engine-agnostic results into `ArrayMesh`, materials, collision shapes and nodes;
- greedy ordinary-cube meshing inside each 8³ meshlet for opaque/cutout faces when material, UV transform and vertex lighting are compatible; translucent faces stay independent for ordering;
- data-driven fluid registry plus chunk-local palette fluid storage independent from blocks;
- player fluid contact is sampled in Core from actual fluid surface height; passive immersion sinks while Space swims upward/helps exit near the surface;
- fluid movement physics is definition-owned: water currently authors a 0.45× horizontal speed multiplier with acceleration/drag, 0.9 passive sink speed, 2.4 swim ascent and 5.0 near-surface exit speed; future fluids can override the same contract without gameplay ID branches;
- fluid side/bottom faces remain back-face culled to avoid the broken transparent interior seen with a fully double-sided volume; exposed top surfaces alone receive reverse-wound underside triangles so the water surface stays visible from below;
- block and fluid occupancy are mutually exclusive: placing or settling a block replaces fluid in that voxel, fluid cannot enter a solid cell, breaking/removing a block immediately wakes fluid topology, and refill still follows authored fluid timing;
- a block mutation that displaces fluid publishes one shared consequence fan-out—terrain/fluid mesh halos, lighting, topology, physics and revisions are not double-enqueued or double-revisioned; boundary edits invalidate every loaded chunk whose one-voxel halo observes the change;
- incremental bounded fluid simulation with source cells, 8 visual levels, vertical falling columns and authored horizontal spread distance;
- fluid hot paths avoid per-evaluation candidate/surface-sample allocations: horizontal candidate ordering uses a fixed four-entry stack buffer, smoothed corner sampling is allocation-free, and topology work stays in an incrementally ordered set with cached chunk keys instead of sorting/recomputing the entire backlog every 40 Hz drain;
- fluid worker snapshots are bounded to the horizontal authored spread envelope plus only the seed chunk layer and its immediate vertical neighbors; unrelated vertical chunks are not cloned into worker jobs;
- fluid worker stale validation uses exact per-chunk residency stamps for that same 3D envelope, so newly loaded/unloaded dependencies invalidate the result while unrelated vertical residency does not;
- a 40 Hz engine-agnostic world tick clock drives authored fluid spreadSpeed; fluid ticks keep the earliest due time, round-robin equal-due work across chunks and become dormant while their chunk is unloaded;
- horizontal fluid spreading searches for the nearest reachable drop within the remaining authored range and prefers first-step directions that lead downhill, while falling columns reset their horizontal spread run;
- fluid meshlets are built and published independently from terrain meshlets, using smoothed corner heights, tiled per-face UVs and a dedicated translucent material path; fluid definitions may reference pack-relative textures, and the default water uses `textures/fluids/water.png`;
- fluid occupancy now participates in voxel lighting as a medium: authored fluid light dampening scales by fill level, direct sky and RGB propagation attenuate through fluid, and accepted fluid changes enqueue incremental relighting plus terrain/fluid vertex-light refreshes;
- block and fluid definitions can author RGB light emission; fluid emission scales with fill level, while equal-strength colored sources combine deterministically per RGB channel and propagate across chunk boundaries;
- block light dampening follows actual occupied geometry: layers and microblocks attenuate proportionally to occupied volume, and translucent/cutout behavior remains explicitly authored through `lightDampening` rather than inferred from render mode;
- incremental skylight treats unloaded vertical gaps as provisional open sky, matching initial chunk seeding; later residency changes enqueue reconciliation instead of allowing distant loaded chunks to incorrectly shadow across unknown space;
- accepted lighting worker results are integrated by a Core owner that copies authoritative light data and coalesces terrain/fluid mesh invalidation per affected meshlet before bumping revisions;
- voxel face/AO sampling is allocation-free in the repeated mesh-build path; static face bases and direct four-sample accumulation avoid per-vertex temporary arrays;
- incremental cross-chunk voxel lighting after edits: edited voxels seed a deduplicated propagation frontier instead of relighting every resident chunk;
- correct world ↔ chunk/local coordinate conversion across negative coordinates; repeated voxel-halo invalidation uses allocation-free integer ranges rather than allocating offset arrays on edit/remesh/lighting paths;
- cube, surface-layer, centered-layer, hollow and 8³ microblock geometry;
- independent block orientation, horizontal facing and texture rotation state;
- culled voxel mesh rendered as one `ArrayMesh`, not one node per block;
- one shared `Texture2DArray` terrain material with per-face base/overlay textures;
- nearest-neighbor pixel textures, tintable texture layers and fixed voxel face shading;
- FPS player with terrain collision;
- Godot used only as the client/rendering/platform adapter;
- HTML/CSS/TypeScript WebUI embedded over the game through Godot WRY;
- bidirectional JSON bridge between C# and the WebUI.

Visible terrain now comes from a temporary deterministic chunk provider used by the streaming runtime. The QA provider also places one water source above the origin terrain so falling/spreading fluid behavior and translucent fluid meshing are exercised without introducing world generation. It materializes QA chunks on demand around the player and is deliberately not a world-generation API. Chunks outside the desired radius are retained for a hysteresis/cache margin before authoritative residency is removed. Edited chunks are then archived in memory and restored before provider fallback, so break/place survives unload/reload during the current session. This is not save-game persistence and writes nothing to disk.

### Block content

Authorial block definitions in the built-in data pack live in `packs/default/data/blocks/*.json`. The current base catalog contains grass, dirt, stone, sand, gravel, clay, mud, snow, sand/snow layers and the initial oak-log variants.

Built-in block textures live under `packs/default/resources/textures/blocks/`. Definitions keep pack-relative logical paths such as `textures/blocks/stone.png`; the selected resource-pack root is resolved centrally. The initial textures were ported directly from the Mineclone `world-systems-rebuild` reference.

Built-in resource assets may have Godot-generated `.import` sidecars checked in under `packs/default/resources/` so editor import settings stay stable. Those files are internal development metadata only; external packs never require or distribute Godot `.import` files/resources; see [PACKS.md](PACKS.md).

The current startup selection is the unified `default` pack. One runtime `PackSelection` resolves `packs/default/data/`, `packs/default/resources/` and `packs/default/ui/`, so future import/selection can replace the complete visual/data package without changing individual loaders. The WebUI receives declarative theme tokens from `packs/default/ui/theme.json`.

The renderer builds one deterministic texture array from every texture referenced by the loaded block registry. A block face can currently use a base layer plus one alpha-composited overlay, matching the grass side/base-overlay setup.

### Requirements

- Godot 4.7.2 .NET
- .NET 8 SDK
- Node.js 22+
- Windows 10/11 for the first embedded WebUI target

Godot WRY 1.0.2 is pinned by the setup script. Its binaries are not committed to this repository.

### Setup WebUI

From CMD at the repository root:

```cmd
powershell -ExecutionPolicy Bypass -File scripts\install-webui.ps1
```

The script:

1. downloads Godot WRY 1.0.2 into `addons/godot_wry`;
2. installs the WebUI npm dependencies;
3. builds `ui/dist` for loading through `res://`.

Restart Godot after the first addon install if the editor was already open.

### Run

Open the repository root with the Godot 4.7.2 .NET editor and run the project.

Expected result:

- a deterministic streamed QA world around the player, rendered as 8³ meshlets;
- QA examples for thin sand/snow layers, oriented logs, hollow logs and a sculpted microblock;
- an FPS camera controlled by mouse + WASD, with Space to jump and Esc to release/capture the cursor;
- a centered gameplay crosshair and bottom-center nine-slot hotbar;
- slot 1 mirrors the currently selected placement block until the future inventory/hotbar owner replaces that temporary source;
- player vitals, status effects, contextual prompts and toast regions stay hidden until authoritative state is published;
- the old runtime card is now a debug overlay, hidden by default and toggled with F3 through Godot's input layer; WebUI only renders the resulting state.

### WebUI development

The WebUI uses framework-light HTML/CSS/TypeScript with **Atomic Design**:

```text
ui/src/components/
  atoms/
  molecules/
  organisms/
  templates/
  pages/
```

Godot/WRY transport lives in `ui/src/bridge/`, while message and interaction binding lives in `ui/src/controllers/`. Components stay transport-agnostic.

Run the embedded WebUI:

```bash
cd ui
npm install
npm run dev
```

Run Storybook for isolated component development:

```bash
npm run storybook
```

Build both production surfaces:

```bash
npm run build
npm run build-storybook
```

Every reusable component/page must have a colocated `*.stories.ts` file covering meaningful states. CI validates both the Vite bundle and Storybook. See `ui/README.md` for the full WebUI architecture contract.

The embedded build loads `res://ui/dist/index.html`. In debug/editor runs, `WebUiHost.gd` checks whether the bundle is missing or older than the WebUI source/config files and runs `npm --prefix ui run build` automatically before opening WRY. Release builds still require a prebuilt `ui/dist`.

## Bridge contract

Messages are JSON objects with this shape:

```json
{
  "type": "namespace.event",
  "payload": {}
}
```

Initial messages:

- `ui.ready` → WebUI loaded;
- `ui.ping` → WebUI bridge test;
- `game.ui_theme` → selected pack's validated declarative UI theme;
- `game.hud.debug` → Godot-owned debug-overlay visibility;
- `game.hud.hotbar` → current hotbar slots/selection;
- `game.hud.vitals` → authoritative health/stamina values when those systems exist;
- `game.hud.effects` → active buffs/debuffs;
- `game.hud.prompt` → contextual interaction prompt;
- `game.hud.toast` → bounded transient HUD notification;
- `game.ready` → Godot bridge ready;
- `game.pong` → Godot bridge response;
- `game.chunk_ready` → the streamed center presentation is resident and rendered;
- `game.player_ready` → FPS controller is active.

`src/Asteria.Godot/UI/WebUiHost.gd` is the only layer that knows about Godot WRY. Gameplay/core code must not depend on the browser implementation.

## Structure

```text
packs/
  default/
    data/                    built-in gameplay definitions
    resources/               built-in world/item presentation assets
    ui/                      declarative WebUI theme/assets
shaders/                    internal Godot renderer shaders
src/Asteria.Core/           engine-agnostic block/voxel/chunk runtime
src/Asteria.Core.Tests/     focused core runtime tests
src/Asteria.Godot/          Godot adapter and rendering
src/Asteria.Godot/UI/       WebUI embedding adapter
scenes/                     Godot scenes
ui/                         Atomic Design HTML/CSS/TypeScript WebUI + Storybook
scripts/                    local dependency/setup helpers
```
