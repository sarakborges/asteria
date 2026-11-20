import { defineConfig } from "vite";

export default defineConfig({
  base: "./",
  // The browser bundle is exported by Godot from a project-root directory.
  // Inline authored assets so WebView receives raw WOFF2/SVG bytes without
  // Godot converting them into native imported resources.
  publicDir: false,
  build: {
    outDir: "../webui-dist",
    emptyOutDir: true,
    assetsInlineLimit: Number.MAX_SAFE_INTEGER,
  },
});
