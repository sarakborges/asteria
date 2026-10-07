import {
  useEffect,
  useState,
  type FormEvent,
} from "react";
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
              label="Menu principal"
              size="menu"
              className="new-world__create"
              disabled={state.pending}
              onClick={onBack}
            />
            <Button
              label="Criar mundo"
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
          ? "Gerando mundo"
          : "Criar mundo"
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
                text="Preparando terreno, iluminação e regiões próximas ao spawn."
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
                  text="Seed do mundo"
                  variant="setting-title"
                />
                <Text
                  text="Use uma seed para recriar exatamente o mesmo terreno."
                  variant="caption"
                />
                <div className="new-world__seed-row">
                  <TextInput
                    id="world-seed"
                    inputMode="numeric"
                    maxLength={20}
                    spellCheck={false}
                    autoComplete="off"
                    placeholder="Seed de 64 bits"
                    aria-describedby="world-seed-help"
                    value={seed}
                    disabled={state.pending}
                    invalid={Boolean(state.error)}
                    onChange={(event) =>
                      setSeed(event.target.value)
                    }
                  />
                  <Button
                    label="Gerar outra"
                    disabled={state.pending}
                    onClick={onRandomize}
                    className="new-world__randomize"
                  />
                </div>
                <span
                  id="world-seed-help"
                  className="new-world__help"
                >
                  Um número entre 0 e 18446744073709551615.
                </span>
                {state.error && (
                  <span
                    className="new-world__error"
                    role="alert"
                  >
                    {state.error}
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
