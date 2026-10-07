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
  disabled?: boolean;
  dataUi?: string;
  type?: "button" | "submit";
  variant?: ButtonVariant;
  size?: ButtonSize;
  stretch?: boolean;
  className?: string;
  onClick?: MouseEventHandler<HTMLButtonElement>;
};

export function Button({
  label,
  disabled = false,
  dataUi,
  type = "button",
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
      className={classes}
      disabled={disabled}
      data-ui={dataUi}
      onClick={onClick}
    >
      {label}
    </button>
  );
}
