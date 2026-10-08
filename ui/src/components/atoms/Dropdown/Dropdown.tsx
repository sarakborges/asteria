import { useEffect, useId, useRef, useState, type CSSProperties } from "react";
import type { SelectOption } from "../Select/Select";
import "./Dropdown.css";

export type DropdownProps = {
  value: string;
  options: readonly SelectOption[];
  ariaLabel: string;
  disabled?: boolean;
  onChange(value: string): void;
};

/** A MineClone-style anchored selector. Focus stays within this UI control. */
export function Dropdown({
  value, options, ariaLabel, disabled = false, onChange,
}: DropdownProps) {
  const panelId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const optionRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const [placement, setPlacement] = useState<CSSProperties>({});
  const selectedIndex = options.findIndex(option => option.value === value);
  const selected = options[selectedIndex];
  const inactive = disabled || options.length === 0;

  useEffect(() => {
    if (inactive) setOpen(false);
  }, [inactive]);

  useEffect(() => {
    if (open) optionRefs.current[Math.min(activeIndex, options.length - 1)]?.focus();
  }, [open, activeIndex, options.length]);

  const show = (index: number) => {
    if (inactive) return;
    const rect = triggerRef.current?.getBoundingClientRect();
    if (!rect) return;
    const height = Math.min(340, options.length * 40 + 16);
    const below = window.innerHeight - rect.bottom;
    const top = below >= height + 6 || below >= rect.top - 6
      ? rect.bottom + 6
      : rect.top - height - 6;
    setPlacement({
      top: Math.max(6, top),
      left: Math.max(6, Math.min(rect.left, window.innerWidth - rect.width - 6)),
      width: Math.min(rect.width, window.innerWidth - 12),
    });
    setActiveIndex(Math.max(0, Math.min(index, options.length - 1)));
    setOpen(true);
  };

  const choose = (next: string) => {
    if (next !== value) onChange(next);
    setOpen(false);
    triggerRef.current?.focus();
  };

  return (
    <div className="ui-dropdown" onBlurCapture={event => {
      if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
        setOpen(false);
      }
    }}>
      <button
        ref={triggerRef}
        className="ui-dropdown__control"
        type="button"
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        aria-controls={open ? panelId : undefined}
        aria-expanded={open}
        disabled={inactive}
        onClick={() => open ? setOpen(false) : show(Math.max(0, selectedIndex))}
        onKeyDown={event => {
          if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            show(Math.max(0, selectedIndex));
          } else if (event.key === "Escape") {
            setOpen(false);
          }
        }}
      >
        <span className="ui-dropdown__label">{selected?.label ?? ""}</span>
        <span className="ui-dropdown__chevron" aria-hidden="true" />
      </button>
      {open && (
        <div
          id={panelId}
          role="listbox"
          aria-label={ariaLabel}
          className="ui-dropdown__panel"
          style={placement}
        >
          {options.map((option, index) => (
            <button
              key={option.value}
              ref={element => { optionRefs.current[index] = element; }}
              type="button"
              role="option"
              aria-selected={option.value === value}
              className={"ui-dropdown__option" +
                (option.value === value ? " ui-dropdown__option--selected" : "")}
              tabIndex={activeIndex === index ? 0 : -1}
              onClick={() => choose(option.value)}
              onKeyDown={event => {
                if (event.key === "Escape") {
                  event.preventDefault();
                  setOpen(false);
                  triggerRef.current?.focus();
                } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                  event.preventDefault();
                  setActiveIndex((index + (event.key === "ArrowDown" ? 1 : -1) +
                    options.length) % options.length);
                } else if (event.key === "Home" || event.key === "End") {
                  event.preventDefault();
                  setActiveIndex(event.key === "Home" ? 0 : options.length - 1);
                }
              }}
            >
              {option.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
