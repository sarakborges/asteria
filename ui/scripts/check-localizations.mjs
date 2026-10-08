import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const languages = ["english", "portuguese_brazil", "spanish"];
const domains = ["ui", "blocks", "dimensions", "fluids"];
const dataRoot = fileURLToPath(new URL("../../packs/default/data/", import.meta.url));
const catalogsRoot = join(dataRoot, "localization");
const readCatalog = (language, domain) =>
  JSON.parse(readFileSync(join(catalogsRoot, language, domain + ".json"), "utf8"));
const placeholders = value =>
  [...value.matchAll(/\{([^{}]+)\}/g)].map(match => match[1]).sort().join("|");

for (const domain of domains) {
  const original = readCatalog("english", domain);
  const keys = Object.keys(original).sort();
  for (const language of languages) {
    const catalog = readCatalog(language, domain);
    if (JSON.stringify(Object.keys(catalog).sort()) !== JSON.stringify(keys)) {
      throw new Error(language + "/" + domain + " keys differ from English");
    }
    for (const key of keys) {
      const source = domain === "ui" ? original[key] : original[key]["/name"];
      const translated = domain === "ui" ? catalog[key] : catalog[key]["/name"];
      if (typeof translated !== "string" || !translated.trim()) {
        throw new Error(language + "/" + domain + " missing entry " + key);
      }
      if (placeholders(translated) !== placeholders(source)) {
        throw new Error(language + "/" + domain + " mismatched placeholders for " + key);
      }
      if (domain !== "ui" && JSON.stringify(Object.keys(original[key]).sort()) !== JSON.stringify(Object.keys(catalog[key]).sort())) {
        throw new Error(language + "/" + domain + " pointer mismatch for " + key);
      }
    }
  }
  console.log("Validated " + domain + ": " + keys.length + " entries");
}

// Every authored block, biome, dimension and fluid must have a display name.
// Validate coverage at build time so missing translations never ship silently.
for (const [domain, directories] of [
  ["blocks", ["blocks"]],
  ["dimensions", ["biomes", "dimensions"]],
  ["fluids", ["fluids"]],
]) {
  const keys = new Set(Object.keys(readCatalog("english", domain)));
  const seen = new Set();
  for (const directory of directories) {
    const dir = join(dataRoot, directory);
    for (const file of readdirSync(dir).filter(file => file.endsWith(".json")).sort()) {
      const definition = JSON.parse(readFileSync(join(dir, file), "utf8"));
      if (typeof definition.id !== "string" || !definition.id) {
        throw new Error("Missing definition id in " + directory + "/" + file);
      }
      if (seen.has(definition.id)) {
        throw new Error("Duplicate definition id: " + definition.id);
      }
      seen.add(definition.id);
      if (!keys.has(definition.id)) {
        throw new Error("Missing " + domain + " translation for " + definition.id);
      }
    }
  }
  for (const key of keys) {
    if (!seen.has(key)) {
      throw new Error("Unused " + domain + " localization: " + key);
    }
  }
  console.log("Validated authored " + domain + ": " + seen.size + " definitions");
}
