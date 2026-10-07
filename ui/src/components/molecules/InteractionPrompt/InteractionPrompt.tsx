import type { InteractionPromptState } from "../../../state/uiState";
import "./InteractionPrompt.css";

export type InteractionPromptProps = {
  prompt: InteractionPromptState;
};

export function InteractionPrompt({
  prompt,
}: InteractionPromptProps) {
  return (
    <div
      className="interaction-prompt"
      hidden={prompt === null}
    >
      <kbd className="interaction-prompt__key">
        {prompt?.key ?? ""}
      </kbd>
      <span className="interaction-prompt__text">
        {prompt?.text ?? ""}
      </span>
    </div>
  );
}
