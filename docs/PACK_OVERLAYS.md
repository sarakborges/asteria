# Pack overlays (Asteria)

Asteria's authored content is the low-priority base layer. An external pack may contribute data, resources, or both. Higher-priority packs override lower-priority packs. Definition composition is by **namespaced ID**, not by JSON filename.

## Definition overrides

Put a JSON file in the same `data/<category>/` category as the definition being changed. A partial object merges with the inherited definition, recursively for objects. Arrays replace the complete inherited array.

```json
{ "id": "asteria:dirt", "enabled": false }
```

The example *removes* `asteria:dirt` from the final block registry. References in other enabled definitions must still validate; a pack disabling foundational content may make a world unstartable. This is a content-validation error, not an instruction to rewrite references automatically.

An enabled patch can override a single field:

```json
{ "id": "asteria:dirt", "mining": { "hardness": 3 } }
```

Omitting `enabled` is equivalent to `true`. Duplicate IDs within a single pack/category are invalid. Files are processed in ordinal path order and pack priority is explicit. A higher layer may reintroduce an ID removed by a previous layer, but it must provide a complete definition.

This document specifies the composition contract, not unrestricted execution. Code mods have a separate trust and loading boundary.
