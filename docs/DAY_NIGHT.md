# Day/Night runtime

Ported from MineClone `world-systems-rebuild`: `src/content/day_night_cycle.rs`, `src/world/day_night.rs`, `src/rendering/environment.rs`, `src/rendering/sun_lighting.rs` and `data/dimensions/overworld/day_night_cycle.json`.

- The Core `DayNightCycleDefinition` and `DayNightCycleRegistry` own immutable authored phase durations, skylight factors, clock mapping and orbital metadata, loaded from the active pack's `data/day_night_cycles/*.json`.
- Every dimension references exactly one cycle through `dayNightCycle`. Default Overworld uses 48,000 ticks/day (20 minutes at 40 TPS), 06:00 start, and equal Dawn/Day/Dusk/Night durations; Umbral has a separate cycle and retains its authored environment palette.
- Each `DimensionRuntimeSession` owns its `DayNightClock`. `WorldTickClock` remains the only tick producer. At retirement, `DayNightClockState` is stored alongside the world tick in `DimensionSessionState` and restored when the Sphere is revisited. There is no parallel frame timer.
- Crossing world midnight increments the displayed calendar day independently of a full cycle reset, matching MineClone. The WebUI receives `game.hud.clock` only when the displayed minute/day changes, plus force snapshots on UI bridge reconnect and Sphere activation.
- Godot `DimensionEnvironmentPresentation` owns background, ambient/fog presentation, directional sun and visual celestial quads; the authored phase interpolation modifies those engine properties. The Overworld uses the original MineClone sun and moon PNGs in `packs/default/resources/textures/sky/`, selected through optional, validated `sun.texture`/`moon.texture` paths on its data-driven day/night cycle. Godot decodes them with `ProjectPackFiles`, never `ResourceLoader`. A Sphere without textures retains plain unshaded quads; texture lifetimes follow Sphere activation and no per-tick asset loads occur.
- **Native QA pending:** confirm transparency, billboard orientation, tint, fog and exact image framing in Godot/WRY. Stars/clouds remain a separate parity item.
- **No shaders or voxel light propagation are altered.** The terrain remains under its existing unshaded voxel-light contract; environment changes do not darken unshaded block faces. The user explicitly excluded shader changes.
- Global/gameplay keyboard input continues to be handled only by Godot.

Tests cover pack validation, phase boundaries and continuity, world-midnight crossing, save-state restoration and large tick batches.
