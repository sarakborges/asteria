import type { HudState, InventoryCatalogEntry } from "../../../state/uiState";
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
  catalog?: InventoryCatalogEntry[];
  onPing(): void;
  onDismissToast(id: number): void;
};

export function GameHudPage({
  embedded,
  state,
  catalog = [],
  onPing,
  onDismissToast,
}: GameHudPageProps) {
  const spectator = state.gameMode === "spectator";
  const targetOverlay =
    spectator ? null : state.targetEntity ? (
      <HudEntityCard
        entity={state.targetEntity}
      />
    ) : (
      <TargetHud
        state={state.target}
        miningProgress={state.miningProgress}
        artisansKitResolution={state.artisansKitResolution}
      />
    );

  return (
    <HudShell
      debugVisible={state.debugVisible}
      crosshair={spectator ? null : <Crosshair />}
      interactionPrompt={
        spectator ? null : (
          <InteractionPrompt prompt={state.prompt} />
        )
      }
      targetOverlay={state.targetPosition === "Hidden" ? null : targetOverlay}
      targetPosition={state.targetPosition ?? "Center"}
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
      hotbar={spectator ? null : (
        <Hotbar state={{
          ...state.hotbar,
          slots: state.hotbar.slots.map(slot => ({
            ...slot,
            iconUrl: catalog.find(choice =>
              choice.id === slot.id &&
              choice.kind === slot.kind &&
              Object.keys(choice.metadata).length ===
                Object.keys(slot.metadata ?? {}).length &&
              Object.entries(choice.metadata).every(([key, value]) =>
                slot.metadata?.[key] === value))?.iconUrl,
          })),
        }} />
      )}
      playerHud={spectator ? null : (
        <PlayerVitals state={state.vitals} />
      )}
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
