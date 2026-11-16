import { Button } from "../../atoms/Button/Button";
import "./ModePicker.css";

export type ModePickerProps<TMode extends string> = {
  value: TMode;
  modes: readonly TMode[];
  label: string;
  labelFor(mode: TMode): string;
  disabled?: boolean;
  className?: string;
  onChange(mode: TMode): void;
};

/** Shared equal-width choice group for game mode and world generation type. */
export function ModePicker<TMode extends string>({
  value, modes, label, labelFor, disabled = false, className, onChange,
}: ModePickerProps<TMode>) {
  return (
    <div className={["mode-picker", className].filter(Boolean).join(" ")}
      role="group" aria-label={label}>
      {modes.map(mode => (
        <Button key={mode} label={labelFor(mode)}
          variant={mode === value ? "primary" : "normal"}
          ariaPressed={mode === value}
          disabled={disabled}
          onClick={() => onChange(mode)} />
      ))}
    </div>
  );
}
