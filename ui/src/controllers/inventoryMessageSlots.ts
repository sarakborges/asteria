import type { InventoryEntryKind, InventoryMetadata, InventorySlotState } from "../state/uiState";
import { asRecord } from "./messagePayload";

export function readKind(value: unknown): InventoryEntryKind | null {
  return value === "block" || value === "item" || value === "tool" || value === "layer"
    ? value : null;
}

export function readMetadata(value: unknown): InventoryMetadata | null {
  const raw = asRecord(value);
  if (!raw || Object.values(raw).some(x => typeof x !== "string"))
    return null;
  return raw as InventoryMetadata;
}

export function readSlot(value: unknown): InventorySlotState {
  if (value === null) return null;
  const record = asRecord(value);
  if (!record || typeof record.id !== "string" ||
      !Number.isInteger(record.quantity) ||
      (record.quantity as number) < 1 ||
      (record.quantity as number) > 64) return null;
  const kind = readKind(record.kind);
  const metadata = readMetadata(record.metadata);
  if (!kind || !metadata) return null;
  const rawDurability = record.durability == null
    ? null : asRecord(record.durability);
  if (record.durability != null && !rawDurability) return null;
  const durability = rawDurability
    ? Number.isInteger(rawDurability.current) &&
      Number.isInteger(rawDurability.maximum) &&
      (rawDurability.maximum as number) > 0 &&
      (rawDurability.maximum as number) <= 65535 &&
      (rawDurability.current as number) > 0 &&
      (rawDurability.current as number) <= (rawDurability.maximum as number)
        ? { current: rawDurability.current as number, maximum: rawDurability.maximum as number }
        : null
    : null;
  if (rawDurability && !durability) return null;
  return {
    id: record.id, kind, quantity: record.quantity as number, metadata,
    durability,
  };
}

export function readSlots(value: unknown, count: number): InventorySlotState[] | null {
  if (!Array.isArray(value) || value.length !== count) return null;
  return value.map(readSlot);
}

