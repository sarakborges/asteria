import { useMemo } from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { App, type AppActions } from "../../../App";
import { createInitialUiState } from "../../../state/uiState";
import { createUiStore } from "../../../state/uiStore";
import { createUiNavigationController } from "../../../controllers/UiNavigationController";
import { NewWorldPage } from "./NewWorldPage";

const defaultGeneration = {
  mode: "Normal" as const, spawnBiome: null, biomeSizeTenths: 10,
  spawnStructures: true, singleBiome: false,
  spawnCaves: true, spawnOceans: true,
};
const biomes = [
  { id: "asteria:overworld/plains", label: "Plains" },
  { id: "asteria:overworld/swamp", label: "Swamp" },
];

/**
 * Full pre-world flow story: exercises the same App + UiStore + navigation
 * composition as the embedded game, without emulating gameplay or bridge data.
 */
function PreWorldFlow() {
  const { store, actions } = useMemo(() => {
    const initial = createInitialUiState(false);
    const store = createUiStore({
      ...initial,
      worldCatalog: { ...initial.worldCatalog, status: "ready", worlds: [], folderError: false },
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
      loadWorld: noop,
      deleteWorld: noop,
      saveWorld: noop,
      leaveWorld: noop,
      closeStorageBox: noop,
      clickStorageBoxSlot: noop,
      sortStorageBox: noop,
      clickEquipmentSlot: noop,
      craftInventoryRecipe: noop,
      rotateCharacterPortrait: noop,
      openWorldCreation: navigation.openWorldCreation,
      backToStart: navigation.backToStart,
      exitGame: noop,
      dismissToast: noop,
      resumeGame: noop,
      respawn: noop,
      openGameSettings: noop,
      openWorldSettings: noop,
      openControls: noop,
      backFromOverlay: noop,
      escapeNavigation: navigation.escape,
      setRenderDistance: noop,
      setTargetPosition: noop,
      setHideHints: noop,
      setGameplayHint: noop,
      setWorldTicks: noop,
      setSpawnCreatures: noop,
      setKeepInventory: noop,
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

/** Actual App/UiStore modal navigation, without a fake DOM-only shell. */
function InGameFlow({ initialOverlay }: { initialOverlay: "pause" | "inventory" }) {
  const { store, actions } = useMemo(() => {
    const initial = createInitialUiState(false);
    const store = createUiStore({
      ...initial,
      worldCreation: { ...initial.worldCreation, visible: false },
      navigation: { ...initial.navigation, preWorldScreen: "starting", overlay: initialOverlay },
      inventory: { ...initial.inventory, open: initialOverlay === "inventory" },
      settings: {
        ...initial.settings,
        world: { name: "New World", mode: "Survival",
          ticksPerSecond: 40, spawnCreatures: true, keepInventory: true },
        client: {
          renderDistanceChunks: 8,
          hud: { hideHints: false, targetBlockPosition: "Center",
            hints: { rotateBlock: true, breakOrPlaceBlock: true } },
          keybinds: { jump: "Space", descend: "ShiftLeft",
            inventory: "KeyE", chat: "KeyT", toolAction: "KeyR",
            dropItem: "KeyQ", changePerspective: "F5" },
        },
      },
    });
    const post = (type: string) => {
      if (type === "ui.game.resume") store.update(state => ({
        ...state, navigation: { ...state.navigation, overlay: "none" },
      }));
    };
    const navigation = createUiNavigationController(store, post);
    const noop = () => undefined;
    const actions: AppActions = {
      ping: noop,
      createWorld: noop,
      randomizeWorld: noop,
      openWorldSelection: navigation.openWorldSelection,
      openSavesFolder: noop,
      loadWorld: noop,
      deleteWorld: noop,
      saveWorld: noop,
      leaveWorld: noop,
      closeStorageBox: noop,
      clickStorageBoxSlot: noop,
      sortStorageBox: noop,
      clickEquipmentSlot: noop,
      craftInventoryRecipe: noop,
      rotateCharacterPortrait: noop,
      openWorldCreation: navigation.openWorldCreation,
      backToStart: navigation.backToStart,
      exitGame: noop,
      dismissToast: noop,
      resumeGame: () => post("ui.game.resume"),
      respawn: noop,
      openGameSettings: navigation.openGameSettings,
      openWorldSettings: navigation.openWorldSettings,
      openControls: navigation.openControls,
      backFromOverlay: navigation.backFromOverlay,
      escapeNavigation: navigation.escape,
      setRenderDistance: noop,
      setTargetPosition: noop,
      setHideHints: noop,
      setGameplayHint: noop,
      setWorldTicks: noop,
      setSpawnCreatures: noop,
      setKeepInventory: noop,
      setGameMode: noop,
      beginKeyCapture: noop,
      cancelKeyCapture: noop,
      closeBrushPalette: noop,
      selectBrushDye: noop,
      closeInventory: () => store.update(state => ({
        ...state,
        inventory: { ...state.inventory, open: false },
        navigation: { ...state.navigation, overlay: "none" },
      })),
      closeChat: noop,
      submitChat: noop,
      clickInventorySlot: noop,
      sortInventory: noop,
      discardInventoryCursor: noop,
      pickCreativeBlock: noop,
    };
    return { store, actions };
  }, [initialOverlay]);

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
      keepInventory: true,
      generation: defaultGeneration,
      spawnBiomes: biomes,
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

export const VoidWorld: Story = {
  args: { state: { ...meta.args.state, generation: {
    ...defaultGeneration, mode: "Void", spawnCaves: false, spawnOceans: false,
  } } },
};
export const SingleBiome: Story = {
  args: { state: { ...meta.args.state, generation: {
    ...defaultGeneration, singleBiome: true,
    spawnBiome: "asteria:overworld/plains",
  } } },
};

export const ModalNavigation: Story = {
  render: () => <InGameFlow initialOverlay="pause" />,
};
export const InventoryNavigation: Story = {
  render: () => <InGameFlow initialOverlay="inventory" />,
};
