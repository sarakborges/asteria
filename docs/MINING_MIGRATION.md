# Progressive block mining

Reference: MineClone `world-systems-rebuild`, `src/targeting/mining.rs` and `src/targeting/mining_visual.rs`, alongside `src/content/block.rs` and `src/content/tool.rs`.

## Active contract

- Core `BlockMiningRuntime` owns ongoing Survival mining only within the current Sphere. Progress is accumulated from `WorldTickClock.TicksThisFrame`, using `200 × hardness` logical work ticks, matching MineClone's default work baseline. This means authored world ticks per second affect duration in real time.
- `ToolGameplayRuntime.EffectiveMiningSpeed` is the single eligibility/speed calculation. Mining requires an `asteria:mine` left behavior for equipped tools. `requiredTools` blocks unsupported tool categories. `preferredTools` multiplies work per tick by the matching tool's authored positive `mining.speed`; bare hands and unrelated items mine non-required blocks at speed 1. Category mismatches cannot gradually wear down protected blocks.
- Accumulated work resets on input release, target loss, player mode changes, target voxel-state change, hotbar slot change or item identity change. A target may be mined only through loaded voxel content. `unbreakable` always wins.
- Completion invokes the existing `BlockInteractionRuntime.Break` mutation and loot path, avoiding a duplicate voxel writer. Creative retains one-click breaking; spectator cannot interact; special left-hand carpenter/other authored actions are not misinterpreted as mining.
- Godot `FpsPlayer` owns mouse-button press/release and modal/capture cleanup. `Main` only orchestrates the target query and Core invocation. React's Target HUD renders ten discrete progress stages received from `game.hud.mining`; it does not own mining timing, input, or block state.
- `MiningCrackPresentation` owns a single transient 3D mesh instance per active Sphere, using `BlockStateMeshBuilder` to reproduce the current block's authored mesh and microblock occupancy. The 10 original staged 64×64 PNGs live in the default pack's resources. Shader stages change only on progress steps, and the overlay is hidden when Core clears the target or mining ends. No per-block scene nodes or pack import sidecars.
- Core tests cover logical progress, required/preferred categories, speed, zero hardness, unbreakable content, target/tool/slot change and cancellation.

## Still pending

- Better authored swing cadence/animation and mining audiovisual feedback; the world-space crack stage overlay is now active.
- Authored swing cadence/animation and mining audiovisual feedback.
- Extended item/tool behaviors (Brush, Shears, Architect's Compass) and consumable item use. The Artisan's Kit and Bucket are implemented in their scoped runtimes below.
- Loot-table policy parity beyond existing `dropsSelf` and creature-specific drops.

The native input, mutation, content and Sphere boundaries remain unchanged: no UI-side key capture, no negative Y, and no pack-owned Godot resource imports.

## Bucket source-fluid use (2026-10-08)

- Ported the MineClone `src/tools/bucket.rs` contract to Core `BucketGameplayRuntime` using `FluidRegistry`, `VoxelWorld`, `VoxelMutationRuntime` and the existing player inventory. Godot only forwards a native right-click camera ray and block target. No duplicate fluid simulation owner is created.
- An empty authored `asteria:bucket/use` tool uses a loaded-world block-occluded DDA to collect a **source** fluid cell, excluding spreading fluid. The resulting single bucket is tagged `contained_fluid` with the loaded fluid definition ID. A filled bucket places one full source cell in an adjacent loaded, empty voxel, checking Y≥0 and rejecting occupied fluid/block positions.
- `PlayerInventory.TryReplaceSelected` permits only a one-item same-ID/same-kind/same-stack-limit metadata transition. The world mutation rolls back if the inventory transition is rejected; failed/invalid uses do not consume inventory or fluid.
- This covers the fluid flow and metadata contract only. The current authored bucket entry retains its default icon when filled; variant icons and audible/visual use feedback can be authored later. Existing fluid simulation remains authoritative for spread.

## Artisan's Kit microblock editing (2026-10-08)

- Reference: MineClone `world-systems-rebuild/src/tools/artisans_kit.rs`. `ArtisansKitRuntime` belongs to the active Sphere, alongside `BlockMiningRuntime` and `BucketGameplayRuntime`.
- Authored `asteria:artisans_kit/remove` (native left click) and `asteria:artisans_kit/restore` (right click) change occupancy on existing `fragmentable` cube blocks only. It never creates a parent in an empty voxel, alters an unbreakable block or silently mutates unsupported hollow/layer shapes.
- Core resolves the targeted occupied 1/8-cell with a bounded ray scan and snaps edits to resolutions 4/2/1 microcells per edge (in-world size 1/2, 1/4 and 1/8 block). Tool Action cycles resolution in Godot and WebUI displays the current scale next to the selected block with localization. Browser input is never used for the action.
- Edits use `MicroblockMask.Edit`, `BlockStateSnapshot` and `VoxelMutationRuntime` to ensure mesh/lighting/physics/fluids stay invalidated through the canonical owner. Full occupancy reverts to a normal voxel, and removing all occupancy clears it. Restoration is rejected when the candidate sub-volume would intersect the player's authoritative collision AABB.
- Focused tests validate partial removal/restore, block identity/state preservation, protected blocks, tool validation, cycling and collision rejection.

### Remaining parity

- The current tools have no durability wear or inventory rewards from cutting fragments; neither is invented as a side effect.
- Brush requires authored secondary dye properties and the palette interaction; Shears requires authored per-face layers and a layer-removal mutation contract. Their MineClone behavior cannot be faithfully emulated by simply changing block IDs.
- Architect's Compass and structure tool require their own authored world placement/collision policies.
