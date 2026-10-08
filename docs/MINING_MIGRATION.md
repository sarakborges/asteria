# Progressive block mining

Reference: MineClone `world-systems-rebuild`, `src/targeting/mining.rs` and `src/targeting/mining_visual.rs`, alongside `src/content/block.rs` and `src/content/tool.rs`.

## Active contract

- Core `BlockMiningRuntime` owns ongoing Survival mining only within the current Sphere. Progress is accumulated from `WorldTickClock.TicksThisFrame`, using `200 × hardness` logical work ticks, matching MineClone's default work baseline. This means authored world ticks per second affect duration in real time.
- `ToolGameplayRuntime.EffectiveMiningSpeed` is the single eligibility/speed calculation. Mining requires an `asteria:mine` left behavior for equipped tools. `requiredTools` blocks unsupported tool categories. `preferredTools` multiplies work per tick by the matching tool's authored positive `mining.speed`; bare hands and unrelated items mine non-required blocks at speed 1. Category mismatches cannot gradually wear down protected blocks.
- Accumulated work resets on input release, target loss, player mode changes, target voxel-state change, hotbar slot change or item identity change. A target may be mined only through loaded voxel content. `unbreakable` always wins.
- Completion invokes the existing `BlockInteractionRuntime.Break` mutation and loot path, avoiding a duplicate voxel writer. Creative retains one-click breaking; spectator cannot interact; special left-hand carpenter/other authored actions are not misinterpreted as mining.
- Godot `FpsPlayer` owns mouse-button press/release and modal/capture cleanup. `Main` only orchestrates the target query and Core invocation. React's Target HUD renders ten discrete progress stages received from `game.hud.mining`; it does not own mining timing, input, or block state.
- Core tests cover logical progress, required/preferred categories, speed, zero hardness, unbreakable content, target/tool/slot change and cancellation.

## Still pending

- World-space crack/decal stage overlays that accurately follow block and microblock geometry (the current HUD progress is a real progress signal, not a fake surface crack texture).
- Authored swing cadence/animation and mining audiovisual feedback.
- Extended item/tool behaviors (Brush, Bucket, Shears, Artisan's Kit, Architect's Compass) and consumable item use.
- Loot-table policy parity beyond existing `dropsSelf` and creature-specific drops.

The native input, mutation, content and Sphere boundaries remain unchanged: no UI-side key capture, no negative Y, and no pack-owned Godot resource imports.
