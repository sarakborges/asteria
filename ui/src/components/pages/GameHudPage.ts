import { createCrosshair } from "../atoms/Crosshair";
import {
  createStatusCard,
  type StatusCardView,
} from "../organisms/StatusCard";
import { createHudShell } from "../templates/HudShell";

export type GameHudPageProps = {
  embedded: boolean;
};

export type GameHudPageView = {
  element: HTMLElement;
  statusCard: StatusCardView;
};

export function createGameHudPage(
  props: GameHudPageProps,
): GameHudPageView {
  const statusCard = createStatusCard({
    embedded: props.embedded,
  });

  return {
    element: createHudShell({
      statusCard: statusCard.element,
      crosshair: createCrosshair(),
    }),
    statusCard,
  };
}
