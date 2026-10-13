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

This is attempted for every new physical portal crossing when no matching return portal is present near the exact arrival, even if the landing itself is already safe, after initial chunk residency and presentation are ready. The result is checked against authoritative resident voxels again; failure retains the existing safe rollback rather than placing the player in solid/fluid terrain or moving to unrelated coordinates. An existing matching nearby return portal is reused without another Structure write. A second attempt to overwrite a previously committed arrival footprint is rejected. After Structure publication, the travel state waits for terrain mesh work and its Godot collision publication to finish before instantiating the player; the regular loading loop remains active during this final phase.

The default arrival Structure authors a 3×3 support plane, a cleared landing and a Glass return frame enclosing two vertically stacked oriented `asteria:portal` voxels. The canonical placement transaction stamps `target_dimension` with the actual origin Sphere onto both portal voxels; the shared JSON contains no hard-coded destination. Its normal Structure output connector points to `asteria:surface_spiral_stair_segment`, an authored, rising 16-step spiral with head clearance. Each segment connects to the next; the generic `stopAtSurface` and `surfaceClearance` fields stop expansion when the path reaches generated terrain height at the connector. Strength loss and the existing 4,096-voxel placement budget bound expansion. The normal Structure conflict, shell and canonical voxel-mutation safety rules still apply.

During exact portal travel, the chunk streaming selector temporarily includes a 3×3 chunk-column footprint from the arrival to the surface, limited to 32 vertical chunks per column. Ordinary underground gameplay does not use this enlarged selection. If the path requires unavailable chunks, exceeds limits or crosses protected/generated/modified structures, placement rejects safely and travel rolls back rather than silently changing coordinates.

## Remaining

Validate player navigation and the visual appearance of surface exits in Godot, plus lighting, frame boundaries, save/reload and chunk restoration. Core tests establish deterministic connector geometry and bounded selection, not hands-on gameplay. Extreme vertical separation and conflicts may legitimately reject placement and trigger rollback.

## Frame and interior capacity

Both the Glass-frame search and its interior flood are bounded to **4,096 voxels**, matching MineClone `world-systems-rebuild` rather than the former Asteria-only 1,024/512 caps. The existing independent **64-voxel per-axis span**, resident-only/no-fluid constraints, single-plane requirement and atomic mutation preflight remain unchanged. Portals can span chunk boundaries; breaking a qualifying Glass boundary still retracts the complete same-target interior. Core tests exercise a 3,364-cell cross-chunk portal and rejection beyond 4,096 without mutation.
