import { Button } from "../../atoms/Button/Button";
import { NumericInput, readValidInteger } from "../../atoms/NumericInput/NumericInput";
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
      <NumericInput
        ariaLabel={ariaLabel}
        min={min}
        max={max}
        value={value}
        disabled={disabled}
        onChange={onChange}
        onCommit={onCommit}
      />
      <Button label="+" ariaLabel={ariaLabel + ": +1"}
        disabled={disabled || valid === max}
        onClick={() => step(1)} />
    </div>
  );
}
