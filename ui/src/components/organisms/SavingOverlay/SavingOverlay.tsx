import { useLocalization } from "../../../localization/LocalizationProvider";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { CosmicBackground } from "../CosmicBackground/CosmicBackground";
import "./SavingOverlay.css";

export type SavingOverlayProps = {
  visible: boolean;
};

export function SavingOverlay({ visible }: SavingOverlayProps) {
  const { t } = useLocalization();
  if (!visible) return null;

  return (
    <section
      className="saving-overlay"
      aria-live="polite"
      aria-busy="true"
    >
      <CosmicBackground />
      <Surface variant="frosted" className="saving-overlay__card">
        <Text text={t("saving.asteria")} variant="eyebrow" />
        <h1 className="saving-overlay__title">{t("saving.title")}</h1>
        <div
          className="saving-overlay__progress"
          role="progressbar"
          aria-label={t("saving.title")}
          aria-valuetext={t("saving.detail")}
        >
          <span />
        </div>
        <Text
          text={t("saving.detail")}
          variant="detail"
          className="saving-overlay__detail"
        />
      </Surface>
    </section>
  );
}
