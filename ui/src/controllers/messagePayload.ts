export function asRecord(
  value: unknown,
): Record<string, unknown> | null {
  return value !== null &&
    typeof value === "object" &&
    !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

export function isFiniteNumber(
  value: unknown,
): value is number {
  return typeof value === "number" &&
    Number.isFinite(value);
}
