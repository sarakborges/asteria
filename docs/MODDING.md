# Asteria modding SDK (v1)

Asteria supports two separate kinds of extension:

- **Packs**: declarative Asteria-authored data and presentation overrides, no executable code.
- **Mods**: explicitly enabled trusted C# modules loaded into the game process. These have the same OS privileges as Asteria. Only enable DLLs from trusted sources.

The engine's authored default pack is compiled into the managed client assembly in release builds and is always loaded first. It is not installed as a loose editable `packs/default` directory. The authoritative source formats and vanilla definitions remain available in the Asteria source repository.

## Installing a pack

Place a pack in either **`<project>/packs/my_pack/`** (uncommitted local development pack) or **`<Asteria user data>/packs/my_pack/`** (installed pack; Godot `OS.GetUserDataDir()`). Both are discovered. If names collide, the user-data pack takes priority; `default` is always built in.

```text
packs/my_pack/
  pack.json
  data/
    blocks/
      dirt.json
  resources/
    textures/
      blocks/
        dirt.png
  ui/
    theme.json
```

`pack.json` (format version 1):

```json
{
  "format": 1,
  "id": "example:my_pack",
  "version": "1.0.0",
  "name": "My Pack",
  "description": "Custom content",
  "dependencies": []
}
```

Open **Packs** from the start screen and enable the pack. Folders with a missing or invalid `pack.json` are shown with an error and cannot be enabled. Choose **Data**, **Resources** (also includes UI appearance), or **Both**. Packs are layered above the built-in content in the visible priority order; the last enabled pack wins. Changes take effect on game restart, not on a loaded Sphere.

Data files are composed by namespaced definition ID, not filename. The overlay below entirely disables Dirt:

```json
{ "id": "asteria:dirt", "enabled": false }
```

Partial overrides recursively merge JSON objects; arrays replace as a whole. This override changes only one field:

```json
{ "id": "asteria:dirt", "mining": { "hardness": 3 } }
```

Disabling definitions with live references (biome palettes, recipes, structures, loot, or existing saves) can make content validation fail. The pack author must also override or disable those references. Content overrides never run arbitrary JavaScript or Godot scripts.

## Installing a C# mod

Under the same user data directory, create `mods/my_mod/` with a `mod.json`, the compiled DLL and its private dependency DLLs. Mods default to disabled and must be opted in from the **Mods** screen. Settings are user-editable JSON objects, passed into the mod at initialization; they apply after restart.

```json
{
  "format": 1,
  "id": "example:my_mod",
  "name": "My Mod",
  "version": "1.0.0",
  "assembly": "MyMod.dll",
  "entryType": "Example.MyMod",
  "dependencies": []
}
```

Compile the assembly for .NET 8, referencing the public `src/Asteria.ModAPI/Asteria.ModAPI.csproj` contract or its built `Asteria.ModAPI.dll`. The entry point must implement `IAsteriaMod` and expose a public parameterless constructor. Asteria owns and calls the `Initialize(IModContext)` lifecycle; it owns the settings snapshot and hooks.

The first v1 gameplay hook is `IGameplayHooks.RegisterBeforeBlockBreak`. Its callback receives immutable block ID and voxel coordinates; `false` rejects the operation before any layer, drop or voxel mutation. Callbacks run synchronously on the owner of the breaking operation, must return quickly, and must not depend on unordered iteration or wall-clock randomness. Handler exceptions reject that break and disable the offending mod's hooks for the current process.

**Extension status:** This is a working initial C# loader/API boundary, not a promise of arbitrary engine replacement. Other extension points (entity AI, worldgen providers, item-use hooks, UI screens) need explicit authoritative contracts before mods can use them. No example mods are shipped.

## Diagnostics and limitations

- Manifest IDs are namespaced and unique. Enabled mods and dependencies load in deterministic ID order with dependency traversal; cycles or missing dependencies are errors.
- Each mod has its own `AssemblyLoadContext` for dependencies. This is for resolution, **not sandboxing** or security isolation.
- Mods and packs are configured separately. Both require a restart; changing them in a live world is deliberately unsupported.
- Packs and mods never directly own Godot scenes, WebUI bridge logic, or authoritative voxel storage.
- A content-pack failure must be corrected before a world dependent on that content can load. Keep world backups when disabling authored IDs.
