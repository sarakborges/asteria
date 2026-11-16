import type {
  ChangeEventHandler,
} from "react";
import "./Slider.css";

export type SliderProps = {
  value: number;
  min: number;
  max: number;
  step?: number;
  disabled?: boolean;
  ariaLabel: string;
  onChange?: ChangeEventHandler<HTMLInputElement>;
};

export function Slider({
  value,
  min,
  max,
  step = 1,
  disabled = false,
  ariaLabel,
  onChange,
}: SliderProps) {
  return (
    <input
      className="ui-slider"
      type="range"
      value={value}
      min={min}
      max={max}
      step={step}
      disabled={disabled}
      aria-label={ariaLabel}
      onChange={onChange}
    />
  );
}
