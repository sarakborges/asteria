# Inventory crafting migration

Reference: MineClone main, `data/crafting_recipes/rustic_hatchet.json` and `src/hud/crafting.rs`. World Recipes are intentionally excluded by request.

## Stage 1 — Core domain and transaction

- `CraftingRecipeDefinition` validates authored, pack-owned recipes (IDs, environment, unique item ingredients, quantities and output).
- `InventoryCraftingRuntime` resolves ingredients and plain, unambiguous result identities from the authoritative inventory catalog; unknown references fail during initialization.
- `PlayerInventory` is the only mutable owner of ingredient consumption and output insertion. A craft is simulated on an isolated 36-slot copy and committed with one revision only if everything fits. The cursor remains unchanged. Items are not conflated with blocks sharing the same ID.
- The first authored recipe matches MineClone's Rustic Hatchet: 2 pebbles + 3 sticks + 2 plant fibers => one hatchet.
- Regression tests cover definition validation, missing references, consumption, successful output, insufficient resources, capacity failure and no-op revisions.

## Verification still required

Stage 2 below **already implements** the Godot bridge, live recipe snapshots and React crafting actions. Do not repeat its former "Next stage" instructions. The remaining work is to test a real inventory click/craft operation and feedback inside Godot/WRY, compare recipe/station presentation with MineClone, and add authored recipes as needed.

## Stage 2 — Godot ↔ WebUI integration

- Godot startup reads optional `crafting_recipes` from the selected pack and resolves it through the validated Core runtime.
- `game.inventory.state` includes Core-authored recipes with live ingredient counts and authoritative craftability, published only when inventory state is sent.
- `ui.inventory.craft` is a semantic UI action accepted only while the inventory is open and the world is ready. Core owns the operation; a successful craft republishes hotbar and inventory state.
- `game.inventory.crafting_result` supplies structured result codes and localized UI feedback. The UI selects recipes locally but cannot mutate quantities.
- Storybook covers the populated crafting panel inside the inventory and successful-result feedback. WRY input/click smoke testing on a local Godot build is still required.
