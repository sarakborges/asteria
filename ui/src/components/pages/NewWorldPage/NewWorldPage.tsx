import {
  useEffect,
  useState,
  type FormEvent,
} from "react";
import type { WorldCreationState } from "../../../state/uiState";
import "./NewWorldPage.css";

export type NewWorldPageProps = {
  state: WorldCreationState;
  onCreate(seed: string): void;
  onRandomize(): void;
};

export function NewWorldPage({
  state,
  onCreate,
  onRandomize,
}: NewWorldPageProps) {
  const [seed, setSeed] = useState(state.seed);

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

  return (
    <section
      className={[
        "new-world",
        state.generating
          ? "new-world--generating"
          : "",
      ]
        .filter(Boolean)
        .join(" ")}
    >
      <div className="new-world__card">
        <p className="new-world__eyebrow">
          ASTERIA / WORLD CREATION
        </p>
        <h1 className="new-world__title">
          {state.generating
            ? "Gerando mundo"
            : "Um novo mundo"}
        </h1>
        <p className="new-world__description">
          {state.generating
            ? "Preparando terreno, iluminação e regiões próximas ao spawn."
            : "Explore uma Sphere inédita. Use uma seed para recriar exatamente o mesmo terreno."}
        </p>
        <form
          className="new-world__form"
          onSubmit={submit}
          hidden={state.generating}
        >
          <label
            className="new-world__label"
            htmlFor="world-seed"
          >
            Seed do mundo
          </label>
          <div className="new-world__field">
            <input
              id="world-seed"
              className="new-world__input"
              type="text"
              inputMode="numeric"
              maxLength={20}
              spellCheck={false}
              autoComplete="off"
              placeholder="Seed de 64 bits"
              aria-describedby="world-seed-help"
              value={seed}
              disabled={state.pending}
              onChange={(event) =>
                setSeed(event.target.value)
              }
            />
            <button
              className="new-world__randomize"
              type="button"
              disabled={state.pending}
              onClick={onRandomize}
            >
              Gerar outra
            </button>
          </div>
          <p
            id="world-seed-help"
            className="new-world__help"
          >
            Um número entre 0 e 18446744073709551615. Você pode alterar a seed antes de criar.
          </p>
          <p
            className="new-world__error"
            role="alert"
            hidden={!state.error}
          >
            {state.error ?? ""}
          </p>
          <button
            className="new-world__create"
            type="submit"
            disabled={state.pending}
          >
            Criar mundo
          </button>
        </form>
      </div>
    </section>
  );
}
