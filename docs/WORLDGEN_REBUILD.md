# Asteria Worldgen Rebuild — implementation map

Reference contract: MineClone `world-systems-rebuild`,
`docs/design/world-systems-rebuild.md`.
Asteria keeps Godot as its presentation adapter and C# Core as its semantic owner.

## Status

| Capability | Asteria status |
| --- | --- |
| Deterministic seed and Sphere selection | Implemented |
| Biome primary/influences and bounded-area queries | Implemented |
| Cached formation assignment | Implemented, bounded per Sphere |
| Dedicated surface terrain queries and batch samples | Implemented |
| Deterministic material layers and patches | Implemented |
| Dedicated ground decoration query | Implemented for current authored content |
| Composed chunk materializer | Implemented for existing 2D surface rules |
| Authoritative 3D density/caves/overhangs/floating formations | Not yet implemented |
| Structure field and cross-chunk structure placements | Not yet implemented |
| Natural generated-fluid materialization | Not yet implemented |
| Full loading/persistence integration | Not yet implemented |
| Performance baselines | CLI available; real gameplay profile still required |

## Ownership and ordering

```text
BiomeField
  -> SurfaceTerrainField (base surface height)
    -> BiomeSurfaceMaterialField (surface layers/patches)
      -> SurfaceDecorationField (ground decoration)
        -> SurfaceChunkMaterializer (runtime block content)
```

`BiomeWorldGenerator` composes these immutable capabilities, and
`ChunkResidencyRuntime` continues to own async materialization and
activation. The separation is based on the MineClone rebuild, not its Bevy
or Rust runtime types. No semantic generation tile/region has been added.

The `SurfaceTerrainColumnCache` shares a 32x32 immutable biome/height
snapshot between surface selection and every vertical chunk request with
that X/Z coordinate. Maximum retained columns: 128 per Sphere. Formation
assignment memoization is separately bounded to 4096 seeds per Sphere.
Both caches are eviction-independent and are discarded with the Sphere.
Terrain-mesh biome tint uses a stitched 33x33 grid from four cached
32x32 world-space columns; it no longer resamples the full area with
another recursive biome pass. Adjacent chunk seams reuse identical
world-coordinate samples.

Material sampling resolves each finite layer's deterministic patch once
per materialized X/Z column, rather than performing patch search for every
solid Y voxel. Content semantics are unchanged.

## Reproducible benchmarking

From the repository root:

```sh
dotnet run -c Release --project tools/WorldgenBenchmark -- \
  --seed 181960897289965 --dimension asteria:overworld \
  --center-x 0 --center-z 0 --samples 16 --area-size 16 \
  --chunks 2 --output worldgen-benchmark.json
```

The CLI records cold/warm measurements for biome scalar/area queries,
surface scalar/area queries and chunk synthesis. Each metric uses its
own fresh generator instance, and both passes must produce the same
content digest. This is a query/CPU baseline, **not** an end-to-end
FPS or loading-time benchmark. Record the same seed and settings before
and after each subsequent architecture slice; never invent a fixed
performance budget without measuring representative hardware.

## Next implementation contracts

1. Extend `SurfaceTerrainField` with one final 3D occupancy/density
   field (including authored caves and floating formations), preserving
   scalar/volume equivalence and no negative world Y.
2. Make materials consume that final occupancy rather than inferring
   solidity from height alone; keep generated-fluid placement explicit.
3. Add authoritative deterministic `StructureField` placements and
   integrate intersecting portions into chunk synthesis, not per-chunk
   independent decisions.
4. Introduce generator-backed destination/spawn/locate capabilities and
   a shared loading state that separates required residency from
   background presentation, as described in MineClone Phases 8–11.
5. Measure cold/warm near/far paths, overlap/edge cases, materialization,
   mesh publication and frame-work budgets. Verify seam, order, and
   concurrent-query equivalence before expanding content.

Forbidden: duplicate semantic ownership, order-dependent generation,
unbounded caches, hidden runtime chunk generation from queries,
negative Y, or a separate water-planning subsystem.
