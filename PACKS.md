# Asteria Packs

This document defines the authored contract for external **resource packs** and **data packs**.

The format belongs to Asteria. It is deliberately independent from Godot so packs remain portable across runtime/rendering changes.

## 1. Non-negotiable boundary

An authored pack must contain only portable source assets/data understood by Asteria.

The following are **not pack formats** and must never be required inside a distributed pack:

- Godot `.import` sidecars;
- `.godot/` generated content;
- Godot UID/import cache files;
- `.tres`, `.res` or `.tscn` as authoritative pack data;
- editor-specific absolute paths;
- generated runtime caches.

Repository-local built-in assets are different: their `.import` files may be versioned because they preserve editor import settings. Those sidecars remain next to assets under the selected built-in resource directory and are an implementation detail of the Godot project, not part of the external pack API.

## 2. Common manifest

Every pack has one Asteria-owned manifest at its root:

```json
{
  "format": 1,
  "id": "example:my_pack",
  "type": "resource",
  "version": "1.0.0",
  "name": "My Pack",
  "description": "Optional human-readable description",
  "dependencies": []
}
```

Initial contract:

- `format` is the Asteria pack-format version.
- `id` is a stable namespaced identifier.
- `type` is `resource` or `data`.
- `version` is the authored pack version.
- `dependencies` declares other pack IDs/version requirements when needed.
- unknown mandatory format versions must fail clearly instead of being partially loaded.

A future implementation may extend manifest fields without changing the engine-independence rule.

## 3. Resource packs

Resource packs own **presentation**, not gameplay authority.

Typical authored resource-pack root:

```text
pack.json
textures/
  blocks/
  items/
  ui/
audio/
fonts/
models/
presentation/
```

The built-in initial resource pack is stored at `resources/default/`. Its pack-relative references remain logical, for example `textures/blocks/stone.png`; the selected resource-pack root is prepended by the runtime resolver.

Initial texture sources should use portable image files such as PNG. Model/audio formats, when added, must likewise use portable documented source formats rather than engine-native serialized resources.

Resource packs may replace resources by canonical namespaced/path keys. They do not mutate block physics, crafting rules, biome selection, drops, fluid behavior or other gameplay facts.

Presentation metadata must use Asteria-owned declarative files, not Godot resources.

## 4. Data packs

Data packs own **declarative gameplay/content definitions**.

Expected data-pack root as those systems exist:

```text
pack.json
blocks/
fluids/
biomes/
structures/
recipes/
loot/
dimensions/
...
```

The built-in initial data pack is stored at `data/default/`.

Data packs may add or override namespaced definitions through the same definition models/registries used by built-in content.

Data packs must not contain executable gameplay code. Native/code plugins are a separate future extension system with separate trust/security/versioning rules.

A data definition may reference a presentation resource by namespaced resource key, but it must not embed or require Godot-specific asset metadata.

## 5. Base content and layering

Built-in Asteria content is conceptually the **base layer**. The startup selection is `resource=default` and `data=default`; this selection is a runtime value rather than a path hard-coded into individual loaders, so a future importer/pack UI can replace either side.

Runtime resolution order is deterministic:

1. built-in/base content;
2. enabled packs in their explicit configured order;
3. later packs override earlier packs only for the same canonical key.

Never use filesystem enumeration order, archive entry order, hash-map order or modification timestamps to decide precedence.

Conflicting definitions in the same effective layer are errors unless the format explicitly defines a merge strategy for that content type.

## 6. Namespaces and paths

Pack IDs and authored content IDs use namespaced identifiers such as:

```text
asteria:grass
example:blue_grass
example:misty_swamp
```

Rules:

- IDs are normalized and validated before registry insertion.
- pack paths are relative; `..` traversal and absolute paths are invalid.
- path comparison/collision behavior is defined by Asteria and must behave consistently on Windows/Linux/macOS.
- duplicate logical keys that differ only by unsupported casing/path normalization are rejected.
- authored references are resolved after layering so overrides are visible consistently.

## 7. Runtime/import boundary

The external pack loader should produce engine-agnostic resolved content first.

For resource packs:

1. validate manifest/paths;
2. resolve deterministic layering;
3. decode portable source assets;
4. build Asteria presentation descriptors/runtime asset data;
5. let the Godot adapter create `ImageTexture`, texture arrays, audio resources or other engine objects as needed.

For data packs:

1. validate manifest/paths;
2. parse declarative definitions;
3. resolve layering and references deterministically;
4. validate the final definition graph;
5. build immutable registries/runtime definitions.

Godot must not become the parser, authority or schema owner for either pack type.

## 8. Caching

Asteria may later cache decoded/resolved pack data for startup performance.

Caches are:

- derived;
- disposable;
- versioned separately from authored pack format;
- safe to regenerate from the original pack;
- never required from pack authors.

Godot's `.import` mechanism is not the Asteria pack cache contract.

## 9. Compatibility policy

While Asteria is pre-release, new pack-format versions do not promise backward compatibility unless explicitly required.

When compatibility becomes a product requirement, migration/version handling must be explicit and tested rather than inferred.

## 10. Security boundary

Pack files are untrusted input.

Loaders must eventually enforce:

- bounded file/archive sizes;
- bounded decoded image/model dimensions;
- path traversal rejection;
- duplicate/collision detection;
- schema validation;
- dependency-cycle detection;
- deterministic failure reporting.

Data packs remain declarative specifically so loading content does not implicitly grant code execution.
