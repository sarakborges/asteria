import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { resolve, relative } from "node:path";
import { fileURLToPath } from "node:url";

// Keep this list in sync with WebUiHost.gd's UI_SOURCE_PATHS.
const project = resolve(fileURLToPath(new URL("../../", import.meta.url)));
const sourceRoots = [
  "ui/src", "ui/index.html", "ui/package.json", "ui/package-lock.json",
  "ui/tsconfig.json", "ui/vite.config.ts", "packs/default/data/localization",
];

function visit(path) {
  if (!existsSync(path)) return [];
  const stats = statSync(path);
  if (stats.isDirectory()) {
    return readdirSync(path).flatMap(entry => visit(resolve(path, entry)));
  }
  return stats.isFile() ? [path] : [];
}

const entries = sourceRoots.flatMap(path => visit(resolve(project, path)))
  .map(path => {
    const relativePath = relative(project, path).replaceAll("\\", "/");
    const hash = createHash("sha256").update(readFileSync(path)).digest("hex");
    return relativePath + "\t" + hash;
  }).sort();

writeFileSync(resolve(project, "ui/dist/source-manifest.txt"), entries.join("\n") + "\n");
