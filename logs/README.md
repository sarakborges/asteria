# Runtime logs

Godot writes one log per game execution here.

- `godot.log`: current/latest execution.
- Older executions are automatically rotated to timestamped files.
- Up to 200 execution logs are retained.
- `*.log` files are intentionally ignored by Git.

This logging works when running the game from the Godot editor with F5/F6 as well as normal desktop runs.
