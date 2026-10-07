import type {
  ChangeEventHandler,
} from "react";
import "./Select.css";

export type SelectOption = {
  value: string;
  label: string;
};

export type SelectProps = {
  value: string;
  options: readonly SelectOption[];
  disabled?: boolean;
  ariaLabel: string;
  onChange?: ChangeEventHandler<HTMLSelectElement>;
};

export function Select({
  value,
  options,
  disabled = false,
  ariaLabel,
  onChange,
}: SelectProps) {
  return (
    <label className="ui-select">
      <select
        value={value}
        disabled={disabled}
        aria-label={ariaLabel}
        onChange={onChange}
      >
        {options.map((option) => (
          <option
            key={option.value}
            value={option.value}
          >
            {option.label}
          </option>
        ))}
      </select>
      <span
        className="ui-select__chevron"
        aria-hidden="true"
      />
    </label>
  );
}
