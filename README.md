# Asteria

Fresh Godot + C# rewrite of Asteria.

## Current runtime foundation

The current milestone proves the base runtime architecture without introducing world generation yet:

- engine-agnostic voxel runtime in `Asteria.Core`;
- immutable, data-driven block definitions with namespaced IDs and compact runtime IDs;
- `VoxelCell` runtime state separated from block definitions;
- 16×16×16 chunks with palette-backed voxel storage;
- multi-chunk `VoxelWorld` runtime with world-space reads/writes across chunk boundaries;
- one authoritative voxel-mutation runtime for block edits: successful mutations invalidate terrain and fluid meshlets, enqueue incremental lighting, and wake local fluid topology together;
- player-centered chunk streaming with desired/retained residency, async deterministic QA materialization, prioritized load queues and visibility hysteresis;
- zero-copy in-memory session archive for edited chunks: eviction moves dirty chunks out of residency and restore happens before provider materialization; pristine deterministic chunks are dropped and regenerated instead of consuming archive memory;
- adaptive world-work budgets modeled after Mineclone: roughly 2 ms under frame pressure, 3 ms at normal cadence and 4 ms when frames are fast;
- 8³ chunk meshlets with dirty-halo remesh masks and revision-stale async work rejection;
- terrain render batches split by opaque/cutout/translucent + shadow policy, with collision geometry kept independent from visual surfaces;
- greedy ordinary-cube meshing inside each 8³ meshlet for opaque/cutout faces when material, UV transform and vertex lighting are compatible; translucent faces stay independent for ordering;
- data-driven fluid registry plus chunk-local palette fluid storage independent from blocks;
- incremental bounded fluid simulation with source cells, 8 visual levels, vertical falling columns and authored horizontal spread distance;
- a 40 Hz engine-agnostic world tick clock drives authored fluid spreadSpeed; fluid ticks keep the earliest due time, round-robin equal-due work across chunks and become dormant while their chunk is unloaded;
- horizontal fluid spreading searches for the nearest reachable drop within the remaining authored range and prefers first-step directions that lead downhill, while falling columns reset their horizontal spread run;
- fluid meshlets are built and published independently from terrain meshlets, using smoothed corner heights and a dedicated translucent material path;
- fluid occupancy now participates in voxel lighting as a medium: authored fluid light dampening scales by fill level, direct sky and RGB propagation attenuate through fluid, and accepted fluid changes enqueue incremental relighting plus terrain/fluid vertex-light refreshes;
- incremental cross-chunk voxel lighting after edits: edited voxels seed a deduplicated propagation frontier instead of relighting every resident chunk;
- correct world ↔ chunk/local coordinate conversion across negative coordinates;
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

Authorial block definitions live in `content/blocks/*.json`. The current base catalog contains grass, dirt, stone, sand, gravel, clay, mud, snow, sand/snow layers and the initial oak-log variants.

Block textures live under `textures/blocks/`. The initial textures were ported directly from the Mineclone `world-systems-rebuild` reference.

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
- a transparent WebUI card in the top-left;
- `bridge connected` after the JS ↔ Godot handshake;
- `chunk generated + collision ready` once the worker fixture finishes;
- clicking `Ping Godot` changes the last message to `pong received`.

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

The embedded build loads `res://ui/dist/index.html`.

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
- `game.ready` → Godot bridge ready;
- `game.pong` → Godot bridge response;
- `game.chunk_ready` → the streamed center presentation is resident and rendered;
- `game.player_ready` → FPS controller is active.

`src/Asteria.Godot/UI/WebUiHost.gd` is the only layer that knows about Godot WRY. Gameplay/core code must not depend on the browser implementation.

## Structure

```text
content/                    data-driven gameplay content
textures/                   authored/imported visual assets
shaders/                    Godot terrain/rendering shaders
src/Asteria.Core/           engine-agnostic block/voxel/chunk runtime
src/Asteria.Core.Tests/     focused core runtime tests
src/Asteria.Godot/          Godot adapter and rendering
src/Asteria.Godot/UI/       WebUI embedding adapter
scenes/                     Godot scenes
ui/                         Atomic Design HTML/CSS/TypeScript WebUI + Storybook
scripts/                    local dependency/setup helpers
```
