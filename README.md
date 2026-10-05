# Asteria

Fresh Godot + C# rewrite of Asteria.

## Current runtime foundation

The current milestone proves the base runtime architecture without introducing world generation yet:

- engine-agnostic voxel runtime in `Asteria.Core`;
- immutable block definitions with namespaced IDs and compact runtime IDs;
- `VoxelCell` runtime state separated from block definitions;
- 16×16×16 chunks with palette-backed voxel storage;
- correct world ↔ chunk/local coordinate conversion across negative coordinates;
- culled voxel mesh rendered as one `ArrayMesh`, not one node per block;
- FPS player with terrain collision;
- Godot used only as the client/rendering/platform adapter;
- HTML/CSS/TypeScript WebUI embedded over the game through Godot WRY;
- bidirectional JSON bridge between C# and the WebUI.

The visible terrain is a temporary `TestChunkFactory` QA fixture. It is deliberately not a world-generation API.

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

- one 16³ palette-backed colored voxel test chunk;
- an FPS camera controlled by mouse + WASD, with Space to jump and Esc to release/capture the cursor;
- a transparent WebUI card in the top-left;
- `bridge connected` after the JS ↔ Godot handshake;
- `chunk generated + collision ready` once the worker fixture finishes;
- clicking `Ping Godot` changes the last message to `pong received`.

### WebUI development

```bash
cd ui
npm install
npm run dev
```

The embedded build loads `res://ui/dist/index.html`; rebuild with `npm run build` after WebUI changes.

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
- `game.chunk_ready` → test chunk fixture is resident and rendered;
- `game.player_ready` → FPS controller is active.

`src/Asteria.Godot/UI/WebUiHost.gd` is the only layer that knows about Godot WRY. Gameplay/core code must not depend on the browser implementation.

## Structure

```text
src/Asteria.Core/          engine-agnostic block/voxel/chunk runtime
src/Asteria.Core.Tests/    focused core runtime tests
src/Asteria.Godot/         Godot adapter and rendering
src/Asteria.Godot/UI/      WebUI embedding adapter
scenes/                    Godot scenes
ui/                        HTML/CSS/TypeScript WebUI
scripts/                   local dependency/setup helpers
```
