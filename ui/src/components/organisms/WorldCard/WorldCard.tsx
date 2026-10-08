import { useLocalization } from "../../../localization/LocalizationProvider";
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
  thumbnailUrl?: string;
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
  const { t, contentName } = useLocalization();
  return (
    <Surface
      variant="frosted"
      className={world.compatible ? "world-card" : "world-card world-card--no-preview"}
    >
      {world.compatible && (
        <div className="world-card__preview">
          {world.thumbnailUrl && (
            <img src={world.thumbnailUrl} alt="" loading="lazy" />
          )}
        </div>
      )}

      <div className="world-card__info">
        <Text
          text={world.id}
          variant="heading"
        />

        {world.compatible ? (
          <>
            <Metadata
              label={t("ui.lastSaved")}
              value={world.lastSaved}
            />
            <Metadata
              label={t("worldSelection.seed")}
              value={world.seed}
            />
            <Metadata
              label={t("ui.daysPassed")}
              value={world.daysPassed}
            />
            <Metadata
              label={t("ui.sphere")}
              value={world.sphere.startsWith("asteria:") ? contentName(world.sphere) : world.sphere}
            />
            <Metadata
              label={t("worldSelection.coordinates")}
              value={world.coordinates}
            />
          </>
        ) : (
          <Text
            text={t("ui.worldIncompatible")}
            variant="body"
          />
        )}
      </div>

      <div className="world-card__actions">
        {world.compatible && (
          <Button
            label={t("ui.load")}
            variant="primary"
            stretch
            disabled={!onLoad}
            onClick={() =>
              onLoad?.(world.id)
            }
          />
        )}
        <Button
          label={t("ui.delete")}
          variant="danger"
          stretch
          disabled={!onDelete}
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
