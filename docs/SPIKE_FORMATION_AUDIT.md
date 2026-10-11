# Spike formation / geometry / loot audit — 2026-10-11

## Reference and scope

Compared Asteria `SpikeFormationRuntime`, `SpikeSegmentState`,
`ChunkMeshDataBuilder`, `BlockGeometry` and `BlockInteractionRuntime`
against MineClone `main` and `world-systems-rebuild`.
MineClone has no directly equivalent stackable square-spike formation runtime:
the Asteria formation is game-specific. MineClone is a reference only for
clockwise voxel face winding, canonical mutation boundaries and chunk behavior.

## Existing features retained

- One formation of at most 16 same-material, same-direction segments;
  both upward and hanging chains, including vertical chunk seams.
- Canonical mutations reprofile the remaining rooted segments when a
  middle segment is destroyed; detached unsupported tips become one
  portable segment drop apiece. Returned items omit internal index/height.
- A fully unsupported formation detaches on support removal, including
  external mutations and deferred chunk-residency seam repair.
- Creative breaks of a host suppress all dependent spike drops.
- Axis-aligned tiered square mesh and fine occupancy-based collision
  share `SpikeSegmentState.HalfWidthAt` and use consistent voxel
  clockwise front-face winding.

## Corrections

1. **Visible seams:** `ChunkMeshDataBuilder` now considers the *actual*
   adjacent resident voxel before hiding the surface of an expected
   same-formation segment. It only removes the end cap if the neighbor
   matches block ID, profile index, height and direction. Stale state,
   missing chunks and edited neighbors produce a properly closed cap
   instead of revealing the interior. A matching neighbor exposes the
   square tier ring, not a redundant inner cap.
2. **Drop capacity:** deliberate spike breaks preflight all detached
   segment rewards; deliberate support-block breaks preflight their
   own normal/loot-table reward **plus all immediately detached
   dependent spike segments**. A full pool rejects the operation before
   a voxel mutation and does not evict existing drops. Creative
   suppression still removes the complete affected formation.
   Nonresident segments remain deferred until the chunk is restored,
   preserving the existing session/chunk authority.
3. **External profile mutations:** edits which replace a spike's
   profile but retain the same block ID now reprofile the old root,
   detach its unsupported tips and recheck the replacement. A pure
   texture-rotation edit does not invalidate the formation.

## Focused automated verification

- Original placement/removal, middle/tip, support, both directions,
  archiving and seam-residency tests retained.
- New tests assert actual end cap at missing/mismatched neighbors
  and cross-chunk full-cap ↔ narrow ring transitions, with matching
  collision triangles.
- New tests assert insufficient capacity preserves spikes and
  supports without evicting existing items; exact capacity produces
  the right number of unique rewards; Creative does not generate drops.
- New tests cover same-material profile updates, harmless rotation
  edits and fine collision checks for upright and hanging square spikes.

## Not claimed

- External arbitrary voxel mutations (e.g. a script deleting the anchor)
  cannot be rejected based on a player's inventory/drop budget *before*
  they happen; those already-published mutations use the pre-existing
  bounded world-item behavior during dependent detachment.
- The actual Godot renderer, first/third-person camera and physics
  feel still require a native playtest. Automated mesh/collision tests
  are not an in-engine visual sign-off.
- No audio effects are in scope.
