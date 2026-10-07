import { useSyncExternalStore } from "react";
import type { UiStore } from "./uiStore";

export function useUiStore(store: UiStore) {
  return useSyncExternalStore(
    store.subscribe,
    store.getSnapshot,
    store.getSnapshot,
  );
}
