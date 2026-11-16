# Projectiles (Core + Godot)

`ProjectileRuntime` is the authoritative owner of active projectiles within one Sphere. It holds bounded, deterministic IDs, position, velocity, gravity, remaining lifetime, source identity, radius and damage. Godot's `ProjectilePresentationController` only mirrors these positions as cube visuals.

## Launching

Call `DimensionRuntimeSession.TryLaunchProjectile(new ProjectileLaunch(...))` from a weapon, creature ability or other gameplay feature. Launch parameters define `Kind` (stable logical ID), initial position and velocity in world units, cubic collision half-extent (`Radius`), damage, gravity multiplier, lifetime, and `ProjectileOwner`. Creature-launched projectiles must specify the source creature ID.

No weapon or input binding is assumed by this foundation. Adding an authored arrow, spell, or throw action requires the caller to provide the launch parameters; no additional collision implementation is needed.

## Impacts

- Movement is swept along the entire bounded frame segment (not point-sampled at the final position). Cubic blocks use slab tests; fine block geometry uses `BlockGeometry.Intersects` within the swept candidate interval.
- Projectile collision radius is applied to voxel geometry, authored creature physical colliders and the player's physical bounds.
- A collidable block, creature or targetable player causes one impact and removes the projectile immediately.
- Unloaded space terminates a projectile; it never silently travels through missing chunks. Y below zero is blocked.
- The launching player or source creature is ignored for self-hit purposes.
- Ties are deterministic: block, unloaded boundary, creature (lowest ID), player.
- Creature damage and loot use `DimensionRuntimeSession.TryAttackCreature`; player damage enters the existing `PlayerHealth` owner with equipment reduction.
- The population is capped at 256, speed at 256 blocks/s and lifetime at 30 seconds. Per-frame integration is capped at 100 ms to bound collision work after a stall.
- Projectiles are transient: retiring a Sphere clears them rather than adding in-flight attacks to persistent world saves.

## Checks

`ProjectileRuntimeTests` covers fast collisions, voxel occlusion, creature ID tie-breaks, shooter immunity, player hits, unloaded-chunk boundaries, gravity, lifetime and invalid parameters.
