# Held dynamic RGB lighting

Ported from MineClone `world-systems-rebuild/src/rendering/dynamic_lights.rs`; native Godot shadow occlusion updated 2026-10-13.

- **Source:** `PlayerInventory.SelectedStack`, validated against authored `BlockDefinition.LightEmission` by the existing Core `HeldLightResolver` (RGB levels 0–15). Other stacks are dark.
- **Single emitter:** `HeldDynamicLightPresentation` follows the camera's hand position with one bounded, shadow-casting `OmniLight3D`. Its emission updates on inventory revision; Godot updates shadow visibility when its position changes.
- **Terrain:** six terrain shader variants preserve the Core-authored RGB/sky/AO/face-shading output as `EMISSION`. The shared `held_shadow_light.gdshaderinc` uses a custom `light()` callback, consuming Godot's real shadow-map `ATTENUATION` so a solid wall blocks direct held light. Directional sun light is excluded from the transient contribution.
- **No duplicated gameplay state:** moving the light does not change persisted chunk light, world snapshots, revisions, meshlets, queues or Core worker scheduling. The old unoccluded distance shader and terrain material uniforms are removed.
- **Lifetime:** deselection/death, missing camera, exit and Sphere retirement hide the emitter; the next valid selected stack reactivates it.
- **Boundary:** this is directly shadowed GPU light, not a transient flood-filled voxel RGB solution or bounced light. Transparent surfaces and materials that explicitly disable receiving shadows have rendering limitations. Verify in a graphical Godot/WRY run and profile densely populated terrain; headless native smoke checks shader resource loads, not pixels.
