import type { ClientSettingsState, SettingsState } from "../state/uiState";

export type BindableControl = NonNullable<SettingsState["captureAction"]>;

export type ControlEntryView = {
  key: string;
  action: string;
  bindAction?: BindableControl;
};

export type ControlGroupView = {
  title: string;
  entries: readonly ControlEntryView[];
};

export function buildControlGroups(
  t: (key: string) => string,
  keybinds: ClientSettingsState["keybinds"] | undefined,
): readonly ControlGroupView[] {
  const jump = keybinds?.jump ?? "Space";
  const descend = keybinds?.descend ?? "ShiftLeft";
  const inventory = keybinds?.inventory ?? "KeyE";
  const chat = keybinds?.chat ?? "KeyT";
  const tool = keybinds?.toolAction ?? "KeyR";
  const drop = keybinds?.dropItem ?? "KeyQ";
  const perspective = keybinds?.changePerspective ?? "F5";

  return [
    {
      title: t("controls.category.movement"),
      entries: [
        { key: "WASD", action: t("controls.movement") },
        { key: "W ×2", action: t("controls.run") },
        { key: jump, action: t("settings.keybind.jump"), bindAction: "Jump" },
        { key: jump + " ×2", action: t("controls.toggleFlight") },
        { key: descend, action: t("settings.keybind.descend"), bindAction: "Descend" },
        { key: "MOUSE", action: t("controls.look") },
      ],
    },
    {
      title: t("controls.category.interface"),
      entries: [
        { key: "1–9", action: t("controls.hotbar") },
        { key: inventory, action: t("settings.keybind.inventory"), bindAction: "Inventory" },
        { key: "ESC", action: t("controls.pauseClose") },
        { key: perspective, action: t("settings.keybind.changePerspective"),
          bindAction: "ChangePerspective" },
      ],
    },
    {
      title: t("controls.category.actions"),
      entries: [
        { key: "LMB", action: t("controls.primaryAction") },
        { key: "RMB", action: t("controls.secondaryAction") },
        { key: "MMB", action: t("controls.pickBlock") },
        { key: tool, action: t("settings.keybind.toolAction"), bindAction: "ToolAction" },
        { key: drop, action: t("settings.keybind.dropItem"), bindAction: "DropItem" },
      ],
    },
    {
      // MineClone's fourth control group documents the existing chat
      // shortcuts. They are handled by the focused React input, not by
      // global Godot gameplay key routing or new keybind preferences.
      title: t("controls.category.chat"),
      entries: [
        { key: chat, action: t("settings.keybind.chat"), bindAction: "Chat" },
        { key: "ENTER", action: t("controls.sendChat") },
        { key: "↑ / ↓", action: t("controls.chatHistory") },
        { key: "TAB", action: t("controls.autocomplete") },
      ],
    },
  ];
}
