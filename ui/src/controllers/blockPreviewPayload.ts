import type { BlockPreviewState } from "../state/uiState";
import { asRecord } from "./messagePayload";

const MAX_ICON_URL_LENGTH = 180_000;
const PNG_DATA_URL = /^data:image\/png;base64,[A-Za-z0-9+/]+={0,2}$/;

export function readPngIconUrl(value: unknown): value is string {
  return typeof value === "string" &&
    value.length <= MAX_ICON_URL_LENGTH && PNG_DATA_URL.test(value);
}

/** Shared contract for catalog, hotbar, individual stacks and target HUD. */
export function readBlockPreview(raw: unknown): BlockPreviewState | null {
  const data = asRecord(raw);
  if (!data ||
      (data.kind !== "cube" && data.kind !== "sprite") ||
      !readPngIconUrl(data.front) ||
      !(data.top === null || readPngIconUrl(data.top)) ||
      !(data.right === null || readPngIconUrl(data.right)))
    return null;
  return {
    kind: data.kind, front: data.front,
    top: data.top, right: data.right,
  };
}
