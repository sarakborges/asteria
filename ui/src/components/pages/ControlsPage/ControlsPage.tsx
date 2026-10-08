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
  bindAction?: "Jump" | "Descend";
};

export type ControlGroupView = {
  title: string;
  entries: readonly ControlEntryView[];
};

export type ControlsPageProps = {
  groups: readonly ControlGroupView[];
  capturingAction?: "Jump" | "Descend" | null;
  captureError?: string | null;
  onCapture?(action: "Jump" | "Descend"): void;
  onCancelCapture?(): void;
  onBack(): void;
};

export function ControlsPage({
  groups,
  capturingAction,
  captureError,
  onCapture,
  onCancelCapture,
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
        {captureError && <Text text={captureError} variant="caption" />}
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
                  {entry.bindAction && onCapture ? (
                    <Button
                      label={capturingAction === entry.bindAction
                        ? t("settings.keybind.pressKey") : entry.key}
                      onClick={() => {
                        if (capturingAction === entry.bindAction)
                          onCancelCapture?.();
                        else onCapture(entry.bindAction!);
                      }}
                    />
                  ) : <KeyCap label={entry.key} />}
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
