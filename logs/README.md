# Runtime logs

Godot always writes the active game run to:

```text
logs/godot.log
```

When a F5/F6 run stops, the Asteria Run Logger editor plugin immediately copies that completed log to a unique archive:

```text
logs/run-YYYY-MM-DDTHH-MM-SS.log
```

So after each completed execution you keep one permanent file for that run. The next run reuses only `godot.log`; archived `run-*.log` files are never overwritten.

The root `run.cmd` launcher also writes directly to a unique timestamped log file.

`*.log` files are intentionally ignored by Git.
