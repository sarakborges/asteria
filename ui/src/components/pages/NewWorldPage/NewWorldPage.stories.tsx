import { useMemo } from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { App, type AppActions } from "../../../App";
import { createInitialUiState } from "../../../state/uiState";
import { createUiStore } from "../../../state/uiStore";
import { createUiNavigationController } from "../../../controllers/UiNavigationController";
import { NewWorldPage } from "./NewWorldPage";

/**
 * Full pre-world flow story: exercises the same App + UiStore + navigation
 * composition as the embedded game, without emulating gameplay or bridge data.
 */
function PreWorldFlow() {
  const { store, actions } = useMemo(() => {
    const initial = createInitialUiState(false);
    const store = createUiStore({
      ...initial,
      worldCatalog: { status: "ready", worlds: [], folderError: false },
      worldCreation: { ...initial.worldCreation, pending: false,
        seed: "181960897289965" },
    });
    const navigation = createUiNavigationController(store, () => undefined);
    const noop = () => undefined;
    const actions: AppActions = {
      ping: noop,
      createWorld: noop,
      randomizeWorld: noop,
      openWorldSelection: navigation.openWorldSelection,
      openSavesFolder: noop,
      openWorldCreation: navigation.openWorldCreation,
      backToStart: navigation.backToStart,
      exitGame: noop,
      dismissToast: noop,
      resumeGame: noop,
      openGameSettings: noop,
      openWorldSettings: noop,
      openControls: noop,
      backFromOverlay: noop,
      setRenderDistance: noop,
      setTargetPosition: noop,
      setHideHints: noop,
      setGameplayHint: noop,
      setWorldTicks: noop,
      setSpawnCreatures: noop,
      setGameMode: noop,
      beginKeyCapture: noop,
      cancelKeyCapture: noop,
      closeBrushPalette: noop,
      selectBrushDye: noop,
      closeInventory: noop,
      closeChat: noop,
      submitChat: noop,
      clickInventorySlot: noop,
      sortInventory: noop,
      discardInventoryCursor: noop,
      pickCreativeBlock: noop,
    };
    return { store, actions };
  }, []);

  return <App embedded={false} store={store} actions={actions} />;
}

const meta = {
  title: "Pages/NewWorldPage",
  component: NewWorldPage,
  parameters: { layout: "fullscreen" },
  args: {
    state: {
      visible: true,
      seed: "181960897289965",
      name: "New World",
      mode: "Survival",
      ticksPerSecond: "40",
      spawnCreatures: true,
      pending: false,
      generating: false,
      errorKey: null,
    },
    onBack: () => undefined,
    onMainMenu: () => undefined,
    onCreate: () => undefined,
    onRandomize: () => undefined,
  },
} satisfies Meta<typeof NewWorldPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Ready: Story = {};
export const ValidationError: Story = {
  args: { state: { ...meta.args.state, seed: "18446744073709551615",
    errorKey: "newWorld.error.invalidSeed" } },
};
export const Generating: Story = {
  args: { state: { ...meta.args.state, generating: true, pending: true } },
};
export const Pending: Story = {
  args: { state: { ...meta.args.state, pending: true } },
};
export const Creative: Story = {
  args: { state: { ...meta.args.state, name: "Creative World",
    seed: "420", mode: "Creative" } },
};
export const NoCreatureSpawning: Story = {
  args: { state: { ...meta.args.state, seed: "419",
    name: "Peaceful World", spawnCreatures: false } },
};
export const RealNavigation: Story = {
  render: () => <PreWorldFlow />,
};
