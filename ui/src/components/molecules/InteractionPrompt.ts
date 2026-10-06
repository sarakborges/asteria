import "./InteractionPrompt.css";

export type InteractionPromptState = {
  key: string;
  text: string;
} | null;

export type InteractionPromptView = {
  element: HTMLElement;
  setPrompt(prompt: InteractionPromptState): void;
};

export function createInteractionPrompt(): InteractionPromptView {
  const root = document.createElement("div");
  root.className = "interaction-prompt";
  root.hidden = true;

  const key = document.createElement("kbd");
  key.className = "interaction-prompt__key";

  const text = document.createElement("span");
  text.className = "interaction-prompt__text";

  root.append(key, text);

  return {
    element: root,
    setPrompt(prompt) {
      root.hidden = prompt === null;
      if (prompt === null) {
        key.textContent = "";
        text.textContent = "";
        return;
      }

      key.textContent = prompt.key;
      text.textContent = prompt.text;
    },
  };
}
