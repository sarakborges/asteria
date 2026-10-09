import { useRef, type PointerEvent } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { CharacterInfoView } from "../../../presentation/inventoryModels";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import "./CharacterInfoPanel.css";

export type CharacterInfoPanelProps = {
  state: CharacterInfoView | null;
  portraitUrl?: string | null;
  onRotatePortrait?(deltaX: number): void;
};

export function CharacterInfoPanel({
  state, portraitUrl, onRotatePortrait,
}: CharacterInfoPanelProps) {
  const { t, contentName } = useLocalization();
  const previousX = useRef<number | null>(null);
  const drag = (event: PointerEvent<HTMLDivElement>) => {
    if (previousX.current === null || !onRotatePortrait) return;
    const deltaX = event.clientX - previousX.current;
    previousX.current = event.clientX;
    if (Math.abs(deltaX) >= 1) onRotatePortrait(deltaX);
  };
  const ratio = state && state.healthMaximum > 0
    ? Math.min(1, Math.max(0, state.healthCurrent / state.healthMaximum))
    : 0;

  return (
    <Surface variant="hud" className="character-info-panel">
      <div className="character-info-panel__preview"
        onPointerDown={event => {
          if (!onRotatePortrait || !portraitUrl) return;
          previousX.current = event.clientX;
          event.currentTarget.setPointerCapture(event.pointerId);
        }}
        onPointerMove={drag}
        onPointerUp={event => {
          previousX.current = null;
          if (event.currentTarget.hasPointerCapture(event.pointerId))
            event.currentTarget.releasePointerCapture(event.pointerId);
        }}
        onPointerCancel={() => { previousX.current = null; }}>
        {portraitUrl
          ? <img className="character-info-panel__portrait"
              src={portraitUrl} alt={state?.name ?? t("ui.player")}
              draggable={false} />
          : <span>{t("inventory.characterPreviewUnavailable")}</span>}
      </div>
      <div className="character-info-panel__details">
        <Text text={state?.name ?? t("ui.player")} variant="heading" />
        {state ? (
          <section className="character-info-panel__section">
            <Text text={t("ui.health")} variant="setting-title" />
            <div className="character-info-panel__health">
              <span className="character-info-panel__health-fill"
                style={{ width: ratio * 100 + "%" }} />
              <span className="character-info-panel__health-label">
                {Math.round(state.healthCurrent)} / {Math.round(state.healthMaximum)}
              </span>
            </div>
          </section>
        ) : (
          <Text text={t("inventory.characterInfoUnavailable")} variant="caption" />
        )}
        {state && state.equipment.length > 0 && (
          <section className="character-info-panel__section">
            <Text text={t("ui.armor")} variant="setting-title" />
            <div className="character-info-panel__equipment">
              {state.equipment.map(entry => (
                <div key={entry.slot} className="character-info-panel__equipment-row">
                  <InventorySlot item={entry.item} disabled />
                  <div className="character-info-panel__equipment-copy">
                    <Text text={entry.item?.id
                      ? contentName(entry.item.id) : entry.emptyLabel} variant="caption" />
                    <Text text={entry.effectLabel ?? t("ui.noEffect")} variant="caption" />
                  </div>
                </div>
              ))}
            </div>
          </section>
        )}
      </div>
    </Surface>
  );
}
