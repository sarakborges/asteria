# Asteria

Fresh technical spike for the Godot rewrite of Asteria.

## Test 1

The first milestone intentionally proves only the foundation:

- engine-agnostic voxel data in `Asteria.Core`;
- one 32×32×32 chunk generated off the Godot main thread;
- culled voxel mesh rendered as one `ArrayMesh`, not one node per block;
- Godot used only as the client/rendering adapter;
- WebUI isolated in `ui/`, with no gameplay UI built from Godot `Control` nodes.

### Requirements

- Godot 4.7.2 .NET
- .NET 8 SDK
- Node.js 22+ for the WebUI dev server

### Run the voxel spike

Open the repository root with the Godot 4.7.2 .NET editor and run the project.

Expected result: a small colored voxel terrain chunk with an orbiting camera. The chunk data is produced on a worker task; the Godot mesh is created on the main thread after generation completes.

### Run the WebUI spike

```bash
cd ui
npm install
npm run dev
```

For now the WebUI runs independently in the browser. The future embedded browser/CEF implementation will live behind a Godot-side adapter so the UI code does not depend on the embedding technology.

## Structure

```text
src/Asteria.Core/      engine-agnostic world data and generation
src/Asteria.Godot/     Godot adapter and rendering
scenes/                Godot scenes
ui/                    HTML/CSS/TypeScript WebUI
```
