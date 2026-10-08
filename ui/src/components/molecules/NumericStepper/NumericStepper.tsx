import { Button } from "../../atoms/Button/Button";
import { TextInput } from "../../atoms/TextInput/TextInput";
import "./NumericStepper.css";

export type NumericStepperProps = {
  value: string;
  min: number;
  max: number;
  ariaLabel: string;
  disabled?: boolean;
  onChange(value: string): void;
  onCommit?(value: number): void;
};

function readValidInteger(value: string, min: number, max: number): number | null {
  if (!/^[0-9]+$/.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed >= min && parsed <= max
    ? parsed : null;
}

export function NumericStepper({
  value, min, max, ariaLabel, disabled = false, onChange, onCommit,
}: NumericStepperProps) {
  const valid = readValidInteger(value, min, max);
  const step = (direction: -1 | 1) => {
    const base = valid ?? min;
    const next = Math.min(max, Math.max(min, base + direction));
    onChange(String(next));
    onCommit?.(next);
  };

  return (
    <div className="numeric-stepper">
      <Button label="−" ariaLabel={ariaLabel + ": −1"}
        disabled={disabled || valid === min}
        onClick={() => step(-1)} />
      <TextInput
        type="number"
        inputMode="numeric"
        aria-label={ariaLabel}
        min={min}
        max={max}
        step={1}
        value={value}
        disabled={disabled}
        onChange={event => onChange(event.target.value)}
        onBlur={() => {
          const committed = readValidInteger(value, min, max);
          if (committed !== null) onCommit?.(committed);
        }}
        onKeyDown={event => {
          if (event.key === "Enter" && onCommit) {
            event.preventDefault();
            event.currentTarget.blur();
          }
        }}
      />
      <Button label="+" ariaLabel={ariaLabel + ": +1"}
        disabled={disabled || valid === max}
        onClick={() => step(1)} />
    </div>
  );
}
