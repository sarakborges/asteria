# Dimensional Slicer portal activation

## One portal identity, per-voxel metadata

The world has **one** authored internal `asteria:portal` block (a noncolliding centered Layer-shape voxel). Every portal cell stores its link as immutable, bounded `BlockSurfaceState.MetaTags["target_dimension"]`, for example `asteria:umbral` or `asteria:overworld`. All links use the same block and texture. Destination is **never** inferred from a special `portal_to_*` block ID.

The Dimensional Slicer stores `target_dimension` on the held item's metadata. Native right-click on a closed Glass frame with `is_portal_frame` and matching `accept_dimension:<id>` accepts exactly one bounded planar enclosed region (three orientations), verifies resident empty/no-fluid cells, and atomically writes the portal cells and per-cell metadata via `VoxelMutationRuntime.ApplyStructureChanges`. The item is consumed only on success in Survival.

`PortalActivationRuntime.TryGetDestinationAt` reads the portal block's voxel metadata. Glass removal retracts only its connected, same-target, same-orientation portal region. All mutation/lighting/meshing and chunk-state revisions remain with the canonical voxel owner.

## Persistence

Voxel `MetaTags` are in existing sparse `BlockSurfaceState` and included in `BlockStateSnapshot`, chunk snapshots (format 2; format 1 still readable), and portable block state (gameplay session format 5; formats 1–4 still readable). Tags are limited to eight pairs with bounded keys/values and sorted for deterministic encoding. Existing blocks without tags remain untouched.

The portal image resides in the selected pack at `resources/textures/blocks/portals/umbral.png`. No new scroll, per-block Godot nodes, independent world state or browser-level gameplay input.

## Exact-coordinate arrival

Each Sphere may author `portalArrivalStructure` in its dimension JSON. It references exactly one ordinary Structure definition (not a StructureSet); the default Overworld and Umbral both reference `asteria:portal_arrival_platform`. The root's authoring anchor is positioned at the portal's exact destination voxel X/Y/Z without surface-height substitution. The existing Structure field expands compatible connector links and validates payload bounds, generated-structure conflicts and previously committed manual footprints. The existing manual Structure transaction validates residency, protected blocks and replacement restrictions, then publishes atomically through `VoxelMutationRuntime` and records its footprint in the per-Sphere journal.

This is attempted only if the exact destination is unsafe, after initial chunk residency and presentation are ready. The result is checked against authoritative resident voxels again; failure retains the existing safe rollback rather than placing the player in solid/fluid terrain or moving to unrelated coordinates. Successful pre-existing safe destinations are never altered. A second attempt to overwrite a previously committed arrival footprint is rejected.

The default arrival Structure authors a 3×3 stone support plane one voxel below the destination and a 3×3×3 clear volume at/above the player. This is a bounded local safe room, **not** yet an authored surface-access tunnel. Longer connections should be expressed by normal Structure connectors and authored child structures, not portal-specific excavation. A blocked unbreakable Sphere Shell also rejects placement.

## Remaining

Author connected paths from underground arrival rooms to navigable terrain where required. Validate actual Godot travel, lighting, frame boundaries, persistence, and chunk restoration in-game; the Core tests cover the deterministic placement and safe rejection boundaries, not an end-to-end playthrough.
