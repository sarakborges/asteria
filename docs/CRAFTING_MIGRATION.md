# Inventory crafting migration

Reference: MineClone main, `data/crafting_recipes/rustic_hatchet.json` and `src/hud/crafting.rs`. World Recipes are intentionally excluded by request.

## Stage 1 — Core domain and transaction

- `CraftingRecipeDefinition` validates authored, pack-owned recipes (IDs, environment, unique item ingredients, quantities and output).
- `InventoryCraftingRuntime` resolves ingredients and plain, unambiguous result identities from the authoritative inventory catalog; unknown references fail during initialization.
- `PlayerInventory` is the only mutable owner of ingredient consumption and output insertion. A craft is simulated on an isolated 36-slot copy and committed with one revision only if everything fits. The cursor remains unchanged. Items are not conflated with blocks sharing the same ID.
- The first authored recipe matches MineClone's Rustic Hatchet: 2 pebbles + 3 sticks + 2 plant fibers => one hatchet.
- Regression tests cover definition validation, missing references, consumption, successful output, insufficient resources, capacity failure and no-op revisions.

## Next stage

Wire the recipe registry into Godot startup and its authoritative crafting actions/snapshots, then bind React's CraftingPanel and CurrentStationPanel through controllers and the store. Only mark it fully ported after playable crafting, inventory synchronization and WRY smoke tests; this Core stage alone does not make the panel interactive.
