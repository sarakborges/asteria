import type { StatusCardState } from "../../../state/uiState";
import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import { StatusIndicator } from "../../molecules/StatusIndicator/StatusIndicator";
import "./StatusCard.css";

export type StatusCardProps = {
  embedded: boolean;
  state: StatusCardState;
  onPing(): void;
};

export function StatusCard({
  embedded,
  state,
  onPing,
}: StatusCardProps) {
  return (
    <section className="status-card">
      <Text text="ASTERIA / DEBUG" variant="eyebrow" />
      <Text text="RUNTIME STATUS" variant="title" />
      <StatusIndicator
        label={state.bridgeLabel}
        tone={state.bridgeTone}
        dataUi="bridge-status"
      />
      <Text
        text={state.worldStatus}
        variant="detail"
        dataUi="world-status"
      />
      <Text
        text={state.playerStatus}
        variant="detail"
        dataUi="player-status"
      />
      <Text
        text="F3 debug · WASD move · LMB break · RMB place · Esc cursor"
        variant="detail"
      />
      <Text
        text={state.lastMessage}
        variant="detail"
        dataUi="last-message"
      />
      <Button
        label="Ping Godot"
        disabled={!embedded}
        dataUi="ping"
        onClick={onPing}
      />
    </section>
  );
}
