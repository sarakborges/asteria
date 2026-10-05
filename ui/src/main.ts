import "./style.css";

type AsteriaBridge = {
  postMessage(message: string): void;
};

declare global {
  interface Window {
    asteria?: AsteriaBridge;
  }
}

const root = document.querySelector<HTMLDivElement>("#app");
if (!root) throw new Error("Missing #app root");

const embedded = Boolean(window.asteria);

root.innerHTML = `
  <main class="hud-shell">
    <section class="status-card">
      <span class="eyebrow">ASTERIA / TEST 1</span>
      <strong>WEBUI ONLINE</strong>
      <span class="bridge ${embedded ? "connected" : "standalone"}">
        ${embedded ? "bridge connected" : "standalone browser mode"}
      </span>
    </section>
    <div class="crosshair" aria-hidden="true"></div>
  </main>
`;

window.asteria?.postMessage(JSON.stringify({ type: "ui.ready", version: 1 }));
