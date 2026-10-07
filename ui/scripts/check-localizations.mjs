import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
const languages = ["english", "portuguese_brazil", "spanish"];
const domains = ["ui", "blocks", "dimensions", "fluids"];
const root = fileURLToPath(new URL("../../packs/default/data/localization/", import.meta.url));
const readCatalog = (language, domain) =>
  JSON.parse(readFileSync(root + language + "/" + domain + ".json", "utf8"));
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
