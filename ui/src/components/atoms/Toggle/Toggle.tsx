import "./Toggle.css";

export type ToggleProps = {
  checked: boolean;
  disabled?: boolean;
  ariaLabel: string;
  onChange?(checked: boolean): void;
};

export function Toggle({
  checked,
  disabled = false,
  ariaLabel,
  onChange,
}: ToggleProps) {
  return (
    <button
      type="button"
      className={[
        "ui-toggle",
        checked ? "ui-toggle--checked" : "",
      ]
        .filter(Boolean)
        .join(" ")}
      role="switch"
      aria-checked={checked}
      aria-label={ariaLabel}
      disabled={disabled}
      onClick={() => onChange?.(!checked)}
    >
      <span className="ui-toggle__thumb" />
    </button>
  );
}
