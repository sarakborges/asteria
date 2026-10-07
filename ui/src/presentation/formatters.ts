import type { VitalValue } from "../state/uiState";

export function displayContentName(id: string): string {
  const local =
    id.split(":").at(-1)?.split("/").at(-1) ?? id;

  return local
    .split(/[_-]+/)
    .filter(Boolean)
    .map((word) => (word[0]?.toUpperCase() ?? "") + word.slice(1))
    .join(" ");
}

export function abbreviateContentId(id: string): string {
  const local = id.includes(":")
    ? id.split(":").at(-1) ?? id
    : id;
  const words = local.split(/[_-]+/).filter(Boolean);

  if (words.length === 0) return "?";
  if (words.length === 1) {
    return words[0].slice(0, 2).toUpperCase();
  }

  return words
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? "")
    .join("");
}

export function vitalRatio(value: VitalValue): number {
  if (
    !Number.isFinite(value.maximum) ||
    value.maximum <= 0 ||
    !Number.isFinite(value.current)
  ) {
    return 0;
  }

  return value.current / value.maximum;
}

export function formatVital(value: VitalValue): string {
  return (
    Math.max(0, Math.round(value.current)) +
    "/" +
    Math.max(0, Math.round(value.maximum))
  );
}
