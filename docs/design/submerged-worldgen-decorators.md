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
