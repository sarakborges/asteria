import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import { TextInput } from "../../atoms/TextInput/TextInput";
import "./InventoryPanelHeader.css";

export type InventoryPanelHeaderProps = {
  title: string;
  search: string;
  searchLabel: string;
  sortLabel: string;
  onSearchChange?(value: string): void;
  onSort?(): void;
};

export function InventoryPanelHeader({
  title, search, searchLabel, sortLabel, onSearchChange, onSort,
}: InventoryPanelHeaderProps) {
  return (
    <header className="inventory-panel-header">
      <Text text={title} variant="heading" />
      <div className="inventory-panel-header__controls">
        <TextInput value={search} maxLength={128}
          placeholder={searchLabel} aria-label={searchLabel}
          readOnly={!onSearchChange}
          onChange={event => onSearchChange?.(event.target.value)} />
        <Button label="⇅" className="inventory-panel-header__sort"
          ariaLabel={sortLabel} disabled={!onSort} onClick={onSort} />
      </div>
    </header>
  );
}
