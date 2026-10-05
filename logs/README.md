# Runtime logs

Each game execution gets its own Godot engine log in this directory.

When running from the Godot editor with F5/F6, the Asteria Run Logger editor plugin injects a unique `--log-file` argument immediately before launch.

Files use this format:

```text
run-YYYY-MM-DD_HH-MM-SS.log
```

The root `run.cmd` launcher uses the same directory and creates a unique timestamped file as well.

`*.log` files are intentionally ignored by Git.

A legacy `godot.log` may remain from the previous logging configuration and can be deleted.
