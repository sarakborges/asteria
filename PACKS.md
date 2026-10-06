# Asteria Packs

This document defines the authored contract for Asteria content packs.

A pack is one selectable unit. It owns data plus presentation customizations while preserving strict domain boundaries.

## 1. Canonical layout

Every pack lives under one root:

```text
packs/
  default/
    pack.json
    data/
      blocks/
      fluids/
      biomes/
      structures/
      recipes/
      loot/
      dimensions/
    resources/
      textures/
      audio/
      fonts/
      models/
    ui/
      theme.json
      assets/
```

The built-in startup pack is `packs/default/`.

Runtime selection is represented by one `PackSelection` value. The initial value is `default`; future import/selection UI may change it without changing individual content loaders.

## 2. Non-negotiable engine boundary

The authored format belongs to Asteria, not Godot.

External packs must never require:

- Godot `.import` sidecars;
- `.godot/` generated content;
- Godot UID/import cache files;
- `.tres`, `.res` or `.tscn` as authoritative pack data;
- editor-specific absolute paths;
- generated runtime caches.

Built-in assets may keep Godot-generated `.import` files beside files in `packs/default/resources/` so editor import settings remain stable. Those sidecars are internal repository metadata and are never part of the external pack API.

## 3. Manifest

Each pack has one manifest at `packs/{name}/pack.json`:

```json
{
  "format": 1,
  "id": "example:my_pack",
  "version": "1.0.0",
  "name": "My Pack",
  "description": "Optional description",
  "dependencies": []
}
```

- `format` is the Asteria pack-format version.
- `id` is a stable namespaced identifier.
- `version` is the authored pack version.
- `dependencies` declares pack dependencies/version requirements.
- unsupported mandatory format versions fail clearly.

## 4. Data domain

`data/` owns declarative gameplay/content definitions.

Definitions may add or override namespaced blocks, fluids, biomes, structures, recipes, loot, dimensions and future definition-driven systems.

Dimensions live under `data/dimensions/*.json`. A dimension is one authored world-runtime configuration and declares its stable ID, explicit biome pool, sea level, gravity strength, spawn coordinates and engine-agnostic environment presentation values. Surface biomes author `baseHeightOffset` relative to that dimension sea level rather than baking an absolute world height into each biome. A root world seed is not duplicated into the pack; runtime derives a stable per-dimension seed from the world seed + dimension ID.

Data must not contain executable gameplay code. Native/code plugins are a separate future extension system.

Data definitions may reference presentation resources by logical pack-relative keys, but must not embed Godot-specific metadata.

## 5. Resource domain

`resources/` owns non-UI presentation assets: textures, item/world images, audio, fonts, models and related declarative presentation descriptors.

Definitions reference these by logical paths such as:

```text
textures/blocks/stone.png
```

The selected pack resolver maps that to:

```text
packs/{selected}/resources/textures/blocks/stone.png
```

## 6. UI domain

`ui/` owns UI presentation customization.

The initial supported contract is `ui/theme.json`, a declarative map of whitelisted semantic design tokens. The WebUI receives the selected theme through the existing Godot → WebUI bridge and applies only recognized CSS variables.

Current theme token categories include colors, borders, radii, spacing and typography. UI-specific images/fonts may later live under `ui/assets/` and use the same validated pack-relative resolution.

A pack **does not** replace the trusted WebUI application code. It must not inject arbitrary HTML or JavaScript, redefine IPC messages, own controllers, or bypass Atomic Design/bridge boundaries.

The built-in WebUI implementation remains under `ui/`; `packs/{name}/ui/` only supplies presentation data/assets.

## 7. Layering

Built-in/default content is the base layer.

When overlays are added, resolution order is deterministic:

1. base/default pack;
2. enabled overlays in explicit configured order;
3. later layers replace earlier content only through canonical keys.

Never use filesystem enumeration order, archive entry order, hash-map order or timestamps to decide precedence.

## 8. Names and paths

Pack folder names use lowercase ASCII letters, digits, `.`, `_` or `-`.

Pack paths are relative. Absolute paths, `..`, empty segments, backslashes and URI/scheme separators are invalid.

Path comparison/collision behavior is defined by Asteria and must be consistent across Windows/Linux/macOS.

## 9. Runtime boundary

Loaders validate the selected pack root, parse portable source data and produce resolved engine-agnostic/runtime presentation inputs.

Godot may consume resolved resource paths to create engine objects, but it does not own pack schema or gameplay definitions.

The WebUI consumes validated UI theme payloads through its controller boundary; individual components do not parse pack files.

## 10. Caching and compatibility

Derived caches are disposable, regenerate from authored pack contents and are versioned separately from the pack format.

While Asteria is pre-release, new pack-format versions do not promise backward compatibility unless explicitly required.

## 11. Security

Pack files are untrusted input. Loaders must enforce or evolve toward:

- bounded files/archive sizes;
- bounded decoded asset dimensions;
- path traversal rejection;
- duplicate/collision detection;
- schema validation;
- dependency-cycle detection;
- deterministic failure reporting.

Keeping data and UI customization declarative prevents pack installation from implicitly granting code execution.
