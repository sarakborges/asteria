import { useLocalization } from "../../../localization/LocalizationProvider";
import type { InteractionPromptState } from "../../../state/uiState";
import "./InteractionPrompt.css";

export type InteractionPromptProps = {
  prompt: InteractionPromptState;
};

export function InteractionPrompt({
  prompt,
}: InteractionPromptProps) {
  const { t } = useLocalization();
  const text = prompt?.text;
  return (
    <div
      className="interaction-prompt"
      hidden={prompt === null}
    >
      <kbd className="interaction-prompt__key">
        {prompt?.key ?? ""}
      </kbd>
      <span className="interaction-prompt__text">
        {text?.startsWith("hud.hint.") ? t(text) : text ?? ""}
      </span>
    </div>
  );
}
