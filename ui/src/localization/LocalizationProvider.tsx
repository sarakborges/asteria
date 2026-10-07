import { createContext, useContext, useState, type ReactNode } from "react";
import { displayContentName } from "../presentation/formatters";
import english from "../../../packs/default/data/localization/english/ui.json";
import portugueseBrazil from "../../../packs/default/data/localization/portuguese_brazil/ui.json";
import spanish from "../../../packs/default/data/localization/spanish/ui.json";
import blocksEn from "../../../packs/default/data/localization/english/blocks.json";
import blocksPt from "../../../packs/default/data/localization/portuguese_brazil/blocks.json";
import blocksEs from "../../../packs/default/data/localization/spanish/blocks.json";
import dimensionsEn from "../../../packs/default/data/localization/english/dimensions.json";
import dimensionsPt from "../../../packs/default/data/localization/portuguese_brazil/dimensions.json";
import dimensionsEs from "../../../packs/default/data/localization/spanish/dimensions.json";
import fluidsEn from "../../../packs/default/data/localization/english/fluids.json";
import fluidsPt from "../../../packs/default/data/localization/portuguese_brazil/fluids.json";
import fluidsEs from "../../../packs/default/data/localization/spanish/fluids.json";

export const languages = ["english", "portuguese_brazil", "spanish"] as const;
export type Language = (typeof languages)[number];
type UiCatalog = Readonly<Record<string, string>>;
type Names = Readonly<Record<string, { "/name": string }>>;
const uiCatalogs: Record<Language, UiCatalog> = {
  english, portuguese_brazil: portugueseBrazil, spanish,
};
const nameCatalogs: Record<Language, readonly Names[]> = {
  english: [blocksEn, dimensionsEn, fluidsEn],
  portuguese_brazil: [blocksPt, dimensionsPt, fluidsPt],
  spanish: [blocksEs, dimensionsEs, fluidsEs],
};

type Localization = {
  language: Language;
  languages: readonly Language[];
  setLanguage(value: Language): void;
  t(key: string, replacements?: Record<string, string | number>): string;
  contentName(id: string): string;
};
const LocalizationContext = createContext<Localization | null>(null);
const STORAGE_KEY = "asteria.language";

function initialLanguage(): Language {
  const saved = window.localStorage.getItem(STORAGE_KEY);
  return languages.find(language => language === saved) ?? "english";
}

export function LocalizationProvider({ children }: { children: ReactNode }) {
  const [language, updateLanguage] = useState<Language>(initialLanguage);
  const setLanguage = (value: Language) => {
    if (!languages.includes(value)) throw new Error("Unsupported language: " + value);
    window.localStorage.setItem(STORAGE_KEY, value);
    updateLanguage(value);
  };
  const t = (key: string, replacements: Record<string, string | number> = {}) => {
    const value = uiCatalogs[language][key];
    if (value === undefined) throw new Error("Missing translation: " + language + "/" + key);
    return value.replace(/\{([^{}]+)\}/g, (_token, name: string) => {
      const replacement = replacements[name];
      if (replacement === undefined) throw new Error("Missing placeholder: " + key + "/" + name);
      return String(replacement);
    });
  };
  const contentName = (id: string): string => {
    for (const catalog of nameCatalogs[language]) {
      const entry = catalog[id];
      if (entry) return entry["/name"];
    }
    return displayContentName(id);
  };

  return (
    <LocalizationContext.Provider value={{ language, languages, setLanguage, t, contentName }}>
      {children}
    </LocalizationContext.Provider>
  );
}

export function useLocalization(): Localization {
  const context = useContext(LocalizationContext);
  if (!context) throw new Error("LocalizationProvider is required");
  return context;
}
