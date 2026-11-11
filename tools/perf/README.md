# Performance baseline comparison

## Deterministic world-generation A/B

```sh
dotnet run -c Release --project tools/WorldgenBenchmark -- \
  --seed 181960897289965 --dimension asteria:overworld \
  --samples 2 --area-size 4 --chunks 1 --output candidate.json

python tools/perf/compare_worldgen_ab.py \
  --baseline-dir path/to/baseline-results \
  --candidate-dir path/to/candidate-results \
  --repetitions 4 --expected-chunks 67 \
  --json-output worldgen-ab.json --report-output worldgen-ab.md
```

The A/B comparator requires matching seed, Sphere, center, spawn, initial
chunk selection and generated-content digests. It summarizes repeated processes
using medians and reports every materialization stage's elapsed time and
allocations, including per-chunk values in the JSON output. A stage present in
only one revision is shown as unavailable on the other side rather than being
treated as zero.

The report also combines `TerrainLayer` and `DirectionalFacePalettes` per
process before calculating the median. Candidate-face preparation is performed
inside `TerrainLayer`, while the baseline performs more of that work in the
directional-palette stage; the combined metric prevents this change in stage
ownership from looking like a false regression. Individual stage results remain
available to identify where the work is performed.

This is a headless Core world-generation benchmark. It does not measure Godot
mesh publication, collision creation, GPU work or frame pacing.

## Headless movement-route profile

Run the Core streaming route independently of the static world-generation
benchmark:

```sh
dotnet run -c Release --project tools/WorldStreamingBenchmark -- \
  --seed 181960897289965 --dimension asteria:overworld \
  --route-steps 8 --radius 4 --output streaming-route.json
```

The route tool drives the real Core `ChunkStreamingSelectionRuntime` and
`ChunkResidencyRuntime` with the production biome generator. It moves one
chunk along +X every eight simulated 60 Hz frames, runs selection and
materialization workers concurrently, reserves up to four ready presentations
per frame, processes fluid activation and block-physics wakeups, and retires
distant chunks. Output includes queue/worker peaks, local-neighborhood gaps,
selection latency, per-stage worker time, and a digest of every unique
activated chunk's block content. Repeated activations of the same coordinate
must reproduce the same content.

The A/B workflow builds this same route-harness source against each revision's
own `Asteria.Core` and executes four alternating processes per revision. It
rejects different route geometry or nondeterministic chunk content within a
revision. Per-chunk digests cover block IDs, fluid IDs/levels/source state and
attached Objects (ID, face, rotation and transform). Across revisions it
compares per-coordinate chunk digests: identical content permits timing
percentage deltas, while different content is reported with changed/missing
coordinate samples and all timing percentages are withheld.
The static 67-chunk fixture remains independently digest-gated. Admission
pressure is normalized across revisions and uses only physics wakeups, which
the route processes. Lighting and terrain-mesh queues are recorded but their
workers are outside this Core-only comparison. These are diagnostic
comparisons, not hard timing budgets.

The route treats presentation reservations as immediate and cost-free. It
does not execute Godot mesh construction, collision-shape creation, GPU upload,
or actual frame pacing; the result isolates Core selection, residency,
materialization, fluid activation and physics wakeup work.

## Gameplay-session comparison


This compares equal post-ready snapshot windows from two Asteria logs. It does
not substitute for a deterministic camera route or a GPU profiler.

1. Build a **fresh** Godot C# Debug assembly with `dotnet build Asteria.csproj -c Debug`.
2. Start the game, create a **new** Normal world in `asteria:overworld` with
   seed `16229071880563670726`, render distance **4**, and the same packs,
   graphics settings, mods, and machine as the earlier capture.
3. Follow the same movement pattern from spawn for at least 65 seconds after
   `world.loading.ready`. Save/exit and collect the newest `logs/run-*.log`.
4. Compare with the October 14 baseline:

```shell
python tools/perf/compare_logs.py logs/run-2026-10-14T22-05-22-0870910Z-p2044.log logs/<new-run>.log --seconds 60
```

The baseline log above is a user test artifact, **not committed** to the
repository: supply its path when available. Build identity is recorded as
`event=build.identity commit=<sha> mvid=<compiled-module-id>`; MVID
distinguishes locally modified binaries from an unmodified commit build.
An old log without build identity is reported as `unrecorded`.

The script refuses comparisons with different seeds, Spheres, render distance,
worker settings or insufficient capture duration. Identical configuration
alone cannot guarantee identical movement/graphics conditions.

Unit tests: `python -m unittest discover -s tools/perf -p 'test_*.py'`.
