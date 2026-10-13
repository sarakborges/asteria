import { useRef } from "react";
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
  const steppingPointer = useRef(false);
  const valid = readValidInteger(value, min, max);
  const step = (direction: -1 | 1) => {
    // Invalid/empty drafts step to the lower bound, not min + 1.
    const base = valid ?? (direction === 1 ? min - 1 : min + 1);
    const next = Math.min(max, Math.max(min, base + direction));
    onChange(String(next));
    onCommit?.(next);
  };

  return (
    <div className="numeric-stepper"
      onPointerDownCapture={event => {
        if (event.button === 0 && event.target instanceof Element &&
            event.target.closest("button"))
          steppingPointer.current = true;
      }}
      onPointerUpCapture={() => { steppingPointer.current = false; }}
      onPointerCancelCapture={() => { steppingPointer.current = false; }}>
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
        deferBlurCommit={() => steppingPointer.current}
      />
      <Button label="+" ariaLabel={ariaLabel + ": +1"}
        disabled={disabled || valid === max}
        onClick={() => step(1)} />
    </div>
  );
}
