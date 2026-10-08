# Save/Load migration

Reference: MineClone \`world-systems-rebuild\`, \`src/world/save_session.rs\`,
\`src/world/save_catalog\` and \`src/world/dimension_persistence.rs\`.
Asteria owns chunk residency and archived-state preservation in Core and
keeps Godot as an I/O/interaction adapter.

## Stage 1: stable chunk snapshot codec

- \`ChunkSaveCodec\` in Core serializes a complete chunk's blocks, rotations,
  orientations, facing, state, sculpted microblocks, dye, ordered attached
  layers, and fluid source/level/spread with a versioned binary format.
- Namespaced authored IDs are stored instead of runtime IDs; a changed block
  or fluid registry order does not corrupt restored content.
- Sparse records have deterministic index ordering. Snapshot size, record
  counts, index ordering, content existence, attachment faces, fluid occupancy,
  and a SHA-256 corruption checksum are checked before the isolated restored
  chunk is returned.
- Lighting intentionally remains derived and is recomputed through the normal
  runtime publication pipeline.
- Invalid/truncated/unknown-content snapshots are rejected without mutating
  \`VoxelWorld\` or the authoritative chunk.
- No claim of playable disk saves yet. \`WorldSaveCatalog\` continues to mark
  manifests incompatible until the full world/session restore contract exists.

## Stage 2: complete voxel world snapshot

- `VoxelWorldSaveCodec` captures both resident and archived materialized chunks,
  retaining residency/dirty metadata and preserving pristine or empty chunks.
- Snapshot payloads are detached byte copies. Save capture does not unload
  chunks, mutate the world or materialize unexplored areas.
- Restore returns a new `VoxelWorld`, validated in isolation; archived chunks
  remain unavailable to materializers until restored through normal residency.
- Chunk order is stable Y/Z/X; duplicate positions, negative Y, corrupted
  payloads, invalid content and unbounded chunk counts are rejected.
- Dirty/pristine classification stays distinct from residency and is preserved.
- Core regression tests cover capture/restore, missing chunks, archives, corruption
  and deterministic output.

## Stage 3: multi-Sphere spatial snapshots

- `DimensionChunkSaveCodec` captures every initialized Sphere in stable ID
  order without inventing chunks for Spheres the player never entered.
- The snapshot pins the root seed and immutable generation settings.
  Restoring with a different seed or generation configuration is rejected.
- Every Sphere decodes into its own independent `VoxelWorld` before the
  replacement `DimensionSessionStateStore` is returned. An invalid chunk or
  unknown Sphere never changes an active game's state.
- Per-Sphere materialized chunk history remains isolated; untouched Spheres
  retain lazy initialization. Tests cover multiple Spheres, uninitialized
  Spheres, seed mismatch and invalid payloads.

## Remaining

Persist and restore *all* materialized chunks (resident and archived),
per-Sphere storage boxes and detached entities, pending fluid/physics work,
player inventory and state, world creation settings/game rules, world tick
and day-night clock, manual structures and active Sphere. Add bounded atomic
filesystem publication/recovery with generation validity, world load/delete
and save/leave UI wiring, then real Godot/Wry tests.
