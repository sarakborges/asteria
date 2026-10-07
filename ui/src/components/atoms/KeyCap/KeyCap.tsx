import "./KeyCap.css";

export type KeyCapProps = {
  label: string;
};

export function KeyCap({
  label,
}: KeyCapProps) {
  return (
    <kbd className="ui-key-cap">
      {label}
    </kbd>
  );
}
