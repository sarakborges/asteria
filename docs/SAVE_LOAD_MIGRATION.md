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

## Stage 4: durable spatial-generation files (not playable saves)

- `DimensionChunkFileCodec` writes a versioned binary snapshot with the world
  seed, immutable generation settings, every created Sphere ID, and every
  resident/archived chunk's coordinates, dirty status, and checked binary
  payload. Strict limits reject malformed IDs, booleans, indexes, counts,
  oversized files and trailing bytes; ordering is deterministic.
- `SpatialSaveStorage` writes to a private temporary file, flushes it, appends
  a SHA-256 checksum, flushes again and publishes by same-directory rename.
  The latest fully intact committed generation is selected on load; damaged
  newer generations are skipped. Unpublished temporary files are ignored.
  The four newest generations are retained and old staging files pruned.
- Saving and loading are serialized with a per-directory exclusive lock.
  Spatial files use `sphere-chunks-*.bin` and deliberately do not create
  `world.json` or mark incomplete worlds playable in the world catalog.
- File renames are atomic on supported local filesystems, but this implementation
  does not promise directory-metadata fsync durability across sudden power loss
  on all platforms. Recovery always validates an entire file before returning it.
- The detached spatial snapshot is the only input to this I/O API; background
  persistence cannot read or mutate authoritative runtime chunk objects.
- Unit tests verify deterministic binary roundtrip, generation rotation,
  damage fallback, truncated files, ignored staging, and malformed input.

## Stage 5: shared player and Sphere scalar state

- `GameplaySessionSaveCodec` captures/restores inventory slots and cursor,
  selected hotbar slot, item metadata, game mode/creative flight, mutable
  gamerules, every initialized Sphere's world tick, day/night clock,
  last player position, and natural-spawn scheduling tick.
- Saved player inventory is detached from exposed arrays; the Core owner
  validates the complete slot layout before restoring and never routes
  authoritative mutations through the UI or Godot.
- Session restoration first validates the spatial snapshot and constructs
  a new `DimensionSessionStateStore`; active sessions remain untouched on
  mismatched world identity, missing Sphere or invalid content.
- No save is marked playable at this stage: container contents, dropped
  entities, physics, manual structures and full metadata disk publication
  still require integration.
- Added regression tests for shared cursor/metadata and per-Sphere clock
  isolation, immutable inventory views, flight validation, corrupted
  snapshot and world-name mismatch.

## Stage 6: per-Sphere Storage Box contents

- Occupied Storage Boxes now snapshot as detached 27-slot arrays under
  the owning Sphere (including items with authored metadata).
- Restoration verifies each container has the actual Storage Box block in
  an already materialized resident **or archived** chunk, without moving
  archived chunks into residency. Duplicate/missing containers are rejected.
- Contents are installed only after all containers have been validated.
  Tests cover identical coordinates with different inventories in two
  Spheres, archived block validation, snapshot immutability, and corruption.
- Inventories are not exposed to Godot/UI as mutable saved-state arrays.
  This is still an in-memory full-session capture layer, not a playable
  disk save: detached creatures, manual structures, physics and full
  generation publication remain outstanding.

## Remaining

Wire the spatial snapshot capture and disk publisher into a quiescent
gameplay save lifecycle. Persist and restore the remaining authoritative
world/session state: player inventory, cursor, game mode and movement, game
rules and creation options, per-Sphere storage boxes and manual structures,
detached creatures/drops and pending fluid/physics work, world ticks, day/night,
current Sphere and all saved player positions. Publish one manifest only after
*all* spatial and session components of a generation have been validated.
Implement world load/delete and save/leave UI after full restore is supported.
Test recovered worlds end-to-end in Godot/Wry before marking any catalog entry
compatible. Do not present the spatial-only files as playable saves.
