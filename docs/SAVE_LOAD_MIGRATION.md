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

## Remaining

Persist and restore *all* materialized chunks (resident and archived),
per-Sphere storage boxes and detached entities, pending fluid/physics work,
player inventory and state, world creation settings/game rules, world tick
and day-night clock, manual structures and active Sphere. Add bounded atomic
filesystem publication/recovery with generation validity, world load/delete
and save/leave UI wiring, then real Godot/Wry tests.
