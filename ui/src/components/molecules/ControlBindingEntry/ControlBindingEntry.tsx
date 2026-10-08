import type { BindableControl, ControlEntryView } from "../../../presentation/controlGroups";
import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import { KeyCap } from "../../atoms/KeyCap/KeyCap";
import { Text } from "../../atoms/Text/Text";
import "./ControlBindingEntry.css";

export type ControlBindingEntryProps = {
  entry: ControlEntryView;
  capturingAction?: BindableControl | null;
  onCapture?(action: BindableControl): void;
  onCancelCapture?(): void;
};

export function ControlBindingEntry({
  entry, capturingAction, onCapture, onCancelCapture,
}: ControlBindingEntryProps) {
  const { t } = useLocalization();
  const capturing = entry.bindAction !== undefined &&
    capturingAction === entry.bindAction;
  return (
    <div className="control-binding-entry">
      <div className="control-binding-entry__binding">
        {entry.bindAction && onCapture ? (
          <Button
            label={capturing ? t("settings.keybind.pressKey") : entry.key}
            ariaPressed={capturing}
            onClick={() => {
              if (!entry.bindAction) return;
              if (capturing) onCancelCapture?.();
              else onCapture(entry.bindAction);
            }}
          />
        ) : <KeyCap label={entry.key} />}
      </div>
      <Text text={entry.action} variant="detail" />
    </div>
  );
}
