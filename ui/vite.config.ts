import { defineConfig } from "vite";

export default defineConfig({
  base: "./",
  // Copy the pack-owned presentation assets into the browser bundle as raw
  // files. Godot must not import these through its native resource pipeline.
  publicDir: "../packs/default/ui",
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
});
