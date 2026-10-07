import type { InputHTMLAttributes } from "react";
import "./TextInput.css";

export type TextInputProps =
  Omit<InputHTMLAttributes<HTMLInputElement>, "className"> & {
    invalid?: boolean;
    className?: string;
  };

export function TextInput({
  invalid = false,
  className,
  ...props
}: TextInputProps) {
  return (
    <input
      {...props}
      className={[
        "ui-text-input",
        invalid ? "ui-text-input--invalid" : "",
        className,
      ]
        .filter(Boolean)
        .join(" ")}
      aria-invalid={invalid || undefined}
    />
  );
}
