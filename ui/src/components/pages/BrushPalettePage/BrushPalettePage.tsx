import { useLocalization } from "../../../localization/LocalizationProvider";
import type { BrushPaletteState } from "../../../state/uiState";
import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import "./BrushPalettePage.css";

export type BrushPalettePageProps = {
  state: BrushPaletteState;
  onSelect(id: string | null): void;
  onClose(): void;
};

export function BrushPalettePage({
  state, onSelect, onClose,
}: BrushPalettePageProps) {
  const { t, contentName } = useLocalization();
  return (
    <main className="brush-palette" aria-label={t("brush.palette.title")}>
      <section className="brush-palette__panel">
        <header className="brush-palette__header">
          <Text text={t("brush.palette.title")} variant="heading" />
          <Button label={t("brush.palette.close")} onClick={onClose} />
        </header>
        <Text text={t("brush.palette.help")} variant="detail" />
        <button
          type="button"
          className={"brush-palette__clear" +
            (state.selectedId === null ? " brush-palette__selected" : "")}
          aria-pressed={state.selectedId === null}
          onClick={() => onSelect(null)}
        >
          {t("brush.palette.clear")}
        </button>
        <div className="brush-palette__grid">
          {state.colors.map(color => (
            <button
              key={color.id}
              type="button"
              className={"brush-palette__swatch" +
                (state.selectedId === color.id ? " brush-palette__selected" : "")}
              style={{ backgroundColor: color.rgb }}
              aria-label={contentName(color.id)}
              title={contentName(color.id)}
              aria-pressed={state.selectedId === color.id}
              onClick={() => onSelect(color.id)}
            />
          ))}
        </div>
      </section>
    </main>
  );
}
