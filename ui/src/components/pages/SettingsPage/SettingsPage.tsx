import { useLocalization } from "../../../localization/LocalizationProvider";
import type { ReactNode } from "react";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import { ScreenShell } from "../../templates/ScreenShell/ScreenShell";
import "./SettingsPage.css";

export type SettingsSectionView = {
  id: string;
  label: string;
  content: ReactNode;
};

export type SettingsPageProps = {
  sections: readonly SettingsSectionView[];
  selectedId: string;
  onSelect(id: string): void;
  onBack(): void;
};

export function SettingsPage({
  sections,
  selectedId,
  onSelect,
  onBack,
}: SettingsPageProps) {
  const { t } = useLocalization();
  return (
    <ScreenShell
      title={t("common.settings")}
      background={<CosmicBackground />}
      footer={
        <Button
          label={t("ui.back")}
          size="menu"
          className="settings-page__back"
          onClick={onBack}
        />
      }
    >
      <div className="settings-page">
        <nav className="settings-page__navigation">
          {sections.map((section) => (
            <Button
              key={section.id}
              label={section.label}
              size="menu"
              stretch
              variant={
                section.id === selectedId
                  ? "primary"
                  : "normal"
              }
              onClick={() =>
                onSelect(section.id)
              }
            />
          ))}
        </nav>

        <div className="settings-page__sections">
          {sections.map((section) => (
            <section
              key={section.id}
              className="settings-page__section"
              data-selected={
                section.id === selectedId
                  ? "true"
                  : "false"
              }
            >
              <Text
                text={section.label}
                variant="heading"
              />
              <Surface
                variant="frosted"
                className="settings-page__card"
              >
                {section.content}
              </Surface>
            </section>
          ))}
        </div>
      </div>
    </ScreenShell>
  );
}
