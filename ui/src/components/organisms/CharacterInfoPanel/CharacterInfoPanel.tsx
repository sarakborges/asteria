import type { CharacterInfoView } from "../../../presentation/inventoryModels";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import "./CharacterInfoPanel.css";

export type CharacterInfoPanelProps = {
  state: CharacterInfoView;
};

export function CharacterInfoPanel({
  state,
}: CharacterInfoPanelProps) {
  const maximum =
    state.healthMaximum > 0
      ? state.healthMaximum
      : 1;
  const ratio = Math.min(
    1,
    Math.max(
      0,
      state.healthCurrent /
        maximum,
    ),
  );

  return (
    <Surface
      variant="hud"
      className="character-info-panel"
    >
      <div className="character-info-panel__preview">
        <span aria-hidden="true">PLAYER</span>
      </div>

      <div className="character-info-panel__details">
        <Text
          text={state.name}
          variant="heading"
        />

        <section className="character-info-panel__section">
          <Text
            text="Health"
            variant="setting-title"
          />
          <div className="character-info-panel__health">
            <span
              className="character-info-panel__health-fill"
              style={{
                width:
                  ratio *
                    100 +
                  "%",
              }}
            />
            <span className="character-info-panel__health-label">
              {Math.round(
                state.healthCurrent,
              )}{" "}
              /{" "}
              {Math.round(
                state.healthMaximum,
              )}
            </span>
          </div>
        </section>

        <section className="character-info-panel__section">
          <Text
            text="Armor"
            variant="setting-title"
          />
          <div className="character-info-panel__equipment">
            {state.equipment.map(
              (entry) => (
                <div
                  key={
                    entry.slot
                  }
                  className="character-info-panel__equipment-row"
                >
                  <InventorySlot
                    item={
                      entry.item
                    }
                    disabled
                  />
                  <div className="character-info-panel__equipment-copy">
                    <Text
                      text={
                        entry.item
                          ?.name ??
                        entry.emptyLabel
                      }
                      variant="caption"
                    />
                    <Text
                      text={
                        entry.effectLabel ??
                        "No effect"
                      }
                      variant="caption"
                    />
                  </div>
                </div>
              ),
            )}
          </div>
        </section>
      </div>
    </Surface>
  );
}
