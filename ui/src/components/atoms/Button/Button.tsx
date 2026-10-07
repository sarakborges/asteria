import type { MouseEventHandler } from "react";
import "./Button.css";

export type ButtonProps = {
  label: string;
  disabled?: boolean;
  dataUi?: string;
  type?: "button" | "submit";
  onClick?: MouseEventHandler<HTMLButtonElement>;
};

export function Button({
  label,
  disabled = false,
  dataUi,
  type = "button",
  onClick,
}: ButtonProps) {
  return (
    <button
      type={type}
      className="ui-button"
      disabled={disabled}
      data-ui={dataUi}
      onClick={onClick}
    >
      {label}
    </button>
  );
}
