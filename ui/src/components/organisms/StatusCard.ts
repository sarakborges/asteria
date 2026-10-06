import { createButton } from "../atoms/Button";
import { createText } from "../atoms/Text";
import {
  createStatusIndicator,
  type StatusIndicatorView,
} from "../molecules/StatusIndicator";
import "./StatusCard.css";

export type StatusCardProps = {
  embedded: boolean;
};

export type StatusCardView = {
  element: HTMLElement;
  bridgeStatus: StatusIndicatorView;
  worldStatus: HTMLElement;
  playerStatus: HTMLElement;
  lastMessage: HTMLElement;
  pingButton: HTMLButtonElement;
};

export function createStatusCard(props: StatusCardProps): StatusCardView {
  const card = document.createElement("section");
  card.className = "status-card";

  const bridgeStatus = createStatusIndicator({
    label: props.embedded
      ? "connecting to Godot"
      : "standalone browser mode",
    tone: props.embedded ? "connecting" : "neutral",
    dataUi: "bridge-status",
  });

  const worldStatus = createText({
    text: "waiting for chunk",
    variant: "detail",
    dataUi: "world-status",
  });

  const playerStatus = createText({
    text: "waiting for player",
    variant: "detail",
    dataUi: "player-status",
  });

  const controls = createText({
    text: "WASD · mouse look · Space jump · LMB break · RMB place · Esc cursor",
    variant: "detail",
  });

  const lastMessage = createText({
    text: "no bridge messages yet",
    variant: "detail",
    dataUi: "last-message",
  });

  const pingButton = createButton({
    label: "Ping Godot",
    disabled: !props.embedded,
    dataUi: "ping",
  });

  card.append(
    createText({
      text: "ASTERIA / GODOT SPIKE",
      variant: "eyebrow",
    }),
    createText({
      text: "WEBUI ONLINE",
      variant: "title",
    }),
    bridgeStatus.element,
    worldStatus,
    playerStatus,
    controls,
    lastMessage,
    pingButton,
  );

  return {
    element: card,
    bridgeStatus,
    worldStatus,
    playerStatus,
    lastMessage,
    pingButton,
  };
}
