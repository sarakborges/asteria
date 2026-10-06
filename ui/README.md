# Asteria WebUI

The WebUI is a framework-light HTML/CSS/TypeScript frontend embedded in Godot through Godot WRY.

## Architecture: Atomic Design

All gameplay UI follows Atomic Design and lives under `src/components/`:

```text
components/
  atoms/       indivisible UI primitives
  molecules/   small compositions of atoms
  organisms/   domain-level UI sections
  templates/   layout and slot composition
  pages/       complete game UI surfaces
```

The hierarchy is intentional:

- **Atoms** own primitive presentation only. They do not know about Godot messages or gameplay state.
- **Molecules** compose atoms into reusable UI concepts.
- **Organisms** compose atoms/molecules into domain sections such as the runtime status card.
- **Templates** define layout and slots without transport/business logic.
- **Pages** assemble complete UI surfaces from templates and lower-level components.

Godot transport is not a component concern:

- `src/bridge/` owns WRY/IPC message transport and parsing.
- `src/controllers/` binds bridge messages and user events to page/component state.
- `src/theme/` validates and applies declarative UI theme tokens received from the selected Asteria pack.
- `src/main.ts` is composition/bootstrap only.

Do not put bridge calls, global event listeners, or gameplay message parsing inside atoms, molecules, organisms, or templates.

## Storybook

Storybook is the canonical isolated development surface for reusable WebUI components.

Every reusable atom, molecule, organism, template, or page must have a colocated `*.stories.ts` file covering its meaningful visual states.

Run:

```bash
npm install
npm run storybook
```

Build the static Storybook:

```bash
npm run build-storybook
```

Storybook uses `@storybook/html-vite`, matching the framework-light WebUI. The repository CI builds both the embedded Vite bundle and Storybook, so stories are part of the validation contract.

## Embedded build

```bash
npm run dev
npm run build
```

The production bundle is written to `ui/dist/` and loaded by Godot WRY through `res://ui/dist/index.html`.

The trusted WebUI application code stays here. Visual customization comes from `packs/{selected}/ui/theme.json` (and future `ui/assets/`) through the Godot bridge; packs do not replace HTML/TypeScript or inject executable UI code.
