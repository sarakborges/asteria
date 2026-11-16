import type { InputHTMLAttributes, Ref } from "react";
import "./TextInput.css";

export type TextInputProps =
  Omit<InputHTMLAttributes<HTMLInputElement>, "className"> & {
    invalid?: boolean;
    inputRef?: Ref<HTMLInputElement>;
    className?: string;
  };

export function TextInput({
  invalid = false,
  inputRef,
  className,
  ...props
}: TextInputProps) {
  return (
    <input
      ref={inputRef}
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
