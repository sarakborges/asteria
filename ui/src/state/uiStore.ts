import type {
  ToastInput,
  UiState,
} from "./uiState";

const MAX_TOASTS = 4;

export type UiStore = {
  getSnapshot(): UiState;
  subscribe(listener: () => void): () => void;
  update(updater: (state: UiState) => UiState): void;
  pushToast(toast: ToastInput): void;
  dismissToast(id: number): void;
};

export function createUiStore(
  initialState: UiState,
): UiStore {
  let state = initialState;
  let nextToastId = 1;
  const listeners = new Set<() => void>();

  const publish = (next: UiState): void => {
    if (next === state) return;
    state = next;
    for (const listener of listeners) {
      listener();
    }
  };

  return {
    getSnapshot() {
      return state;
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    update(updater) {
      publish(updater(state));
    },
    pushToast(toast) {
      const item = {
        ...toast,
        id: nextToastId++,
      };
      publish({
        ...state,
        hud: {
          ...state.hud,
          toasts: [
            ...state.hud.toasts.slice(-(MAX_TOASTS - 1)),
            item,
          ],
        },
      });
    },
    dismissToast(id) {
      const nextToasts = state.hud.toasts.filter(
        (toast) => toast.id !== id,
      );
      if (nextToasts.length === state.hud.toasts.length) return;
      publish({
        ...state,
        hud: {
          ...state.hud,
          toasts: nextToasts,
        },
      });
    },
  };
}
