import { useRef } from "react";
import { TextInput } from "../TextInput/TextInput";
import "./NumericInput.css";

export type NumericInputProps = {
  value: string;
  min: number;
  max: number;
  ariaLabel: string;
  disabled?: boolean;
  onChange(value: string): void;
  onCommit?(value: number): void;
};

/** Validate the committed value without lossy parsing or exponent notation. */
export function readValidInteger(value: string, min: number, max: number): number | null {
  if (!/^[0-9]+$/.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed >= min && parsed <= max
    ? parsed : null;
}

/**
 * MineClone's digit-only editor, adapted to a native WebUI field.
 * Draft text may be empty while editing, but invalid drafts never commit.
 */
export function NumericInput({
  value, min, max, ariaLabel, disabled = false, onChange, onCommit,
}: NumericInputProps) {
  const lastCommitted = useRef(value);
  const canceling = useRef(false);

  const finish = () => {
    if (canceling.current) {
      canceling.current = false;
      return;
    }
    const parsed = readValidInteger(value, min, max);
    if (parsed === null) {
      onChange(lastCommitted.current);
      return;
    }
    if (value !== lastCommitted.current) {
      lastCommitted.current = value;
      onCommit?.(parsed);
    }
  };

  return (
    <TextInput
      className="ui-numeric-input"
      type="text"
      inputMode="numeric"
      pattern="[0-9]*"
      maxLength={String(max).length}
      autoComplete="off"
      spellCheck={false}
      aria-label={ariaLabel}
      value={value}
      disabled={disabled}
      onFocus={() => {
        canceling.current = false;
        if (readValidInteger(value, min, max) !== null) {
          lastCommitted.current = value;
        }
      }}
      onChange={event => {
        const next = event.target.value;
        if (/^[0-9]*$/.test(next) && next.length <= String(max).length) {
          onChange(next);
        }
      }}
      onBlur={finish}
      onKeyDown={event => {
        if (event.key === "Escape") {
            event.stopPropagation();
          canceling.current = true;
          onChange(lastCommitted.current);
          event.currentTarget.blur();
        } else if (event.key === "Enter") {
          event.preventDefault();
          event.currentTarget.blur();
        }
      }}
    />
  );
}
