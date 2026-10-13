import { expect, test } from "@playwright/test";
import { createWorldCreationController } from "../../src/controllers/WorldCreationController";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiStore } from "../../src/state/uiStore";

function fixture() {
  const state = createInitialUiState(false);
  const store = createUiStore({
    ...state,
    worldCreation: { ...state.worldCreation, seed: "123", pending: false },
  });
  const sent: { type: string; payload?: unknown }[] = [];
  const controller = createWorldCreationController(store, (type, payload) => {
    sent.push({ type, payload });
  });
  const request = {
    seed: "123", name: "Asteria", mode: "Survival" as const,
    ticksPerSecond: "40", spawnCreatures: true, keepInventory: false,
    creatureAttackMode: "Default" as const,
    generation: {
      mode: "Flat" as const, spawnBiome: null, biomeSizeTenths: 10,
      spawnStructures: false, singleBiome: false,
      spawnCaves: false, spawnOceans: true,
    },
  };
  return { store, sent, controller, request };
}

test("Create World is single-flight across footer, Enter and randomizer", () => {
  const { store, sent, controller, request } = fixture();
  controller.createWorld(request);
  controller.createWorld(request);
  controller.randomizeWorld();
  expect(sent).toEqual([{
    type: "ui.world.create",
    payload: { ...request, ticksPerSecond: 40 },
  }]);
  expect(store.getSnapshot().worldCreation.pending).toBe(true);
  controller.handleGodotMessage({
    type: "game.world_creation.started", payload: { seed: "123" },
  });
  controller.createWorld(request);
  expect(sent).toHaveLength(1);
});

test("native validation failure unlocks retry without duplicate submissions", () => {
  const { store, sent, controller, request } = fixture();
  controller.createWorld(request);
  controller.handleGodotMessage({
    type: "game.world_creation.error",
    payload: { key: "newWorld.error.invalidName" },
  });
  expect(store.getSnapshot().worldCreation.pending).toBe(false);
  expect(store.getSnapshot().worldCreation.errorKey).toBe("newWorld.error.invalidName");
  controller.createWorld({ ...request, name: "Valid World" });
  expect(sent).toHaveLength(2);
  expect(sent[1].payload).toMatchObject({ name: "Valid World" });
  expect(store.getSnapshot().worldCreation.errorKey).toBeNull();
});

test("Randomize is single-flight and new seed unlocks Create", () => {
  const { store, sent, controller, request } = fixture();
  controller.randomizeWorld();
  controller.randomizeWorld();
  controller.createWorld(request);
  expect(sent).toEqual([{ type: "ui.world.randomize", payload: undefined }]);
  controller.handleGodotMessage({
    type: "game.world_creation",
    payload: { seed: "18446744073709551615", spawnBiomes: [] },
  });
  expect(store.getSnapshot().worldCreation.pending).toBe(false);
  expect(store.getSnapshot().worldCreation.seed).toBe("18446744073709551615");
  controller.createWorld({ ...request, seed: "18446744073709551615" });
  expect(sent).toHaveLength(2);
  expect(sent[1].payload).toMatchObject({
    seed: "18446744073709551615",
  });
});
