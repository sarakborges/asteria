import { createText } from "../atoms/Text";
import "./StatusIndicator.css";

export type StatusTone = "neutral" | "connecting" | "connected";

export type StatusIndicatorProps = {
  label: string;
  tone?: StatusTone;
  dataUi?: string;
};

export type StatusIndicatorView = {
  element: HTMLElement;
  setLabel(label: string): void;
  setTone(tone: StatusTone): void;
};

export function createStatusIndicator(
  props: StatusIndicatorProps,
): StatusIndicatorView {
  const element = createText({
    text: props.label,
    variant: "detail",
    dataUi: props.dataUi,
  });

  element.classList.add("status-indicator");

  const setTone = (tone: StatusTone): void => {
    element.classList.remove(
      "status-indicator--neutral",
      "status-indicator--connecting",
      "status-indicator--connected",
    );
    element.classList.add(`status-indicator--${tone}`);
  };

  setTone(props.tone ?? "neutral");

  return {
    element,
    setLabel(label) {
      element.textContent = label;
    },
    setTone,
  };
}
