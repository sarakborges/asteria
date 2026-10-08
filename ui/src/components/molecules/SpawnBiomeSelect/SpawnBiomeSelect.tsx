import { useEffect, useRef, useState, type CSSProperties } from "react";
import { createPortal } from "react-dom";
import type { SpawnBiomeOption } from "../../../state/uiState";
import { TextInput } from "../../atoms/TextInput/TextInput";
import "./SpawnBiomeSelect.css";

export type SpawnBiomeSelectProps = {
  value: string | null;
  options: readonly SpawnBiomeOption[];
  label: string;
  randomLabel: string;
  searchPlaceholder: string;
  disabled?: boolean;
  requireSelection?: boolean;
  onChange(value: string | null): void;
};

/** Searchable MineClone spawn-biome menu, limited to focused UI interactions. */
export function SpawnBiomeSelect({
  value, options, label, randomLabel, searchPlaceholder,
  disabled = false, requireSelection = false, onChange,
}: SpawnBiomeSelectProps) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [placement, setPlacement] = useState<CSSProperties>({});
  const current = options.find(option => option.id === value)?.label ??
    (value ?? (requireSelection ? label : randomLabel));
  const matches = options.filter(option =>
    (option.label + " " + option.id).toLocaleLowerCase()
      .includes(query.trim().toLocaleLowerCase()));

  useEffect(() => {
    if (open) searchRef.current?.focus();
  }, [open]);

  const show = () => {
    if (disabled) return;
    const rect = triggerRef.current?.getBoundingClientRect();
    if (!rect) return;
    const height = 340;
    const below = window.innerHeight - rect.bottom;
    const top = below >= height || below >= rect.top
      ? rect.bottom + 6 : Math.max(6, rect.top - height - 6);
    setPlacement({
      left: Math.max(6, Math.min(rect.left, window.innerWidth - rect.width - 6)),
      top, width: Math.min(rect.width, window.innerWidth - 12),
    });
    setQuery("");
    setOpen(true);
  };

  const choose = (id: string | null) => {
    onChange(id);
    setOpen(false);
    triggerRef.current?.focus();
  };

  return <>
    <button ref={triggerRef} type="button"
      className="spawn-biome-select__trigger"
      disabled={disabled}
      aria-label={label}
      aria-haspopup="dialog"
      aria-expanded={open}
      onClick={() => open ? setOpen(false) : show()}>
      <span>{current}</span><span aria-hidden="true">⌄</span>
    </button>
    {open && createPortal(
      <div className="spawn-biome-select__overlay">
        <button className="spawn-biome-select__backdrop" type="button"
          aria-label="Close biome list" onClick={() => setOpen(false)} />
        <div role="dialog" aria-label={label}
          className="spawn-biome-select__panel" style={placement}>
          <TextInput
            ref={searchRef}
            aria-label={searchPlaceholder}
            placeholder={searchPlaceholder}
            maxLength={128}
            value={query}
            onChange={event => setQuery(event.target.value)}
            onKeyDown={event => {
              if (event.key === "Escape") {
                event.preventDefault();
                setOpen(false);
                triggerRef.current?.focus();
              } else if (event.key === "ArrowDown") {
                event.preventDefault();
                const first = event.currentTarget.parentElement
                  ?.querySelector<HTMLButtonElement>(".spawn-biome-select__option");
                first?.focus();
              }
            }}
          />
          <div className="spawn-biome-select__options" role="listbox" aria-label={label}>
            {!requireSelection && !query && (
              <button type="button" role="option" aria-selected={value === null}
                className="spawn-biome-select__option"
                onClick={() => choose(null)}>{randomLabel}</button>
            )}
            {matches.map(option => (
              <button type="button" key={option.id} role="option"
                aria-selected={value === option.id}
                className="spawn-biome-select__option"
                onClick={() => choose(option.id)}
                onKeyDown={event => {
                  if (event.key === "Escape") {
                    event.preventDefault();
                    setOpen(false);
                    triggerRef.current?.focus();
                  }
                }}>{option.label}</button>
            ))}
          </div>
        </div>
      </div>, document.body,
    )}
  </>;
}
