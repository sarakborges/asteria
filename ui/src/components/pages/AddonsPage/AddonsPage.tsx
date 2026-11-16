import { useEffect, useState } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { AddonsState, ModAddon, PackAddon } from "../../../state/uiState";
import { Button } from "../../atoms/Button/Button";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import "./AddonsPage.css";

export type AddonsPageProps = {
  tab: "mods" | "packs";
  state: AddonsState;
  onBack(): void;
  onPackScope(name: string, scope: "Data" | "Resources" | "Both" | null): void;
  onMovePack(name: string, direction: -1 | 1): void;
  onModConfigure(id: string, enabled: boolean, settings: string): void;
};

function ModRow({ mod, onConfigure }: {
  mod: ModAddon;
  onConfigure(id: string, enabled: boolean, settings: string): void;
}) {
  const { t } = useLocalization();
  const [settings, setSettings] = useState(mod.settings);
  const [invalid, setInvalid] = useState(false);
  useEffect(() => {
    setSettings(mod.settings);
    setInvalid(false);
  }, [mod.settings]);

  const save = (enabled: boolean) => {
    try {
      const parsed: unknown = JSON.parse(settings);
      if (!parsed || typeof parsed !== "object" || Array.isArray(parsed))
        throw new Error("Invalid settings");
      setInvalid(false);
      onConfigure(mod.id, enabled, settings);
    } catch { setInvalid(true); }
  };
  return <article className="addons-page__entry">
    <div className="addons-page__line">
      <div><strong>{mod.name}</strong> <small>{mod.version}</small>
        <p className="addons-page__sub">{mod.id} — {mod.status}</p>
      </div>
      <label>
        <input type="checkbox" checked={mod.enabled}
          onChange={event => save(event.target.checked)} />
        {t("addons.enabled")}
      </label>
    </div>
    {mod.error && <p className="addons-page__error" role="alert">{mod.error}</p>}
    <label className="addons-page__settings">
      {t("addons.modSettings")}
      <textarea rows={3} spellCheck={false} value={settings}
        onChange={event => setSettings(event.target.value)} />
    </label>
    {invalid && <p role="alert" className="addons-page__error">{t("addons.invalidJson")}</p>}
    <Button label={t("addons.saveSettings")} onClick={() => save(mod.enabled)} />
  </article>;
}

function PackRow({ pack, onScope, onMove, lastOrder }: {
  pack: PackAddon;
  onScope(name: string, scope: "Data" | "Resources" | "Both" | null): void;
  onMove(name: string, direction: -1 | 1): void;
  lastOrder: number;
}) {
  const { t } = useLocalization();
  return <article className="addons-page__entry">
    <div className="addons-page__line">
      <div><strong>{pack.title}</strong> <small>{pack.version}</small>
        <p className="addons-page__sub">{pack.id}</p>
        <p className="addons-page__sub">{pack.description}</p>
        {pack.error && <p className="addons-page__error" role="alert">{pack.error}</p>}
      </div>
      <label>
        <input type="checkbox" checked={pack.enabled} disabled={pack.error !== null}
          onChange={event => onScope(pack.name, event.target.checked ? "Both" : null)} />
        {t("addons.enabled")}
      </label>
    </div>
    {pack.enabled && <div className="addons-page__line">
      <label>{t("addons.domain")}
        <select value={pack.scope} onChange={event =>
          onScope(pack.name, event.target.value as "Data" | "Resources" | "Both")}>
          <option value="Resources">{t("addons.resources")}</option>
          <option value="Data">{t("addons.data")}</option>
          <option value="Both">{t("addons.both")}</option>
        </select>
      </label>
      <div className="addons-page__priority">
        <Button label="↑" disabled={pack.order === 0}
          onClick={() => onMove(pack.name, -1)} />
        <Button label="↓" disabled={pack.order === lastOrder}
          onClick={() => onMove(pack.name, 1)} />
      </div>
    </div>}
  </article>;
}

export function AddonsPage({
  tab, state, onBack, onPackScope, onMovePack, onModConfigure,
}: AddonsPageProps) {
  const { t } = useLocalization();
  const packs = [...state.packs].sort((a, b) =>
    Number(b.enabled) - Number(a.enabled) || a.order - b.order || a.name.localeCompare(b.name));
  const maxOrder = Math.max(-1, ...state.packs.filter(item => item.enabled).map(item => item.order));
  const restart = tab === "packs" ? state.packRestartRequired : state.modRestartRequired;
  return <main className="addons-page">
    <CosmicBackground />
    <section className="addons-page__panel">
      <header className="addons-page__header">
        <h1>{t(tab === "mods" ? "addons.mods" : "addons.packs")}</h1>
        <Button label={t("newWorld.return")} onClick={onBack} />
      </header>
      <p className="addons-page__notice">
        {t(tab === "mods" ? "addons.modsDescription" : "addons.packsDescription")}
      </p>
      {restart && <p className="addons-page__notice">{t("addons.restartRequired")}</p>}
      {state.error && <p className="addons-page__error" role="alert">{state.error}</p>}
      {tab === "packs" && <article className="addons-page__entry">
        <strong>Asteria Default</strong>
        <p className="addons-page__sub">{t("addons.default")}</p>
      </article>}
      {tab === "packs" && packs.length === 0 &&
        <p className="addons-page__notice">{t("addons.emptyPacks")}</p>}
      {tab === "mods" && state.mods.length === 0 &&
        <p className="addons-page__notice">{t("addons.emptyMods")}</p>}
      {tab === "packs" ? packs.map(pack => <PackRow
        key={pack.name} pack={pack} lastOrder={maxOrder}
        onScope={onPackScope} onMove={onMovePack} />)
        : state.mods.map(mod => <ModRow key={mod.id} mod={mod}
          onConfigure={onModConfigure} />)}
    </section>
  </main>;
}
