import "./style.css";

type BridgeMessage = {
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

const root = document.querySelector<HTMLDivElement>("#app");
if (!root) throw new Error("Missing #app root");

const embedded = Boolean(window.ipc);

root.innerHTML = `
  <main class="hud-shell">
    <section class="status-card">
      <span class="eyebrow">ASTERIA / WEBUI SPIKE</span>
      <strong>WEBUI ONLINE</strong>
      <span id="bridge-status" class="bridge ${embedded ? "connecting" : "standalone"}">
        ${embedded ? "connecting to Godot" : "standalone browser mode"}
      </span>
      <span id="world-status" class="detail">waiting for chunk</span>
      <span id="last-message" class="detail">no bridge messages yet</span>
      <button id="ping" type="button" ${embedded ? "" : "disabled"}>Ping Godot</button>
    </section>
    <div class="crosshair" aria-hidden="true"></div>
  </main>
`;

const bridgeStatus = document.querySelector<HTMLSpanElement>("#bridge-status");
const worldStatus = document.querySelector<HTMLSpanElement>("#world-status");
const lastMessage = document.querySelector<HTMLSpanElement>("#last-message");
const pingButton = document.querySelector<HTMLButtonElement>("#ping");

function postMessage(type: string, payload: unknown = {}): void {
  window.ipc?.postMessage(JSON.stringify({ type, payload } satisfies BridgeMessage));
}

function handleGodotMessage(message: BridgeMessage): void {
  if (lastMessage) lastMessage.textContent = `godot → webui: ${message.type}`;

  switch (message.type) {
    case "game.ready":
      bridgeStatus?.classList.remove("connecting", "standalone");
      bridgeStatus?.classList.add("connected");
      if (bridgeStatus) bridgeStatus.textContent = "bridge connected";
      break;
    case "game.chunk_ready":
      if (worldStatus) worldStatus.textContent = "chunk generated";
      break;
    case "game.pong":
      if (lastMessage) lastMessage.textContent = "godot → webui: pong received";
      break;
  }
}

document.addEventListener("message", (event) => {
  const detail = (event as CustomEvent<string>).detail;

  try {
    handleGodotMessage(JSON.parse(detail) as BridgeMessage);
  } catch (error) {
    console.error("Invalid message from Godot", error);
  }
});

pingButton?.addEventListener("click", () => {
  if (lastMessage) lastMessage.textContent = "webui → godot: ping sent";
  postMessage("ui.ping");
});

postMessage("ui.ready", { version: 1 });
