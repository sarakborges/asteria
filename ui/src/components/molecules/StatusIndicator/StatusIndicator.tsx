import type { BridgeStatusTone } from "../../../state/uiState";
import { Text } from "../../atoms/Text/Text";
import "./StatusIndicator.css";

export type StatusIndicatorProps = {
  label: string;
  tone?: BridgeStatusTone;
  dataUi?: string;
};

export function StatusIndicator({
  label,
  tone = "neutral",
  dataUi,
}: StatusIndicatorProps) {
  return (
    <Text
      text={label}
      variant="detail"
      dataUi={dataUi}
      className={
        "status-indicator status-indicator--" +
        tone
      }
    />
  );
}
