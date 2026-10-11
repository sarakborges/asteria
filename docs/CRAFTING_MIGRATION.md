# Inventory crafting migration

Reference: MineClone main, `data/crafting_recipes/rustic_hatchet.json` and `src/hud/crafting.rs`. World Recipes are intentionally excluded by request.

## Stage 1 — Core domain and transaction

- `CraftingRecipeDefinition` validates authored, pack-owned recipes (IDs, environment, unique item ingredients, quantities and output).
- `InventoryCraftingRuntime` resolves ingredients and plain, unambiguous result identities from the authoritative inventory catalog; unknown references fail during initialization.
- `PlayerInventory` is the only mutable owner of ingredient consumption and output insertion. A craft is simulated on an isolated 36-slot copy and committed with one revision only if everything fits. The cursor remains unchanged. Items are not conflated with blocks sharing the same ID.
- The first authored recipe matches MineClone's Rustic Hatchet: 2 pebbles + 3 sticks + 2 plant fibers => one hatchet.
- Regression tests cover definition validation, missing references, consumption, successful output, insufficient resources, capacity failure and no-op revisions.

## Reference comparison and verification (2026-10-11)

- Inspected MineClone `main` and `world-systems-rebuild`: both contain one authored inventory recipe (Rustic Hatchet) and the *Inventory / Personal crafting* station panel. Neither branch currently authors additional crafting station recipes or station runtime owners. **Do not invent dedicated workbenches, furnaces, or forges and call them a port.** World Recipes remain expressly outside this migration.
- The Asteria survival inventory now keeps the personal *Current Station* panel visible even when there are no recipes. Its image uses the authored `crafting_materials` category icon from the selected-pack catalog whenever available rather than an artificial content ID preview.
- `InventoryCraftingRuntime.Availability` returns read-only `UnknownRecipe`, `MissingIngredients`, `InventoryFull`, or `Crafted` outcomes using the same Core preflight as the real transaction. Godot publishes the evaluated unavailable reason in the immutable recipe view and React presents appropriate localized feedback. A craft blocked by output capacity no longer claims that ingredients are missing. Last-craft feedback is shown only for the currently selected recipe.
- Core regression tests cover output-capacity preflight, no-op revisions, ingredient requirements, and successful crafting; Playwright stories cover no-recipe personal station and inventory-full feedback.
- **Still requires native Godot/WRY QA:** perform Rustic Hatchet craft end-to-end, verify close/reopen, response/focus, icons, and rendering under selected packs with absent `crafting_materials` category imagery.

## Stage 2 — Godot ↔ WebUI integration

- Godot startup reads optional `crafting_recipes` from the selected pack and resolves it through the validated Core runtime.
- `game.inventory.state` includes Core-authored recipes with live ingredient counts and authoritative craftability, published only when inventory state is sent.
- `ui.inventory.craft` is a semantic UI action accepted only while the inventory is open and the world is ready. Core owns the operation; a successful craft republishes hotbar and inventory state.
- `game.inventory.crafting_result` supplies structured result codes and localized UI feedback. The UI selects recipes locally but cannot mutate quantities.
- Storybook covers the populated crafting panel inside the inventory and successful-result feedback. WRY input/click smoke testing on a local Godot build is still required.
