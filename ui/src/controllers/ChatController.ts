import type { BridgeMessage } from "../bridge/godotBridge";
import type { ChatMessageView } from "../presentation/chatModels";
import type { ChatCompletionCatalog } from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

const SUPPORTED_COMMANDS = new Set(["/spawn", "/place", "/locate", "/warp", "/kill", "/modify"]);
const LOCAL_KEYS = new Set([
  "chat.command.usage", "chat.command.unknown",
  "chat.command.spawn.success", "chat.command.spawn.failed",
  "chat.command.spawn.unknownCreature", "chat.command.locate.found",
  "chat.command.locate.notFound", "chat.command.locate.unknownBiome",
  "chat.command.locate.biomeInactive", "chat.command.locate.unknownStructure",
  "chat.command.locate.searching", "chat.command.locate.playerUnavailable",
  "chat.command.warp.start", "chat.command.warp.success", "chat.command.warp.failed",
  "chat.command.kill.success", "chat.command.target.none",
  "chat.command.target.unavailable",
  "chat.command.failed", "chat.command.notImplemented",
  "chat.command.spectatorUnavailable", "chat.command.spawn.playerUnavailable",
  "chat.command.spawn.noSpace",
]);

export function createChatController(
  store: UiStore,
  post: (type: string, payload?: unknown) => void,
) {
  return {
    submit(text: string) {
      if (text.length > 256 || !store.getSnapshot().chat.open) return;
      post("ui.chat.submit", { text });
    },
    close() {
      if (store.getSnapshot().chat.open) post("ui.chat.close");
    },
    handleGodotMessage(message: BridgeMessage) {
      if (message.type !== "game.chat.state") return;
      const payload = asRecord(message.payload);
      if (!payload || typeof payload.open !== "boolean" ||
          typeof payload.visible !== "boolean" ||
          !Array.isArray(payload.history) || payload.history.length > 64 ||
          !Array.isArray(payload.commands) || payload.commands.length > 16) return;

      const entries = payload.history.map(readMessage);
      if (entries.some(value => value === null)) return;
      const commands = payload.commands;
      if (!commands.every(value => typeof value === "string" &&
          SUPPORTED_COMMANDS.has(value))) return;

      const catalog = readCatalog(payload.catalog);
      if (!catalog) return;

      store.update(state => ({
        ...state,
        chat: {
          open: payload.open as boolean,
          visible: payload.visible as boolean,
          history: entries as ChatMessageView[],
          commands: commands as string[],
          catalog,
        },
      }));
    },
  };
}

function readMessage(value: unknown): ChatMessageView | null {
  const entry = asRecord(value);
  if (!entry || typeof entry.id !== "string" ||
      entry.id.length > 24 || !/^\d+$/.test(entry.id) ||
      typeof entry.text !== "string" || entry.text.length > 1024 ||
      (entry.tone !== "normal" && entry.tone !== "error") ||
      (entry.localizationKey !== null && entry.localizationKey !== undefined &&
        (typeof entry.localizationKey !== "string" ||
          !LOCAL_KEYS.has(entry.localizationKey)))) return null;

  const parameters = entry.parameters == null ? {} : asRecord(entry.parameters);
  if (!parameters || Object.keys(parameters).length > 8 ||
      !Object.entries(parameters).every(([key, value]) =>
        /^[a-zA-Z]+$/.test(key) && typeof value === "string" && value.length <= 512))
    return null;

  return {
    parameters: parameters as Record<string, string>,
    id: entry.id,
    text: entry.text,
    tone: entry.tone,
    localizationKey: (entry.localizationKey ?? undefined) as ChatMessageView["localizationKey"],
  };
}

const MAX_CATALOG_ITEMS = 512;
const MAX_NAME_CHARS = 128;

function readStringArray(value: unknown, maximum = MAX_CATALOG_ITEMS): string[] | null {
  if (!Array.isArray(value) || value.length > maximum ||
      !value.every(entry => typeof entry === "string" &&
        entry.length > 0 && entry.length <= MAX_NAME_CHARS))
    return null;
  return value;
}

function readCatalog(value: unknown): ChatCompletionCatalog | null {
  const catalog = asRecord(value);
  if (!catalog) return null;
  const creatures = readStringArray(catalog.creatures);
  const biomes = readStringArray(catalog.biomes);
  const structures = readStringArray(catalog.structures);
  const dimensions = readStringArray(catalog.dimensions, 64);
  if (!creatures || !biomes || !structures || !dimensions ||
      !Array.isArray(catalog.variations) || catalog.variations.length > 256)
    return null;

  const variations: Record<string, readonly string[]> = {};
  for (const raw of catalog.variations) {
    const entry = asRecord(raw);
    if (!entry || typeof entry.id !== "string" ||
        !structures.includes(entry.id) ||
        Object.prototype.hasOwnProperty.call(variations, entry.id))
      return null;
    const ids = readStringArray(entry.ids, 128);
    if (!ids) return null;
    Object.defineProperty(variations, entry.id, {
      value: ids, enumerable: true, writable: false, configurable: false,
    });
  }

  const position = catalog.position === null ? null : asRecord(catalog.position);
  if (position === undefined || (position !== null &&
      !["x", "y", "z"].every(axis =>
        Number.isSafeInteger(position[axis]))))
    return null;
  return {
    creatures, biomes, structures, dimensions, variations,
    position: position === null ? null : {
      x: position.x as number, y: position.y as number, z: position.z as number,
    },
  };
}
