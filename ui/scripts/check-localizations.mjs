import { existsSync, readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const languages = ["english", "portuguese_brazil", "spanish"];
const domains = ["ui", "blocks", "dimensions", "fluids", "items", "tools", "creatures"];
const packRoot = fileURLToPath(new URL("../../packs/default/", import.meta.url));
const dataRoot = join(packRoot, "data");
const resourcesRoot = join(packRoot, "resources");
const catalogsRoot = join(dataRoot, "localization");
const catalog = (language, domain) =>
  JSON.parse(readFileSync(join(catalogsRoot, language, domain + ".json"), "utf8"));
const placeholders = value =>
  [...value.matchAll(/\{([^{}]+)\}/g)].map(match => match[1]).sort().join("|");

for (const domain of domains) {
  const original = catalog("english", domain);
  const keys = Object.keys(original).sort();
  for (const language of languages) {
    const localized = catalog(language, domain);
    if (JSON.stringify(Object.keys(localized).sort()) !== JSON.stringify(keys)) {
      throw new Error(language + "/" + domain + " ids differ from English");
    }
    for (const key of keys) {
      const sourceFields = domain === "ui" ? { text: original[key] } : original[key];
      const fields = domain === "ui" ? { text: localized[key] } : localized[key];
      if (JSON.stringify(Object.keys(fields).sort()) !==
          JSON.stringify(Object.keys(sourceFields).sort())) {
        throw new Error(language + "/" + domain + " pointers differ at " + key);
      }
      for (const pointer of Object.keys(sourceFields)) {
        if (domain !== "ui" && !pointer.startsWith("/")) {
          throw new Error(domain + " localization field must be a JSON pointer: " + pointer);
        }
        const translated = fields[pointer];
        if (typeof translated !== "string" || !translated.trim()) {
          throw new Error(language + "/" + domain + " missing text " + key + pointer);
        }
        if (placeholders(translated) !== placeholders(sourceFields[pointer])) {
          throw new Error(language + "/" + domain + " placeholder mismatch: " + key + pointer);
        }
      }
    }
  }
  console.log("Validated " + domain + ": " + keys.length + " entries");
}

const resource = (path, context) => {
  if (typeof path !== "string" || !path ||
      path.includes("\\") || path.includes(":") ||
      path.startsWith("/") ||
      path.split("/").some(segment => !segment || segment === "." || segment === "..")) {
    throw new Error(context + ": invalid pack resource path");
  }
  if (!existsSync(join(resourcesRoot, path))) {
    throw new Error(context + ": missing pack resource " + path);
  }
};

for (const [domain, directories] of [
  ["blocks", ["blocks"]],
  ["dimensions", ["biomes", "dimensions"]],
  ["fluids", ["fluids"]],
  ["items", ["items"]],
  ["tools", ["tools"]],
  ["creatures", ["creatures"]],
]) {
  const ids = new Set(Object.keys(catalog("english", domain)));
  const seen = new Set();
  for (const directory of directories) {
    for (const file of readdirSync(join(dataRoot, directory))
      .filter(filename => filename.endsWith(".json")).sort()) {
      const definition = JSON.parse(readFileSync(join(dataRoot, directory, file), "utf8"));
      if (typeof definition.id !== "string" || !definition.id) {
        throw new Error("Missing id in " + directory + "/" + file);
      }
      if (seen.has(definition.id) || !ids.has(definition.id)) {
        throw new Error("Duplicate or untranslated " + domain + " id " + definition.id);
      }
      seen.add(definition.id);
      if (domain === "items" || domain === "tools") {
        resource(definition.icon, definition.id + ".icon");
        if (definition.tintIcon) resource(definition.tintIcon, definition.id + ".tintIcon");
        for (const variant of definition.iconVariants ?? []) {
          resource(variant.icon, definition.id + ".iconVariants");
        }
      }
      if (domain === "creatures") {
        resource(definition.model, definition.id + ".model");
        for (const [material, texture] of Object.entries(definition.textures ?? {})) {
          resource(texture, definition.id + ".textures." + material);
        }
        const items = catalog("english", "items");
        for (const entry of definition.lootTable ?? []) {
          if (!Object.hasOwn(items, entry.item)) {
            throw new Error(definition.id + ": unresolved creature loot item " + entry.item);
          }
        }
      }
    }
  }
  for (const id of ids) {
    if (!seen.has(id)) throw new Error("Unused " + domain + " localization: " + id);
  }
  console.log("Validated authored " + domain + ": " + seen.size + " definitions");
}
