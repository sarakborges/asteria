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

## Stage 7: Sphere manual history and detached dynamics

- The per-Sphere session snapshot now carries committed manual-structure
  footprints, falling blocks with velocity and IDs, dropped stacks with
  position/velocity/age/settled support, and creature identity, health,
  motion, tags and age. Every runtime snapshot is copied behind read-only
  arrays with explicit count, unique-ID and finite-value validation.
- Manual placement journals rebuild their sparse reservation index using
  a validated temporary ledger. Overlaps and oversized 2D footprints are
  rejected before publishing any reservations to the restored Sphere.
- Restored dynamics remain attached to the target `DimensionSessionState`.
  The normal owning runtimes receive and further validate them on session
  initialization; no shadow entity owner is introduced.
- Tests cover per-Sphere separation, recovered overlapping placement
  prevention, detached snapshot immutability and malformed entity rejection.
- Runtime block IDs in captured falling blocks/drops are still in-process
  identities. They must be mapped to authored IDs by the full disk session
  codec before any playable load is enabled.

## Stage 8: pending fluid and block-physics work on Sphere retirement

- `FluidUpdateQueue` now captures the complete topology work set, due tick
  schedule, and dormant ticks for unloaded chunks. Pending work follows its
  owning Sphere through retirement and resumed session initialization.
- `BlockPhysicsUpdateQueue` preserves its deduplicated FIFO wakeup sequence.
  Both queue snapshots are detached, bounded and reject duplicate or invalid
  world coordinates/tick identities.
- `DimensionRuntimeSession` captures both queues after draining workers,
  and reconstructs them on Sphere re-entry. This fixes lost pending fluid
  and block-support work during dimension transitions even without disk save.
- Session capture/restore includes both pending queues. They still carry
  runtime fluid IDs, so a final disk codec must remap to namespaced IDs.
- Regression tests verify topology/schedule/dormant work, FIFO restoration,
  corruption rejection and per-Sphere session snapshots.

## Stage 9: portable stack encoding for the complete session file

- `PortableStackSaveCodec` writes versioned-session-ready, bounded portable
  stacks for player inventory/cursor, Storage Boxes and detached item drops.
  All block references use stable authored IDs and preserve orientation,
  texture rotation, state, microblock geometry, dye and attached layers.
- Item/tool stacks preserve quantities, max sizes and sorted authored metadata;
  layer items validate against the selected pack. Corrupt payloads and missing
  authored definitions are rejected before state publication.
- Regression tests cover registry reordering, item metadata and invalid/truncated
  block records. The complete session container format and atomic publication
  remain pending.

## Stage 10: complete session stream format

- `GameplaySessionFileCodec` writes one versioned deterministic stream with
  player mode, flight, inventory/cursor, rules, the complete multi-Sphere
  spatial snapshot and each Sphere's non-voxel session state.
- `SphereSessionFileCodec` includes clock, last player position, natural
  spawn time, Storage Boxes, manual placement records, falling blocks, drops,
  creatures and both pending work queues. Block and fluid entities are
  remapped to pack-authored namespaced IDs when serialized.
- Bounds and validation reject malformed sizes, duplicate state, unknown
  content and trailing bytes before building the detached session snapshot.
- Publication, generation rotation and end-to-end native save wiring remain
  separate from the stream format until recovery behavior is verified.

## Stage 11: atomic complete-session generations (Core only)

- `SessionSaveStorage` publishes the entire detached `GameplaySessionSnapshot`
  (world state, player, all initialized Spheres, entities and queues) as one
  checksummed `session-*.bin` generation, separate from spatial-only files.
- The active Sphere ID is now part of the session snapshot and a prerequisite
  for durable publication. It must be supplied by the Godot session controller
  at the quiescent capture boundary.
- `AtomicSaveGenerationStore` now owns shared publication, locking, SHA-256
  validation, retention of four generations and fallback to an earlier valid
  generation. Existing spatial-only saves use this same narrow primitive.
- `RestoreLatest` validates complete Core reconstruction of a candidate
  before accepting its generation. Corrupted/mismatched newer saves do not
  replace the active session, and callers can select the earlier valid file.
- These are **not yet playable saves**. The game has not connected its active
  runtime quiescence, native save/load lifecycle, content registry/cycle
  validation and UI catalog to this Core-only storage layer.

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

## Stage 12: Godot checkpoint entry through existing retirement

- Native `ui.world.save` action and Ctrl+S trigger a checkpoint only while
  the active Sphere is ready and no transition is underway.
- `DimensionSessionController.RequestCheckpoint` cooperatively drains the
  exact workers already owned by dimension travel, captures the Core state
  after retirement/archiving (including player position and active Sphere),
  and publishes one session generation on a background task.
- No active gameplay session is reconstructed until publication finishes;
  save failure still reconstructs the same Sphere and reports an explicit
  error rather than abandoning the player's world.
- The existing loading/residency/presentation pipeline is reused to return
  the player to the current Sphere. Save I/O never runs on the Godot frame.
- This is **checkpoint writing only**. Loading from the world catalog, save
  metadata manifest publication and in-game save/leave are not yet enabled.

## Stage 13: derived world-catalog manifest

- A successful native checkpoint now atomically publishes a bounded
  `world.json` metadata manifest **after** its session generation has
  been committed. It derives its name, seed, active Sphere, position and
  day from the detached session snapshot and includes the generation number.
- The manifest is not authoritative. `WorldSaveCatalog` still reports
  `Compatible=false` until actual registry-aware restoration checks run.
- Mismatched directory names and incomplete sessions cannot publish a
  misleading catalog entry. Tests verify discovery and rejection behavior.

## Stage 14: validated native world entry

- The world catalog validates complete sessions on a bounded background
  scan before setting `Compatible=true`; manifests alone never enable Load.
- `WorldLoadController` validates the requested world ID as a single canonical
  directory component, decodes and reconstructs all Core state in a worker,
  and publishes the result only after complete validation.
- New and restored worlds share `Main.InitializeWorldSession`, including the
  existing loading/residency/presentation path and restored player position.
- The world-selection Load button now emits a semantic WebUI request;
  browser code neither owns the save path nor parses snapshot files.
- Delete/save-and-leave actions and native end-to-end tests remain pending.
