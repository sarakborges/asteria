import type { BridgeMessage } from "../bridge/godotBridge";
import type { AddonsState, PackAddon, ModAddon } from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

type Scope = "Data" | "Resources" | "Both";

function packsFrom(raw: unknown): PackAddon[] {
  if (!Array.isArray(raw)) return [];
  return raw.flatMap(value => {
    const item = asRecord(value);
    if (!item || typeof item.name !== "string" ||
        typeof item.id !== "string" || typeof item.title !== "string" ||
        typeof item.version !== "string" || typeof item.enabled !== "boolean" ||
        typeof item.order !== "number") return [];
    return [{
      name: item.name, id: item.id, title: item.title, version: item.version,
      description: typeof item.description === "string" ? item.description : "",
      enabled: item.enabled, order: item.order,
      scope: item.scope === "Data" || item.scope === "Resources"
        ? item.scope : "Both",
    }];
  });
}
function modsFrom(raw: unknown): ModAddon[] {
  if (!Array.isArray(raw)) return [];
  return raw.flatMap(value => {
    const item = asRecord(value);
    if (!item || typeof item.id !== "string" ||
        typeof item.name !== "string" || typeof item.version !== "string" ||
        typeof item.enabled !== "boolean" || typeof item.settings !== "string")
      return [];
    return [{
      id: item.id, name: item.name, version: item.version,
      enabled: item.enabled, settings: item.settings,
      status: typeof item.status === "string" ? item.status : "disabled",
      error: typeof item.error === "string" ? item.error : null,
    }];
  });
}

export function createAddonsController(
  store: UiStore, post: (type: string, payload?: unknown) => void,
) {
  const update = (patch: Partial<AddonsState>) =>
    store.update(state => ({ ...state, addons: { ...state.addons, ...patch } }));
  const sendPacks = (packs: PackAddon[]) =>
    post("ui.packs.configure", {
      packs: packs.filter(pack => pack.enabled)
        .sort((a, b) => a.order - b.order || a.name.localeCompare(b.name))
        .map(pack => ({ name: pack.name, domains: pack.scope })),
    });
  return {
    refresh() { post("ui.packs.refresh"); post("ui.mods.refresh"); },
    setPackScope(name: string, scope: Scope | null) {
      const packs = store.getSnapshot().addons.packs;
      const highest = Math.max(-1, ...packs.filter(pack => pack.enabled).map(pack => pack.order));
      sendPacks(packs.map(pack => pack.name !== name ? pack : {
        ...pack, enabled: scope !== null,
        scope: scope ?? pack.scope,
        order: scope && !pack.enabled ? highest + 1 : pack.order,
      }));
    },
    movePack(name: string, direction: -1 | 1) {
      const packs = store.getSnapshot().addons.packs;
      const ordered = packs.filter(pack => pack.enabled)
        .sort((a, b) => a.order - b.order || a.name.localeCompare(b.name));
      const index = ordered.findIndex(pack => pack.name === name);
      if (index < 0 || index + direction < 0 || index + direction >= ordered.length) return;
      const next = index + direction;
      [ordered[index], ordered[next]] = [ordered[next], ordered[index]];
      sendPacks(ordered.map((pack, order) => ({ ...pack, order })));
    },
    configureMod(id: string, enabled: boolean, settings: string) {
      try {
        const parsed: unknown = JSON.parse(settings);
        if (!parsed || typeof parsed !== "object" || Array.isArray(parsed))
          throw new Error("Settings must be a JSON object.");
        update({ error: null });
        post("ui.mods.configure", { id, enabled, settings: parsed });
      } catch (error) { update({ error: String(error) }); }
    },
    handleGodotMessage(message: BridgeMessage) {
      const payload = asRecord(message.payload);
      switch (message.type) {
        case "game.packs.catalog":
          update({ packs: packsFrom(payload?.packs),
            packRestartRequired: payload?.restartRequired === true, error: null });
          break;
        case "game.mods.catalog":
          update({ mods: modsFrom(payload?.mods),
            modRestartRequired: payload?.restartRequired === true, error: null });
          break;
        case "game.packs.error":
        case "game.mods.error":
          update({ error: typeof payload?.message === "string"
            ? payload.message : "Configuration failed." });
          break;
      }
    },
  };
}
