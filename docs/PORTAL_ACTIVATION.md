# Dimensional Slicer portal activation

## One portal identity, per-voxel metadata

The world has **one** authored internal `asteria:portal` block (a noncolliding centered Layer-shape voxel). Every portal cell stores its link as immutable, bounded `BlockSurfaceState.MetaTags["target_dimension"]`, for example `asteria:umbral` or `asteria:overworld`. All links use the same block and texture. Destination is **never** inferred from a special `portal_to_*` block ID.

The Dimensional Slicer stores `target_dimension` on the held item's metadata. Native right-click on a closed Glass frame with `is_portal_frame` and matching `accept_dimension:<id>` accepts exactly one bounded planar enclosed region (three orientations), verifies resident empty/no-fluid cells, and atomically writes the portal cells and per-cell metadata via `VoxelMutationRuntime.ApplyStructureChanges`. The item is consumed only on success in Survival.

`PortalActivationRuntime.TryGetDestinationAt` reads the portal block's voxel metadata. Glass removal retracts only its connected, same-target, same-orientation portal region. All mutation/lighting/meshing and chunk-state revisions remain with the canonical voxel owner.

## Persistence

Voxel `MetaTags` are in existing sparse `BlockSurfaceState` and included in `BlockStateSnapshot`, chunk snapshots (format 2; format 1 still readable), and portable block state (gameplay session format 5; formats 1–4 still readable). Tags are limited to eight pairs with bounded keys/values and sorted for deterministic encoding. Existing blocks without tags remain untouched.

The portal image resides in the selected pack at `resources/textures/blocks/portals/umbral.png`. No new scroll, per-block Godot nodes, independent world state or browser-level gameplay input.

## Remaining

Destination carving and authored connected arrival structures are not yet implemented. If exact X/Y/Z arrival is obstructed, travel safely rolls back rather than substituting another coordinate. In-engine rendering, triggering, frame-boundary and save/reload verification remain pending.
