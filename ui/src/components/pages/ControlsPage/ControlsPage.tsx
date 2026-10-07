import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import { KeyCap } from "../../atoms/KeyCap/KeyCap";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import { ScreenShell } from "../../templates/ScreenShell/ScreenShell";
import "./ControlsPage.css";

export type ControlEntryView = {
  key: string;
  action: string;
};

export type ControlGroupView = {
  title: string;
  entries: readonly ControlEntryView[];
};

export type ControlsPageProps = {
  groups: readonly ControlGroupView[];
  onBack(): void;
};

export function ControlsPage({
  groups,
  onBack,
}: ControlsPageProps) {
  const { t } = useLocalization();
  return (
    <ScreenShell
      title={t("common.controls")}
      background={<CosmicBackground />}
      footer={
        <Button
          label={t("ui.back")}
          className="controls-page__back"
          onClick={onBack}
        />
      }
    >
      <div className="controls-page">
        {groups.map((group) => (
          <Surface
            key={group.title}
            variant="frosted"
            className="controls-page__group"
          >
            <Text
              text={group.title}
              variant="heading"
            />
            <div className="controls-page__grid">
              {group.entries.map((entry) => (
                <div
                  key={group.title + entry.key + entry.action}
                  className="controls-page__entry"
                >
                  <KeyCap label={entry.key} />
                  <Text
                    text={entry.action}
                    variant="detail"
                  />
                </div>
              ))}
            </div>
          </Surface>
        ))}
      </div>
    </ScreenShell>
  );
}
