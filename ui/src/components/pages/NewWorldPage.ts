import "./NewWorldPage.css";

export type NewWorldPageView = {
  element: HTMLElement;
  form: HTMLFormElement;
  seedInput: HTMLInputElement;
  randomizeButton: HTMLButtonElement;
  setSuggestedSeed(seed: string): void;
  setPending(pending: boolean): void;
  setError(message: string): void;
  setGenerating(seed: string): void;
  hide(): void;
};

export function createNewWorldPage(): NewWorldPageView {
  const root = document.createElement("section");
  root.className = "new-world";

  const card = document.createElement("div");
  card.className = "new-world__card";

  const eyebrow = document.createElement("p");
  eyebrow.className = "new-world__eyebrow";
  eyebrow.textContent = "ASTERIA / WORLD CREATION";

  const title = document.createElement("h1");
  title.className = "new-world__title";
  title.textContent = "Um novo mundo";

  const description = document.createElement("p");
  description.className = "new-world__description";
  description.textContent =
    "Explore uma Sphere inédita. Use uma seed para recriar exatamente o mesmo terreno.";

  const form = document.createElement("form");
  form.className = "new-world__form";

  const label = document.createElement("label");
  label.className = "new-world__label";
  label.htmlFor = "world-seed";
  label.textContent = "Seed do mundo";

  const field = document.createElement("div");
  field.className = "new-world__field";

  const seedInput = document.createElement("input");
  seedInput.id = "world-seed";
  seedInput.className = "new-world__input";
  seedInput.type = "text";
  seedInput.inputMode = "numeric";
  seedInput.maxLength = 20;
  seedInput.spellcheck = false;
  seedInput.autocomplete = "off";
  seedInput.placeholder = "Seed de 64 bits";
  seedInput.setAttribute("aria-describedby", "world-seed-help");
  seedInput.disabled = true;

  const randomizeButton = document.createElement("button");
  randomizeButton.className = "new-world__randomize";
  randomizeButton.type = "button";
  randomizeButton.textContent = "Gerar outra";
  randomizeButton.disabled = true;

  field.append(seedInput, randomizeButton);

  const help = document.createElement("p");
  help.id = "world-seed-help";
  help.className = "new-world__help";
  help.textContent =
    "Um número entre 0 e 18446744073709551615. Você pode alterar a seed antes de criar.";

  const error = document.createElement("p");
  error.className = "new-world__error";
  error.setAttribute("role", "alert");
  error.hidden = true;

  const createButton = document.createElement("button");
  createButton.className = "new-world__create";
  createButton.type = "submit";
  createButton.textContent = "Criar mundo";
  createButton.disabled = true;

  form.append(label, field, help, error, createButton);
  card.append(eyebrow, title, description, form);
  root.append(card);

  return {
    element: root,
    form,
    seedInput,
    randomizeButton,
    setSuggestedSeed(seed) {
      root.hidden = false;
      root.classList.remove("new-world--generating");
      seedInput.value = seed;
      seedInput.disabled = false;
      randomizeButton.disabled = false;
      createButton.disabled = false;
      error.textContent = "";
      error.hidden = true;
    },
    setPending(pending) {
      seedInput.disabled = pending;
      randomizeButton.disabled = pending;
      createButton.disabled = pending;
    },
    setError(message) {
      root.hidden = false;
      error.textContent = message;
      error.hidden = !message;
      seedInput.disabled = false;
      randomizeButton.disabled = false;
      createButton.disabled = false;
    },
    setGenerating(seed) {
      root.classList.add("new-world--generating");
      title.textContent = "Gerando mundo";
      description.textContent =
        "Preparando terreno, iluminação e regiões próximas ao spawn.";
      seedInput.value = seed;
      seedInput.disabled = true;
      randomizeButton.disabled = true;
      createButton.disabled = true;
      form.hidden = true;
    },
    hide() {
      root.hidden = true;
    },
  };
}
