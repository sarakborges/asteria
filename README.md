# Asteria

Fresh technical spike for the Godot rewrite of Asteria.

## Current spike

The current milestone proves the base architecture:

- engine-agnostic voxel data in `Asteria.Core`;
- one 32×32×32 chunk generated off the Godot main thread;
- culled voxel mesh rendered as one `ArrayMesh`, not one node per block;
- Godot used only as the client/rendering/platform adapter;
- HTML/CSS/TypeScript WebUI embedded over the game through Godot WRY;
- bidirectional JSON bridge between C# and the WebUI.

### Requirements

- Godot 4.7.2 .NET
- .NET 8 SDK
- Node.js 22+
- Windows 10/11 for the first embedded WebUI target

Godot WRY 1.0.2 is pinned by the setup script. Its binaries are not committed to this repository.

### Setup WebUI

From PowerShell at the repository root:

```powershell
./scripts/install-webui.ps1
```

The script:

1. downloads Godot WRY 1.0.2 into `addons/godot_wry`;
2. installs the WebUI npm dependencies;
3. builds `ui/dist` for loading through `res://`.

Restart Godot after the first addon install if the editor was already open.

### Run

Open the repository root with the Godot 4.7.2 .NET editor and run the project.

Expected result:

- a small colored voxel terrain chunk with an orbiting camera;
- a transparent WebUI card in the top-left;
- `bridge connected` after the JS ↔ Godot handshake;
- `chunk generated` once the worker finishes;
- clicking `Ping Godot` changes the last message to `pong received`.

### WebUI development

```bash
cd ui
npm install
npm run dev
```

The production-style embedded test loads `res://ui/dist/index.html`; rebuild with `npm run build` after WebUI changes.

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
- `game.chunk_ready` → first chunk finished generation.

`src/Asteria.Godot/UI/WebUiHost.gd` is the only layer that knows about Godot WRY. Gameplay/core code must not depend on the browser implementation.

## Structure

```text
src/Asteria.Core/          engine-agnostic world data and generation
src/Asteria.Godot/         Godot adapter and rendering
src/Asteria.Godot/UI/      WebUI embedding adapter
scenes/                    Godot scenes
ui/                        HTML/CSS/TypeScript WebUI
scripts/                   local dependency/setup helpers
```
