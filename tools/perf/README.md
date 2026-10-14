# Performance baseline comparison

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
