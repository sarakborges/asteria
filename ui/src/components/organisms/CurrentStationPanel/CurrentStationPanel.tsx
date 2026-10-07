import type { CurrentStationView } from "../../../presentation/inventoryModels";
import { ItemGlyph } from "../../atoms/ItemGlyph/ItemGlyph";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import "./CurrentStationPanel.css";

export type CurrentStationPanelProps = {
  station: CurrentStationView;
};

export function CurrentStationPanel({
  station,
}: CurrentStationPanelProps) {
  return (
    <Surface
      variant="hud"
      className="current-station-panel"
    >
      <Text
        text="Current Station"
        variant="heading"
      />

      <div className="current-station-panel__preview">
        <div className="current-station-panel__icon-frame">
          <ItemGlyph
            item={{
              id:
                "asteria:station/" +
                station.name,
              name:
                station.name,
              iconUrl:
                station.iconUrl,
            }}
            size="station"
          />
        </div>
      </div>

      <div className="current-station-panel__copy">
        <Text
          text={station.eyebrow}
          variant="caption"
        />
        <Text
          text={station.name}
          variant="setting-title"
        />
        <Text
          text={station.description}
          variant="detail"
        />
      </div>
    </Surface>
  );
}
