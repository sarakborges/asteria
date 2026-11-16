# Runtime diagnostics

Every **game process** now creates its own structured, timestamped log as soon as
`Main._Ready()` begins:

```text
logs/run-YYYY-MM-DDTHH-mm-ss-fffffffZ-p<PID>.log
```

The location is relative to the Godot project (`res://logs/`). When this
directory is not writable (for example, in a packaged game), the logger falls
back to Godot's `user://logs/` directory and prints the absolute path to the
Godot console. Each file uses create-new semantics: a new run **never**
overwrites an old one, even with overlapping processes.

### What's captured automatically

- Session start/stop, process ID, runtime/platform, startup and content counts.
- World seed, dimension and derived seed, loading/transition milestones,
  streaming selection changes and worker failures (with full exception text).
- Every **2 seconds**: resident/pending/in-flight/presented chunks,
  generation count/latency/slowest chunk, archive restores, terrain and fluid
  mesh performance/staleness, lighting, fluid simulation, average/maximum
  frame time and frame stalls.
- An additional final snapshot on graceful shutdown, including runs that
  last less than two seconds.
- Unhandled managed exceptions and unobserved background-task exceptions,
  formatted on one searchable event line (embedded newlines are escaped).

Each event contains a UTC ISO-8601 timestamp, severity, event name and thread
ID. Normal per-voxel/per-frame debug printing remains opt-in: telemetry is
aggregated to avoid adding substantial work to world streaming.

**To debug a run:** open the newest `logs/run-*.log` file, search for
`[ERROR]`, `[FATAL]`, `world.phase`, `world.snapshot` or
`generation_ms_max`. A missing `session.stop` marker suggests an abrupt
process exit; an engine/native crash may need the Godot log as well.

### Godot engine output is separate

Godot's own log remains at `logs/godot.log` (the active file), and
`run.cmd` captures the full Godot console output as
`logs/godot-run-*.log`. These capture engine/renderer messages that cannot
be intercepted reliably by a C# observer.

The old editor plugin that archived `godot.log` on F5/F6 was removed because
its editor-side lifecycle could create misleading or incomplete run archives.
Use the game-owned `run-*.log` for runtime diagnostics; `godot.log` is only
the secondary native-engine log.

`*.log` files are intentionally excluded from Git.
