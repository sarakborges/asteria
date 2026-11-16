import type { MouseEventHandler } from "react";
import "./Button.css";

export type ButtonVariant =
  | "normal"
  | "primary"
  | "danger";

export type ButtonSize =
  | "compact"
  | "menu";

export type ButtonProps = {
  label: string;
  iconUrl?: string;
  disabled?: boolean;
  dataUi?: string;
  type?: "button" | "submit";
  formId?: string;
  ariaPressed?: boolean;
  ariaLabel?: string;
  variant?: ButtonVariant;
  size?: ButtonSize;
  stretch?: boolean;
  className?: string;
  onClick?: MouseEventHandler<HTMLButtonElement>;
};

export function Button({
  label,
  iconUrl,
  disabled = false,
  dataUi,
  type = "button",
  formId,
  ariaPressed,
  ariaLabel,
  variant = "normal",
  size = "compact",
  stretch = false,
  className,
  onClick,
}: ButtonProps) {
  const classes = [
    "ui-button",
    "ui-button--" + variant,
    "ui-button--" + size,
    stretch ? "ui-button--stretch" : "",
    className,
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <button
      type={type}
      form={formId}
      aria-pressed={ariaPressed}
      aria-label={ariaLabel}
      className={classes}
      disabled={disabled}
      data-ui={dataUi}
      onClick={onClick}
    >
      {iconUrl && <img className="ui-button__icon" src={iconUrl} alt="" aria-hidden="true" />}
      <span>{label}</span>
    </button>
  );
}
