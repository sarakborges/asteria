import { createContext, useContext, useState, type ReactNode } from "react";
import english from "../../../packs/default/data/localization/english/ui.json";
import portugueseBrazil from "../../../packs/default/data/localization/portuguese_brazil/ui.json";
import spanish from "../../../packs/default/data/localization/spanish/ui.json";

export const languages = ["english", "portuguese_brazil", "spanish"] as const;
export type Language = (typeof languages)[number];
type Catalog = Record<string, string>;
const catalogs: Record<Language, Catalog> = {
  english, portuguese_brazil: portugueseBrazil, spanish,
};
type Localization = {
  language: Language;
  languages: readonly Language[];
  setLanguage(value: Language): void;
  t(key: string, replacements?: Record<string, string | number>): string;
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
    const value = catalogs[language][key];
    if (value === undefined) throw new Error("Missing translation: " + language + "/" + key);
    return value.replace(/\{([^{}]+)\}/g, (token, name: string) => {
      const replacement = replacements[name];
      if (replacement === undefined) throw new Error("Missing placeholder: " + key + "/" + name);
      return String(replacement);
    });
  };
  return <LocalizationContext.Provider value={{ language, languages, setLanguage, t }}>
    {children}
  </LocalizationContext.Provider>;
}
export function useLocalization(): Localization {
  const context = useContext(LocalizationContext);
  if (!context) throw new Error("LocalizationProvider is required");
  return context;
}
