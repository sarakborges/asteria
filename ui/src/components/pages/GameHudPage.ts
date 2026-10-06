import { createCrosshair } from "../atoms/Crosshair";
import {
  createInteractionPrompt,
  type InteractionPromptView,
} from "../molecules/InteractionPrompt";
import {
  createHotbar,
  type HotbarView,
} from "../organisms/Hotbar";
import {
  createPlayerVitals,
  type PlayerVitalsView,
} from "../organisms/PlayerVitals";
import {
  createStatusCard,
  type StatusCardView,
} from "../organisms/StatusCard";
import {
  createStatusEffects,
  type StatusEffectsView,
} from "../organisms/StatusEffects";
import {
  createToastStack,
  type ToastStackView,
} from "../organisms/ToastStack";
import {
  createHudShell,
  type HudShellView,
} from "../templates/HudShell";

export type GameHudPageProps = {
  embedded: boolean;
};

export type GameHudPageView = {
  element: HTMLElement;
  shell: HudShellView;
  hotbar: HotbarView;
  playerVitals: PlayerVitalsView;
  statusEffects: StatusEffectsView;
  interactionPrompt: InteractionPromptView;
  toasts: ToastStackView;
  statusCard: StatusCardView;
};

export function createGameHudPage(
  props: GameHudPageProps,
): GameHudPageView {
  const hotbar = createHotbar();
  const playerVitals = createPlayerVitals();
  const statusEffects = createStatusEffects();
  const interactionPrompt = createInteractionPrompt();
  const toasts = createToastStack();
  const statusCard = createStatusCard({
    embedded: props.embedded,
  });

  hotbar.setState({
    slots: [],
    selectedIndex: null,
  });

  const shell = createHudShell({
    crosshair: createCrosshair(),
    interactionPrompt: interactionPrompt.element,
    hotbar: hotbar.element,
    playerVitals: playerVitals.element,
    statusEffects: statusEffects.element,
    toastStack: toasts.element,
    debugOverlay: statusCard.element,
  });

  return {
    element: shell.element,
    shell,
    hotbar,
    playerVitals,
    statusEffects,
    interactionPrompt,
    toasts,
    statusCard,
  };
}
