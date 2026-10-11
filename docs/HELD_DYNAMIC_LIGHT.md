# Held dynamic RGB lighting

Ported from MineClone `world-systems-rebuild/src/rendering/dynamic_lights.rs` with an Asteria-specific split between authored emission (Core) and transient display (Godot).

- **Authoritative source:** `PlayerInventory.SelectedStack`, resolved by `HeldLightResolver` against the registered `BlockDefinition.LightEmission` RGB channels (0–15). Portable mismatched IDs/states and non-block stacks are dark. No new pack property or hard-coded block ID.
- **One bounded presentation:** `HeldDynamicLightPresentation` observes inventory revision changes, follows the Godot camera with a local hand offset, uses one shadow-free `OmniLight3D` for non-terrain receivers and updates the existing `VoxelTerrainMaterialSet` uniforms for unshaded voxel terrain.
- **Terrain:** the six active terrain shaders share `shaders/held_dynamic_light.gdshaderinc`, combine distance-attenuated RGB with authored voxel light, and keep existing AO/face shading. The inactive case immediately returns dark, with no per-pixel distance calculation.
- **Lifetime:** disabling the selected block, opening a non-world scene, death, missing camera, dimension retirement, or save-and-leave clears presentation. Position uniforms change only when the camera moves; emission updates only when the selected inventory revision changes.
- **No second simulation:** this is a transient render light. It never modifies voxel light, invalidates meshlets, dispatches lighting workers, alters chunk revisions, or touches saved state, so no async result can resurrect a stale moving source.
- **Important limit:** the render-only light is not occluded by solid voxels. Physically blocked, transient voxel-RGB lighting requires a later explicit Core lighting extension with its own revision/worker integration; it is **not** claimed here.
- **QA:** Godot/WRY native playtest required for emissive and nonemissive hotbar swaps, RGB correctness, changing facing/moving across chunk boundaries, first/third-person camera, death, Sphere transition, and save/reload. A C# build does not validate Godot shader compilation/rendering.

Engineers must not replace this with gameplay-state duplication or mutate permanent voxel lighting merely to move the held source.
