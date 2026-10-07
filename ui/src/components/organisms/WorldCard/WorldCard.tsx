import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import "./WorldCard.css";

export type WorldSummaryView = {
  id: string;
  lastSaved: string;
  seed: string;
  daysPassed: string;
  sphere: string;
  coordinates: string;
  compatible: boolean;
};

export type WorldCardProps = {
  world: WorldSummaryView;
  onLoad?(id: string): void;
  onDelete?(id: string): void;
};

export function WorldCard({
  world,
  onLoad,
  onDelete,
}: WorldCardProps) {
  return (
    <Surface
      variant="frosted"
      className="world-card"
    >
      <div className="world-card__preview" />

      <div className="world-card__info">
        <Text
          text={world.id}
          variant="heading"
        />

        {world.compatible ? (
          <>
            <Metadata
              label="Last saved"
              value={world.lastSaved}
            />
            <Metadata
              label="Seed"
              value={world.seed}
            />
            <Metadata
              label="Days passed"
              value={world.daysPassed}
            />
            <Metadata
              label="Sphere"
              value={world.sphere}
            />
            <Metadata
              label="Coordinates"
              value={world.coordinates}
            />
          </>
        ) : (
          <Text
            text="This world is incompatible with the current runtime."
            variant="body"
          />
        )}
      </div>

      <div className="world-card__actions">
        {world.compatible && (
          <Button
            label="Load"
            variant="primary"
            stretch
            onClick={() =>
              onLoad?.(world.id)
            }
          />
        )}
        <Button
          label="Delete"
          variant="danger"
          stretch
          onClick={() =>
            onDelete?.(world.id)
          }
        />
      </div>
    </Surface>
  );
}

function Metadata({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div className="world-card__metadata">
      <Text
        text={label}
        variant="caption"
      />
      <Text
        text={value}
        variant="body"
      />
    </div>
  );
}
