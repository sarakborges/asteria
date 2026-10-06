const themeTokenMap = {
  colorText: "--ui-color-text",
  colorMuted: "--ui-color-muted",
  colorSuccess: "--ui-color-success",
  colorWarning: "--ui-color-warning",
  colorBorder: "--ui-color-border",
  colorBorderStrong: "--ui-color-border-strong",
  colorPanel: "--ui-color-panel",
  colorControl: "--ui-color-control",
  colorControlHover: "--ui-color-control-hover",
  radiusSm: "--ui-radius-sm",
  radiusMd: "--ui-radius-md",
  space1: "--ui-space-1",
  space2: "--ui-space-2",
  space3: "--ui-space-3",
  space4: "--ui-space-4",
  space5: "--ui-space-5",
  fontFamily: "--ui-font-family",
  fontSizeDetail: "--ui-font-size-detail",
  fontSizeTitle: "--ui-font-size-title",
  letterSpacingDetail: "--ui-letter-spacing-detail",
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
