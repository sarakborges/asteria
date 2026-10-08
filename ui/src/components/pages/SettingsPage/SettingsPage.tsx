import { useRef, type FormEventHandler, type ReactNode } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
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
  title?: string;
  footer?: ReactNode;
  formId?: string;
  onSubmit?: FormEventHandler<HTMLFormElement>;
};

export function SettingsPage({
  sections,
  selectedId,
  onSelect,
  onBack,
  title,
  footer,
  formId,
  onSubmit,
}: SettingsPageProps) {
  const { t } = useLocalization();
  const panelRefs = useRef(new Map<string, HTMLElement>());

  const panels = sections.map(section => (
    <section
      key={section.id}
      ref={element => {
        if (element) panelRefs.current.set(section.id, element);
        else panelRefs.current.delete(section.id);
      }}
      className="settings-page__section"
      aria-label={section.label}
    >
      <Text text={section.label} variant="heading" />
      <Surface variant="frosted" className="settings-page__card">
        {section.content}
      </Surface>
    </section>
  ));

  const content = formId ? (
    <form id={formId} className="settings-page__sections" onSubmit={onSubmit}>
      {panels}
    </form>
  ) : (
    <div className="settings-page__sections">{panels}</div>
  );

  return (
    <ScreenShell
      title={title ?? t("common.settings")}
      background={<CosmicBackground />}
      footer={footer ?? (
        <Button
          label={t("newWorld.return")}
          size="compact"
          className="settings-page__back"
          onClick={onBack}
        />
      )}
    >
      <div className="settings-page">
        <nav className="settings-page__navigation" aria-label={title ?? t("common.settings")}>
          {sections.map(section => (
            <Button
              key={section.id}
              label={section.label}
              size="menu"
              stretch
              variant={section.id === selectedId ? "primary" : "normal"}
              ariaPressed={section.id === selectedId}
              onClick={() => {
                onSelect(section.id);
                panelRefs.current.get(section.id)?.scrollIntoView({
                  behavior: "smooth",
                  block: "start",
                });
              }}
            />
          ))}
        </nav>
        {content}
      </div>
    </ScreenShell>
  );
}
