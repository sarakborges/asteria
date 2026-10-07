# Asteria

Fresh Godot + C# rewrite of Asteria.

## Architecture canon

Repository changes must follow [AGENTS.md](AGENTS.md), [ARCHITECTURE.md](ARCHITECTURE.md), and [ENGINEERING_PRACTICES.md](ENGINEERING_PRACTICES.md). These documents are jointly normative for implementation and refactoring. External content packaging follows [PACKS.md](PACKS.md).

## Current runtime foundation

The current milestone includes the rebuilt deterministic world-generation/runtime foundation:

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
- player-centered chunk streaming with desired/retained residency, async provider-driven materialization, prioritized load queues and visibility hysteresis; production materialization uses the data-driven biome world generator through an injected Core `IChunkProvider`, while Core surface-range sampling selects the vertical chunk span required by the actual authored terrain plus a bounded player-local vertical band; pending/presentation priority selection is allocation-free and retired chunks use a deterministic bounded scan;
- `ChunkStreamingController` now coordinates selection/materialization/presentation/eviction frame phases around the Core residency owner, keeping streaming lifecycle mechanics out of the main Godot node;
- zero-copy in-memory session archive for edited chunks: eviction moves dirty chunks out of residency and restore happens before provider materialization; pristine deterministic chunks are dropped and regenerated instead of consuming archive memory;
- adaptive world-work budgets modeled after Mineclone: roughly 2 ms under frame pressure, 3 ms at normal cadence and 4 ms when frames are fast;
- 8³ chunk meshlets with dirty-halo remesh masks and revision-stale async work rejection; terrain/fluid pending mesh maps stay incrementally ordered by chunk coordinate, so bounded worker drains no longer sort/materialize the whole backlog on every dispatch; meshlet content revisions are stored as eight fixed slots per chunk, making chunk retirement O(1) instead of scanning every tracked meshlet;
- interactive voxel edits use an independent terrain fast lane: up to 8 interactive meshlets run on a dedicated worker and publish ahead of background terrain results, while the normal terrain worker remains capped at 16 meshlets; placement/destruction therefore no longer waits for an already-running streaming/remesh batch, and lighting refreshes still enqueue a later vertex-light remesh without invalidating a structurally current result;
- terrain render batches split by opaque/cutout/translucent + shadow policy, with collision geometry kept independent from visual surfaces; non-collidable `crossedSprite` block visuals share the same meshlet batches, texture array, tint and voxel-light pipeline without creating Godot nodes per plant;
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

Visible terrain now comes from the Core `BiomeWorldGenerator`, selected through an authored `DimensionDefinition` and injected into the streaming residency runtime. `packs/default/data/dimensions/` currently contains `asteria:overworld` and `asteria:umbral`. Each dimension explicitly selects separate surface/volume/underground biome pools, derives an independent seed from the root world seed, and owns gravity, spawn coordinates and environment colors/fog.

Overworld actively authors plains, swamp, wasteland, desert, alps, arctic, gorge, mountain belt, mountains and volcano. The Umbral authors Umbral Reach, Withered Waste and Wraith Grove, with its own dark environment/fog presentation. Enchanted Forest remains cataloged but is intentionally excluded from the Overworld pool; Wraith Grove is the Umbral-side replacement. Swamp and desert currently declare `cannotBorder`; height is blended across biome influences so primary-biome changes do not cut the terrain vertically. Surface material ownership remains primary-biome driven, while authored layer patches use continuous deterministic world-space noise with organic boundaries that cross chunk edges without circular footprints.

Ground vegetation now comes from biome decorators rather than the QA fixture: plains/swamp can place grass and brown mushrooms are authored only by swamp. Generation is deterministic for the same seed regardless of chunk/task order. The old `DeterministicChunkProvider` remains only for test/QA fixtures.

Generated surface structures use a separate Core placement/conflict owner and the same single chunk materializer. The default pack ports MineClone's small/medium/big/huge boulders, four oak-tree block variants, three willow-tree block variants, plus the connected lake/river/mountain-pond/mountain-waterfall graph. Willow keeps its authored required water proximity (1..12 blocks) through a bounded structure restriction that queries existing generated field fluid; plains and swamp use MineClone's willow spacing/chance/jitter roots. Placement is deterministic, footprint/ground-fit aware, supports min/max slope, priority/conflict groups, StructureSets, connector strength/loss, source-fluid and clear payloads, `groundAnchorY`/`clearAbove`, cross-chunk bounds, biome-margin roots, and authoritative area/nearest queries. The connected-water content is implemented entirely through generic Structures/connectors; no hydrology subsystem exists. Stick object attachments and willow moss surface layers remain deferred. Swamp now restores MineClone's authored generated water puddles, so willow water proximity can resolve against local swamp water as well as other generated-water owners.

Chunks outside the desired radius are retained for a hysteresis/cache margin before authoritative residency is removed. Every materialized chunk is archived in memory on residency/session retirement, including pristine and intentionally empty chunks, and restored before provider fallback. Dirty tracking remains separate mutation metadata. This is current-session spatial persistence, not disk save-game persistence. Hydrology is intentionally prohibited by the architecture; Sphere-wide caves, biome-authored additive 3D terrain and explicit generated-ocean fill extend the existing Core generation owners without introducing a generalized water-generation layer.

### Worldgen diagnostics

`tools/WorldgenBenchmark` records deterministic cold/warm query timings, allocations and content digests, including biome/terrain queries, search, destination preparation, chunk synthesis and the initial radius-2 playable area. The canonical CI fixture enforces evidence-based regression budgets.

`tools/BiomeMap` samples the authoritative `BiomeField` and emits deterministic SVG + legend diagnostics. CI publishes fixed-seed Overworld and Umbral maps for visual boundary/junction review; the tool is a query consumer and owns no generation semantics.

### Dimension and biome content

Authorial dimensions live in `packs/default/data/dimensions/*.json`. Their current schema owns explicit `surfaceBiomes`, `volumeBiomes` and `undergroundBiomes` pools, optional `generatedSurfaceStructures` and `generatedSurfaceFluids`, `seaLevel`, `gravityStrength`, spawn X/Z and environment background/ambient/fog values. The root world seed derives a distinct seed for each dimension.

Biome definitions live in `packs/default/data/biomes/*.json`. Surface biomes author `surfaceLayout` + `surfaceTerrain`; volume biomes author `volumeLayout` plus bounded `terrain3d`; underground biomes author `undergroundLayout`. Surface/volume biomes may use ordered `surfaceLayers` as the exposed-solid material profile, while an underground-only biome may currently be identity-only. Generation lives entirely in Core; Godot loads the selected pack, selects the startup dimension and composes its provider/presentation adapters.

The default Overworld surface pool selects plains, swamp, wasteland, desert, alps, arctic, ocean, gorge, mountain belt, mountains and volcano; Enchanted Forest remains defined but inactive by design. Floating Islands is now volume-only: it cannot own the ground biome, but its `volumeLayout` may own a bounded X/Z region inside Y 200..280 where `terrain3d.floatingFormation` contributes additive density and its own grass/dirt/stone material profile. Surface terrain uses Mineclone's authored `baseHeightOffset`/noise amplitudes over the dimension `seaLevel`. Ocean uses an explicit dimension-authored `generatedOcean` rule that fills only empty voxels above the base ocean floor through sea level with normal source fluid cells. The same `GeneratedFluidField` now also owns bounded local surface pools: swamp water puddles and volcano lava pools deterministically carve a shallow terrain cut and fill that exact cut with authored source fluid. Its authored shore profile raises the deep floor into a shallow shelf and then a dry sand beach while `ocean` is still the primary biome, preventing disconnected/floating sea-level water at terrain transitions. Runtime fluid simulation takes over after residency; stable generated sources are initially scheduled only where they can spread. Mineclone's `caverns` entry is now represented as the Overworld's first underground-only biome. The Sphere-wide subtractive cave field still owns cave geometry; `caverns` supplies identity only where that field actually carved a void. It does not replace solid-rock material yet because MineClone does not author a distinct caverns material contract. None of this introduces a hydrology subsystem.

`Main.StartupDimensionId` defaults to `asteria:overworld` and is an exported startup setting, so the runtime can be launched directly in `asteria:umbral` without changing generation code. Live dimension transition now retires the current `DimensionRuntimeSession` and creates/restores the destination session instead of clearing a shared world. Dirty chunk edits, world tick, player position, falling blocks and dropped blocks remain dimension-scoped across round trips. Pristine materialized chunks are archived and restored exactly on return; only never-materialized coordinates remain generator-owned.

For QA, F4 cycles authored dimensions in canonical ID order and uses the current exact player coordinates as the destination. Gameplay/global input still belongs to Godot; WebUI does not capture this key. Future portals call the same transition boundary with their authored destination coordinates.

### Block content

Authorial block definitions in the built-in data pack live in `packs/default/data/blocks/*.json`. All 63 block definitions from Mineclone `world-systems-rebuild/data/blocks` are now represented in the default Asteria pack with their corresponding block/tree/storage-box textures: terrain materials, stone/basalt/ice/mud/sandstone/snow construction variants, glass, lamp, terracotta, storage box, oak/willow/enchanted/world-tree logs (including stripped/hollow variants), planks and leaves. Mineclone-only fields whose owning systems do not exist in Asteria are intentionally not imported; supported material, render, lighting, orientation, shape and mining contracts are preserved.

Asteria additionally represents Mineclone's ground grass and all seven colored mushrooms as normal non-collidable `crossedSprite` block content, using the original Mineclone object textures. They use top-face placement and `support_below`; losing support pops them through the existing block-physics lifecycle. Grass does not self-drop yet, while mushrooms do.

Built-in block textures live under `packs/default/resources/textures/blocks/`. Definitions keep pack-relative logical paths such as `textures/blocks/stone.png`; the selected resource-pack root is resolved centrally. The initial textures were ported directly from the Mineclone `world-systems-rebuild` reference.

Pack-owned resource assets are raw Asteria files, not Godot resources. `packs/.gdignore` keeps the pack tree outside Godot's import scanner; runtime loaders decode pack files directly and CI rejects Godot metadata/resources such as `.import`, `.uid`, `.tres`, `.res`, `.tscn` or `.godot/` inside packs. See [PACKS.md](PACKS.md).

The current startup selection is the unified `default` pack. One runtime `PackSelection` resolves `packs/default/data/`, `packs/default/resources/` and `packs/default/ui/`, so future import/selection can replace the complete visual/data package without changing individual loaders. The WebUI receives declarative theme tokens from `packs/default/ui/theme.json`.

The renderer builds one deterministic texture array from every face texture and crossed-sprite texture referenced by the loaded block registry. A block face can currently use a base layer plus one alpha-composited overlay, while crossed sprites use one cutout layer and may opt into authored tint.

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

- a deterministic streamed authored dimension around the player, rendered as 8³ meshlets;
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
- `game.player_ready` → FPS controller is active;
- `game.loading` → authoritative loading phase + completed/total counters from `WorldLoadingState`; WebUI renders this state and owns no loading timer.

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
