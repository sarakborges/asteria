import { useRef } from "react";
import { TextInput } from "../TextInput/TextInput";
import "./UInt64Input.css";

const MAX_UINT64 = 18446744073709551615n;
const DIGITS_ONLY = /^[0-9]*$/;
/** Preserve 64-bit seed precision by keeping decimal strings throughout WebUI. */
export function isUInt64Seed(value: string): boolean {
  if (!DIGITS_ONLY.test(value) || value.length > 20) return false;
  return value === "" || BigInt(value) <= MAX_UINT64;
}
export type UInt64InputProps = {
  id: string; ariaLabel: string; descriptionId?: string;
  placeholder?: string; value: string; disabled?: boolean;
  invalid?: boolean; onChange(value: string): void;
};
export function UInt64Input({ id, ariaLabel, descriptionId, placeholder, value,
  disabled = false, invalid = false, onChange }: UInt64InputProps) {
  const previous = useRef(value);
  return <TextInput id={id} className="ui-uint64-input" type="text"
    inputMode="numeric" pattern="[0-9]*" maxLength={20}
    aria-label={ariaLabel} aria-describedby={descriptionId}
    spellCheck={false} autoComplete="off" placeholder={placeholder}
    value={value} disabled={disabled} invalid={invalid}
    onFocus={() => { if (isUInt64Seed(value)) previous.current = value; }}
    onChange={event => { if (isUInt64Seed(event.target.value)) onChange(event.target.value); }}
    onKeyDown={event => {
      if (event.key === "Escape") { onChange(previous.current); event.currentTarget.blur(); }
    }}
  />;
}
