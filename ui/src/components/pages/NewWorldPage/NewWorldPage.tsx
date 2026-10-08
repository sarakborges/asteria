import {
  useEffect,
  useState,
  type FormEvent,
} from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { WorldCreationState } from "../../../state/uiState";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { TextInput } from "../../atoms/TextInput/TextInput";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import { ScreenShell } from "../../templates/ScreenShell/ScreenShell";
import "./NewWorldPage.css";

export type NewWorldPageProps = {
  state: WorldCreationState;
  onBack(): void;
  onCreate(seed: string): void;
  onRandomize(): void;
};

export function NewWorldPage({
  state,
  onBack,
  onCreate,
  onRandomize,
}: NewWorldPageProps) {
  const { t } = useLocalization();
  const [seed, setSeed] =
    useState(state.seed);

  useEffect(() => {
    setSeed(state.seed);
  }, [state.seed]);

  if (!state.visible) return null;

  const submit = (
    event: FormEvent<HTMLFormElement>,
  ): void => {
    event.preventDefault();
    onCreate(seed.trim());
  };

  const footer =
    state.generating
      ? undefined
      : (
          <>
            <Button
              label={t("newWorld.backToMenu")}
              size="menu"
              className="new-world__create"
              disabled={state.pending}
              onClick={onBack}
            />
            <Button
              label={t("newWorld.creating")}
              variant="primary"
              size="menu"
              className="new-world__create"
              disabled={state.pending}
              onClick={() =>
                onCreate(seed.trim())
              }
            />
          </>
        );

  return (
    <ScreenShell
      title={
        state.generating
          ? t("newWorld.generating")
          : t("newWorld.creating")
      }
      background={<CosmicBackground />}
      footer={footer}
      className={
        state.generating
          ? "new-world new-world--generating"
          : "new-world"
      }
    >
      <div className="new-world__content">
        <Surface
          variant="frosted"
          className="new-world__panel"
        >
          {state.generating ? (
            <div className="new-world__generating-copy">
              <Text
                text={t("newWorld.preparing")}
                variant="body"
              />
            </div>
          ) : (
            <form
              className="new-world__form"
              onSubmit={submit}
            >
              <div className="new-world__setting">
                <Text
                  text={t("newWorld.seedLabel")}
                  variant="setting-title"
                />
                <Text
                  text={t("newWorld.seedHint")}
                  variant="caption"
                />
                <div className="new-world__seed-row">
                  <TextInput
                    id="world-seed"
                    inputMode="numeric"
                    maxLength={20}
                    spellCheck={false}
                    autoComplete="off"
                    placeholder={t("newWorld.seedPlaceholder")}
                    aria-describedby="world-seed-help"
                    value={seed}
                    disabled={state.pending}
                    invalid={Boolean(state.errorKey)}
                    onChange={(event) =>
                      setSeed(event.target.value)
                    }
                  />
                  <Button
                    label={t("newWorld.randomize")}
                    disabled={state.pending}
                    onClick={onRandomize}
                    className="new-world__randomize"
                  />
                </div>
                <span
                  id="world-seed-help"
                  className="new-world__help"
                >
                  {t("newWorld.seedRange")}
                </span>
                {state.errorKey && (
                  <span
                    className="new-world__error"
                    role="alert"
                  >
                    {t(state.errorKey)}
                  </span>
                )}
              </div>
            </form>
          )}
        </Surface>
      </div>
    </ScreenShell>
  );
}
