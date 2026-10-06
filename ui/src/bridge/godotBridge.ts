export type BridgeMessage = {
  type: string;
  payload?: unknown;
};

type GodotIpc = {
  postMessage(message: string): void;
};

declare global {
  interface Window {
    ipc?: GodotIpc;
  }
}

export function isGodotEmbedded(): boolean {
  return Boolean(window.ipc);
}

export function postGodotMessage(
  type: string,
  payload: unknown = {},
): void {
  window.ipc?.postMessage(
    JSON.stringify({ type, payload } satisfies BridgeMessage),
  );
}

export function subscribeGodotMessages(
  handler: (message: BridgeMessage) => void,
): () => void {
  const listener = (event: Event): void => {
    const detail = (event as CustomEvent<string>).detail;

    try {
      handler(JSON.parse(detail) as BridgeMessage);
    } catch (error) {
      console.error("Invalid message from Godot", error);
    }
  };

  document.addEventListener("message", listener);
  return () => document.removeEventListener("message", listener);
}
