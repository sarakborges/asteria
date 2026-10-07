import type { ReactNode } from "react";
import { Text } from "../../atoms/Text/Text";
import "./SettingRow.css";

export type SettingRowProps = {
  title: string;
  description?: string;
  control: ReactNode;
};

export function SettingRow({
  title,
  description,
  control,
}: SettingRowProps) {
  return (
    <div className="setting-row">
      <div className="setting-row__copy">
        <Text
          text={title}
          variant="setting-title"
        />
        {description && (
          <Text
            text={description}
            variant="caption"
          />
        )}
      </div>
      <div className="setting-row__control">
        {control}
      </div>
    </div>
  );
}
