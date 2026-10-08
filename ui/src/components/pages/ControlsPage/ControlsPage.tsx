import { useLocalization } from "../../../localization/LocalizationProvider";
import type { BindableControl, ControlGroupView } from "../../../presentation/controlGroups";
export type { ControlEntryView, ControlGroupView } from "../../../presentation/controlGroups";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { ControlBindingEntry } from "../../molecules/ControlBindingEntry/ControlBindingEntry";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import { ScreenShell } from "../../templates/ScreenShell/ScreenShell";
import "./ControlsPage.css";

export type ControlsPageProps = {
  groups: readonly ControlGroupView[];
  capturingAction?: BindableControl | null;
  captureError?: string | null;
  onCapture?(action: BindableControl): void;
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
      title={t("controls.title")}
      background={<CosmicBackground />}
      footer={
        <Button
          label={t("newWorld.return")}
          className="controls-page__back"
          onClick={onBack}
        />
      }
    >
      <div className="controls-page">
        {captureError && (
          <div role="alert" className="controls-page__error">
            <Text text={captureError} variant="caption" />
          </div>
        )}
        {groups.map(group => (
          <Surface
            key={group.title}
            variant="frosted"
            className="controls-page__group"
          >
            <Text text={group.title} variant="heading" />
            <div className="controls-page__grid">
              {group.entries.map(entry => (
                <ControlBindingEntry
                  key={group.title + entry.key + entry.action}
                  entry={entry}
                  capturingAction={capturingAction}
                  onCapture={onCapture}
                  onCancelCapture={onCancelCapture}
                />
              ))}
            </div>
          </Surface>
        ))}
      </div>
    </ScreenShell>
  );
}
