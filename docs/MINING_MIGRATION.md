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

- Authored swing cadence/animation and mining audiovisual feedback; the world-space crack stage overlay is active.
- Architect's Compass, structure-tool behaviors and further consumable item use. Brush, Shears, Artisan's Kit and Bucket are integrated through their scoped runtimes.
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
- Brush and Shears now use authored dye and attached-layer state; layer placement is an inventory action backed by canonical mutations. Visual validation inside a running Godot client is still required.
- Architect's Compass and structure tool require their own authored world placement/collision policies.

## Block surface state contract (2026-10-08)

- Added chunk-owned sparse, immutable `BlockSurfaceState`: an optional namespaced dye ID plus up to 16 ordered `AttachedBlockLayer` records, each bound to one of the six faces. Attached layers are separate from intrinsic block texture layers; Shears remove only the final attached layer on the hit face.
- Layer order, duplicate face/layer rejection, no-op semantics, world/chunk revision updates, worker snapshot cloning and portable `BlockStateSnapshot` transfer are defined in Core. Replacing/removing the host block clears its attached surface state. Artisan's Kit preserves surface state when editing that host.
- `VoxelMutationRuntime.SetBlockSurfaceStateAt` owns presentation invalidation and dirtiness; color/layer-only changes enqueue priority terrain remesh without waking fluid topology, lighting or block physics.
- This state contract is now consumed by the authored layer registry, per-face placement, Shears removal, the Brush dye palette and Core terrain meshing; all changes are derived from the same sparse authoritative state.

## Authored attached layer content (2026-10-08)

- `AttachedLayerRegistry` and `AttachedLayerDefinition` validate optional pack-authored `data/layers/*.json` definitions (safe relative texture, supported faces, tint, bounded offset, alpha mode and shadows).
- `TerrainTextureCatalog` deterministically includes these texture paths in the shared array, without a new Godot texture manager or layer nodes.
- The default `asteria:foliage_layer` reuses the authored grass overlay texture. `asteria:moss` uses the original MineClone moss PNG, imported without modifications. The portal bitmap remains separate/pending.


## Surface layer mesh projection (2026-10-08)

- A Sphere's terrain mesh worker now receives the immutable `AttachedLayerRegistry` and resolves attached layers from chunk snapshots. `ChunkMeshDataBuilder` projects offset non-colliding quads onto exposed cube faces and onto exposed fine-geometry surfaces of sculpted hosts.
- Existing greedy terrain and fine geometry paths retain their collision contract. Per-layer rotation, face selection, layered offset and authored tint/render/shadow policy are applied by the existing texture-array mesh data pipeline.
- Shears removes attached layers via native right-click; an inventory-selected layer places onto a supported face, and Brush paints opted-in host blocks. Their visuals are generated by Core mesh workers; only the MineClone portal bitmap remains pending.

## Brush tintura (2026-10-08)

- Cores HSI do MineClone são authored em `packs/default/data/dyes/*.json`; `DyeRegistry` valida cores, IDs e ordena a paleta.
- `secondaryProperties: ["dyed"]` opta um bloco à pintura. `ToolGameplayRuntime` mantém a seleção por sessão, inicialmente como modo limpar, e grava a tintura somente via mutação canônica de `BlockSurfaceState`.
- Pintura rejeita IDs desconhecidos, blocos incompatíveis, invioláveis e no-ops. O mesher consulta a tintura do estado autoritativo: blocos pintados substituem biome tint apenas nas texturas dyable, e o greedy mesher não mescla faces com cores diferentes.
- Clique direito nativo do Brush abre a paleta mesmo sem alvo; Godot suspende o input do jogador enquanto aberta, e Escape/fechar restauram a captura. A WebUI React apenas exibe as 16 cores e envia seleção/limpeza sem capturar controles globais de gameplay.
- A interface segue Atomic Design e preserva o font family da Asteria; Storybook e traduções inglês, português brasileiro e espanhol foram acrescentados.

## Creative layer inventory / Shears (2026-10-08)

- The authored `Layer` entry kind is a fourth inventory identity (blocks/items/tools/layers). Creative inventory is populated from selected-pack `AttachedLayerRegistry`, with resource-path-derived icons and category, not browser-fabricated items.
- Native right-click with a selected layer places it onto the hit block face; `AttachedLayerPlacementRuntime` validates layer definition, face, loaded host, geometry kind, unbreakable guard, duplicate/16-layer limit and then asks `VoxelMutationRuntime` to update the sparse state. Survival consumes exactly one selected layer after acceptance; Creative is non-consuming.
- The existing Shears right-click removes the most recently attached layer on the target face. Layers are rendered through the same Core worker mesh path, without collision or per-voxel nodes.
- `asteria:foliage_layer` and `asteria:moss` appear as independent creative catalog choices. The former uses the existing grass overlay; the latter uses the exact original MineClone asset under `resources/textures/layers/moss.png`.

## Original moss PNG provenance (2026-10-08)

- MineClone source: `sarakborges/mineclone`, branch `world-systems-rebuild`, `assets/textures/layers/moss.png`.
- Default pack asset: `packs/default/resources/textures/layers/moss.png`; byte-for-byte original, Git blob SHA-1 `8662adb0a4c3a2be39aedda0f666a25d80e85e1d` (8,211 bytes). No image regeneration, palette changes, engine-specific import sidecars or resampling in the stored PNG.
- `packs/default/data/layers/moss.json` ports the MineClone `asteria:moss` layer definition: `natural_blocks` category, foliage tint, 0.001 offset, 0.5 alpha cutoff, all six faces by default and no layer shadows. Runtime terrain array normalization is independent from the original pack file.
- The original PNG was transferred with a temporary checksum-verified CI operation; the temporary transfer step is removed from the final PR tree. This is a static pack addition, not a new runtime loader.

 
## Architect's Compass selection/export (2026-10-08)

- MineClone reference: `data/tools/structure_tool.json`, `src/tools/structure_tool.rs`. Authored `asteria:architects_compass` is the Structure Tool: right-click one adjacent loaded empty voxel to set the first corner, then right-click a second to export an axis-aligned selection.
- `ArchitectsCompassRuntime` owns session-scoped selection and tool/slot identity; an equipment change clears it. `StructureSelectionBounds` limits height, width, depth and total scanned volume before any world scan; no negative Y, no hidden chunk loading, and no world mutations are introduced.
- `StructureSelectionExporter` deterministically samples only loaded-world voxels (Y/Z/X), generates Asteria's existing `palette` + `layers` JSON, and validates it with `StructureDefinitionJson` and `StructureRegistry`. An all-air selection, unloaded cells, unsupported fluids and nonrepresentable per-block state/sculpting/dyes/layers are rejected rather than silently exported with data loss. A Sphere-scoped `StructureSelectionPresentation` draws only one change-driven outline.
- The Godot adapter saves successful JSON outside authored packs under `user://exports/structures/`; export IDs are unique GUIDs chosen by the adapter, not nondeterministic Core gameplay. WebUI receives ordinary semantic toast notifications. Preview requires a real native target, never browser-captured world input. Export limit is 16,384 voxels per operation to bound synchronous latency.

## Extended structure voxel palette state (2026-10-08)

- Asteria extends the existing structure palette entry schema with optional `textureRotation`, `facing`, `state`, `microblocks`, `dye`, and ordered `attachments`. Without these fields, old structures parse identically.
- Sculpted geometry uses a canonical eight-slice, 128-character hexadecimal mask (partial occupancy only). `attachments` records each face, layer ID and rotation; attachment order is retained for Shears. All properties round-trip from authoritative loaded voxel snapshots through the Compass exporter and `StructureDefinitionJson`.
- `StructureRegistry.ValidateBlocks` rejects masks on non-fragmentable hosts, unsupported dyes, and invalid layer definitions/face permissions when registries are supplied. `SurfaceStructureField` carries the immutable state in placements; `SurfaceChunkMaterializer` restores microblock palette IDs and sparse surface state when writing generated structures. There is no second procedural writer.
- Rotation-aware structure placement now transforms the 8×8×8 sculpted mask about Y, horizontal facing, attached face direction and per-face UV rotation, preserving dye and ordered layers. Top and bottom attached UVs counterrotate differently; lateral UV orientations remain stable. Rotated layer face permissions are validated for all four angles. Authored `rotation: true` is permitted with detailed blocks, and new Compass exports enable it. Fluids are still excluded from Compass export. Base block texture-rotation metadata is preserved unchanged as an authored per-cell value (the current voxel model does not store separate rotation for six intrinsic faces).

