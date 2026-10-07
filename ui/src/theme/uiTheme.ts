const themeTokenMap = {
  colorScreenBackground: "--ui-color-screen-background",
  colorFrostedSurface: "--ui-color-frosted-surface",
  colorFrostedHighlight: "--ui-color-frosted-highlight",
  colorCosmicGlow: "--ui-color-cosmic-glow",
  colorHudSurface: "--ui-color-hud-surface",
  colorSurfaceElevated: "--ui-color-surface-elevated",
  colorSurfaceInset: "--ui-color-surface-inset",

  colorText: "--ui-color-text",
  colorMuted: "--ui-color-muted",
  colorSubtle: "--ui-color-subtle",

  colorSuccess: "--ui-color-success",
  colorWarning: "--ui-color-warning",
  colorDanger: "--ui-color-danger",
  colorDangerHover: "--ui-color-danger-hover",
  colorDangerPressed: "--ui-color-danger-pressed",
  colorHealth: "--ui-color-health",
  colorStamina: "--ui-color-stamina",

  colorAccent: "--ui-color-accent",
  colorAccentHover: "--ui-color-accent-hover",
  colorAccentPressed: "--ui-color-accent-pressed",
  colorAccentStrong: "--ui-color-accent-strong",
  colorAccentSoft: "--ui-color-accent-soft",
  colorAccentGlow: "--ui-color-accent-glow",

  colorBorder: "--ui-color-border",
  colorBorderStrong: "--ui-color-border-strong",
  colorBorderFocus: "--ui-color-border-focus",

  colorSliderTrack: "--ui-color-slider-track",
  colorSliderThumb: "--ui-color-slider-thumb",

  colorCrosshair: "--ui-color-crosshair",
  colorShadow: "--ui-color-shadow",
  colorMeterTrack: "--ui-color-meter-track",
  colorSlot: "--ui-color-slot",
  colorSlotSelected: "--ui-color-slot-selected",
  colorHotbar: "--ui-color-hotbar",
  colorPanel: "--ui-color-panel",
  colorControl: "--ui-color-control",
  colorControlHover: "--ui-color-control-hover",

  controlBorderWidth: "--ui-control-border-width",
  controlHeightCompact: "--ui-control-height-compact",
  controlHeightMenu: "--ui-control-height-menu",
  menuButtonWidth: "--ui-menu-button-width",

  screenContentWidth: "--ui-screen-content-width",
  screenHeaderHeight: "--ui-screen-header-height",
  screenFooterHeight: "--ui-screen-footer-height",
  screenActionGap: "--ui-screen-action-gap",
  screenBodyPaddingX: "--ui-screen-body-padding-x",
  screenBodyPaddingY: "--ui-screen-body-padding-y",

  settingsGroupGap: "--ui-settings-group-gap",
  settingsSettingGap: "--ui-settings-setting-gap",
  surfacePadding: "--ui-surface-padding",
  inputPaddingX: "--ui-input-padding-x",

  inventorySlotSize: "--ui-inventory-slot-size",
  inventorySlotGap: "--ui-inventory-slot-gap",
  inventorySectionGap: "--ui-inventory-section-gap",
  inventoryPanelGap: "--ui-inventory-panel-gap",
  inventoryPanelPadding: "--ui-inventory-panel-padding",
  inventoryPanelBorderWidth: "--ui-inventory-panel-border-width",
  inventoryCharacterWidth: "--ui-inventory-character-width",
  inventoryCraftingWidth: "--ui-inventory-crafting-width",
  inventoryStationWidth: "--ui-inventory-station-width",
  inventoryCategoryWidth: "--ui-inventory-category-width",
  inventorySearchWidth: "--ui-inventory-search-width",
  inventorySearchHeight: "--ui-inventory-search-height",

  radiusSm: "--ui-radius-sm",
  radiusMd: "--ui-radius-md",
  space1: "--ui-space-1",
  space2: "--ui-space-2",
  space3: "--ui-space-3",
  space4: "--ui-space-4",
  space5: "--ui-space-5",

  fontFamily: "--ui-font-family",
  fontSizeCaption: "--ui-font-size-caption",
  fontSizeDetail: "--ui-font-size-detail",
  fontSizeBody: "--ui-font-size-body",
  fontSizeButton: "--ui-font-size-button",
  fontSizeSettingTitle: "--ui-font-size-setting-title",
  fontSizeTitle: "--ui-font-size-title",
  fontSizeHeading: "--ui-font-size-heading",
  fontSizeScreenTitle: "--ui-font-size-screen-title",
  letterSpacingDetail: "--ui-letter-spacing-detail",
  hotbarSlotSize: "--ui-hotbar-slot-size",
} as const;

type ThemeToken = keyof typeof themeTokenMap;

export function applyUiTheme(payload: unknown): void {
  if (!isRecord(payload)) return;

  for (const [token, cssVariable] of Object.entries(themeTokenMap) as [
    ThemeToken,
    string,
  ][]) {
    const value = payload[token];
    if (typeof value !== "string") continue;

    const normalized = value.trim();
    if (normalized.length === 0 || normalized.length > 160) continue;

    document.documentElement.style.setProperty(
      cssVariable,
      normalized,
    );
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
