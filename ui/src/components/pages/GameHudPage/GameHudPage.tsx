import type { HudState } from "../../../state/uiState";
import { Crosshair } from "../../atoms/Crosshair/Crosshair";
import { FpsCounter } from "../../atoms/FpsCounter/FpsCounter";
import { InteractionPrompt } from "../../molecules/InteractionPrompt/InteractionPrompt";
import { Hotbar } from "../../organisms/Hotbar/Hotbar";
import { HudEntityCard } from "../../organisms/HudEntityCard/HudEntityCard";
import { PlayerVitals } from "../../organisms/PlayerVitals/PlayerVitals";
import { StatusCard } from "../../organisms/StatusCard/StatusCard";
import { StatusEffects } from "../../organisms/StatusEffects/StatusEffects";
import { TargetHud } from "../../organisms/TargetHud/TargetHud";
import { ToastStack } from "../../organisms/ToastStack/ToastStack";
import { WorldBanner } from "../../organisms/WorldBanner/WorldBanner";
import { WorldClock } from "../../organisms/WorldClock/WorldClock";
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
  const targetOverlay =
    state.targetEntity ? (
      <HudEntityCard
        entity={state.targetEntity}
      />
    ) : (
      <TargetHud
        state={state.target}
      />
    );

  return (
    <HudShell
      debugVisible={state.debugVisible}
      crosshair={<Crosshair />}
      interactionPrompt={
        <InteractionPrompt
          prompt={state.prompt}
        />
      }
      targetOverlay={targetOverlay}
      worldBanner={
        <WorldBanner
          state={state.world}
        />
      }
      worldClock={
        <WorldClock
          state={state.clock}
        />
      }
      hotbar={
        <Hotbar
          state={state.hotbar}
        />
      }
      playerHud={
        <PlayerVitals
          state={state.vitals}
        />
      }
      statusEffects={
        <StatusEffects
          effects={state.effects}
        />
      }
      toastStack={
        <ToastStack
          toasts={state.toasts}
          onDismiss={
            onDismissToast
          }
        />
      }
      fpsCounter={
        <FpsCounter
          fps={state.fps}
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
