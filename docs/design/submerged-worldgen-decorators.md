# Submerged worldgen decorators

A biome's `decorations` may opt into generated-fluid occupancy with
`fluidPlacement`. This applies to surface and vertical decorators in the
Core materializer, including oceans, authored surface fluids and crater fills.

```json
{
  "block": "asteria:reef",
  "chance": 0.2,
  "surfaceBlocks": ["asteria:stone", "asteria:gravel"],
  "fluidPlacement": "submerged"
}
```

- `dry` (default): only when the placement voxel has no generated fluid;
  existing packs retain this behavior.
- `submerged`: only when the actual placement voxel contains generated fluid.
- `any`: either occupancy.

Other decorator restrictions (surface materials, conditions, habitat
weights and noise clustering) remain in effect. Actual fluid occupancy is
queried from `GeneratedFluidField`; a column's bounding range alone
does not count as submerged. All generation is deterministic and independent
of chunk materialization order.

The voxel storage invariant is unchanged: blocks and fluids cannot occupy
the same voxel. A submerged decorator occupies its own voxel, displacing
the fluid there while leaving surrounding water intact. This is suitable
for solid underwater decorations; water-through foliage would require a
separate visual/object-occupancy capability rather than violating voxel
storage. Surface structures already use `requiresDryGround: false` and
`generation.fluidPolicy` independently.

## Default Ocean flora and objects

Five lightweight objects are authored entirely in the default pack:
`seagrass` (common Sand Flats), `kelp` (Rocky Reefs/Gravel Banks),
`sea_anemone` (Rocky Reefs), `seashell` (Sand Flats) and `starfish`
(sparse on mixed seabeds). All have independent transparent 16x16
textures, supporting-ground material lists, deterministic noise clusters,
`surfaceHabitats` weights, `conditions.maxY: 86` and
`fluidPlacement: "submerged"`. Kelp, seagrass and anemone are
non-collidable crossed sprites; shell and starfish use horizontal ground
sprites. Normal block mining/support semantics apply.

This is authored content only. The existing fluid-placement contract
displaces the water in the occupied decorative voxel while retaining
surrounding water; it does not add a new water-through object layer.

## Swamp shoreline / water-surface constraints

Generic `decorations[].fluidRequirement` authoring supplies two reusable
placement relations (validated against the selected pack's fluid registry):

- `{"fluid":"asteria:water","relation":"nearby","maxDistance":2}`
  requires matching generated fluid within the horizontal 2-block radius
  at the supporting ground's elevation. It does not count water directly
  underneath or replace the actual placement block.
- `{"fluid":"asteria:water","relation":"below"}` requires that exact fluid
  directly beneath the decoration, with its own voxel **above** the fluid
  surface. No distance parameter is allowed.

Cattail uses the former rule and regular `support_below` on Mud, Dirt
or Grass Block. Lily Pad uses the latter and occupies a thin horizontal
ground sprite one voxel above the Water selected by Swamp's level mosaic.
The generated source Water is never removed for a Lily Pad and the rules
are deterministic across chunk boundaries. The fluid requirement
affects authored generation; runtime behavior after editing/removing the
supporting fluid is a separate support-physics concern.

## Swamp roots and Willow hanging moss

The default Swamp adds `asteria:exposed_roots` as a lightweight
`groundSprite` decorator on Mud/Dirt/Grass Block. The generic nearby
Water restriction (three horizontal voxels), clustered noise and
habitat-weighted density strongly prefer Willow Groves without changing
the terrain or its same-level mosaic.

`asteria:hanging_moss` is a noncollidable, wind-swaying crossed sprite.
Seven sparse hanging segments per authored Willow variant are integrated
directly into each `tree_willow_01..03` Structure template: every Moss
voxel is empty in the original tree and has a Willow leaf immediately
above it. Tree placement, rotation, conflicts, water avoidance and biome
constraints still belong to the existing Structure system; no new
generator or canopy scan is added.

The generic `support_above` block capability keeps canopy hangers
dependent on the block directly above. Support is evaluated using the
same full voxel/microblock face coverage as `support_below` in the
opposite direction. Block mutation wakes the neighboring cells
**both above and below**, and loaded-chunk support reconciliation
includes `support_above` blocks. Thus removing a supporting leaf
detaches Hanging Moss, even when the two voxels lie in adjacent chunks.
