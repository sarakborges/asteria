import type { HudState } from "../../../state/uiState";
import { Crosshair } from "../../atoms/Crosshair/Crosshair";
import { InteractionPrompt } from "../../molecules/InteractionPrompt/InteractionPrompt";
import { Hotbar } from "../../organisms/Hotbar/Hotbar";
import { PlayerVitals } from "../../organisms/PlayerVitals/PlayerVitals";
import { StatusCard } from "../../organisms/StatusCard/StatusCard";
import { StatusEffects } from "../../organisms/StatusEffects/StatusEffects";
import { ToastStack } from "../../organisms/ToastStack/ToastStack";
import { WorldBanner } from "../../organisms/WorldBanner/WorldBanner";
import { HudShell } from "../../templates/HudShell/HudShell";

export type GameHudPageProps = {
  embedded: boolean;
  state: HudState;
  onPing(): void;
  onDismissToast(id: number): void;
};

export function GameHudPage({
  embedded,
  state,
  onPing,
  onDismissToast,
}: GameHudPageProps) {
  return (
    <HudShell
      debugVisible={state.debugVisible}
      crosshair={<Crosshair />}
      interactionPrompt={
        <InteractionPrompt prompt={state.prompt} />
      }
      worldBanner={
        <WorldBanner state={state.world} />
      }
      hotbar={<Hotbar state={state.hotbar} />}
      playerVitals={
        <PlayerVitals state={state.vitals} />
      }
      statusEffects={
        <StatusEffects effects={state.effects} />
      }
      toastStack={
        <ToastStack
          toasts={state.toasts}
          onDismiss={onDismissToast}
        />
      }
      debugOverlay={
        <StatusCard
          embedded={embedded}
          state={state.statusCard}
          onPing={onPing}
        />
      }
    />
  );
}
