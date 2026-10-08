import type { BridgeMessage } from "../bridge/godotBridge";
import type { ChatMessageView } from "../presentation/chatModels";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

const LOCAL_KEYS = new Set([
  "chat.local.help", "chat.local.position", "chat.local.time", "chat.local.unknown",
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
          /^\/[a-z]+$/.test(value) && value.length <= 32)) return;

      store.update(state => ({
        ...state,
        chat: {
          open: payload.open as boolean,
          visible: payload.visible as boolean,
          history: entries as ChatMessageView[],
          commands: commands as string[],
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

  return {
    id: entry.id,
    text: entry.text,
    tone: entry.tone,
    localizationKey: (entry.localizationKey ?? undefined) as ChatMessageView["localizationKey"],
  };
}
