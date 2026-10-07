import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  ChatMessageView,
  ChatSuggestionView,
} from "../../../presentation/chatModels";
import { TextInput } from "../../atoms/TextInput/TextInput";
import "./ChatPanel.css";

export type ChatPanelProps = {
  open: boolean;
  visible: boolean;
  history: readonly ChatMessageView[];
  suggestions?: readonly ChatSuggestionView[];
  selectedSuggestionIndex?: number;
  draft: string;
  onDraftChange?(value: string): void;
  onSubmit?(): void;
  onMessageAction?(messageId: string): void;
};

export function ChatPanel({
  open,
  visible,
  history,
  suggestions = [],
  selectedSuggestionIndex = 0,
  draft,
  onDraftChange,
  onSubmit,
  onMessageAction,
}: ChatPanelProps) {
  const { t } = useLocalization();
  if (!visible) return null;

  return (
    <section className="chat-panel">
      <div className="chat-panel__history">
        {history.map((message) => {
          const tone =
            message.tone ??
            "normal";

          return (
            <div
              key={message.id}
              className={
                "chat-panel__message chat-panel__message--" +
                tone
              }
            >
              <span>
                {message.text}
              </span>
              {message.actionLabel && (
                <button
                  type="button"
                  className="chat-panel__link"
                  onClick={() =>
                    onMessageAction?.(
                      message.id,
                    )
                  }
                >
                  {
                    message.actionLabel
                  }
                </button>
              )}
            </div>
          );
        })}
      </div>

      {open &&
        suggestions.length >
          0 && (
          <div className="chat-panel__suggestions">
            <span className="chat-panel__suggestion-hint">
              {t("ui.chatHint")}
            </span>
            {suggestions
              .slice(0, 7)
              .map(
                (
                  suggestion,
                  index,
                ) => (
                  <div
                    key={
                      suggestion.value
                    }
                    className={[
                      "chat-panel__suggestion",
                      index ===
                      selectedSuggestionIndex
                        ? "chat-panel__suggestion--selected"
                        : "",
                    ]
                      .filter(
                        Boolean,
                      )
                      .join(
                        " ",
                      )}
                  >
                    <strong>
                      {
                        suggestion.value
                      }
                    </strong>
                    <span>
                      {
                        suggestion.description
                      }
                    </span>
                  </div>
                ),
              )}
          </div>
        )}

      {open && (
        <form
          className="chat-panel__input"
          onSubmit={(event) => {
            event.preventDefault();
            onSubmit?.();
          }}
        >
          <span aria-hidden="true">
            &gt;
          </span>
          <TextInput
            value={draft}
            maxLength={512}
            aria-label={t("ui.chatMessage")}
            onChange={(event) =>
              onDraftChange?.(
                event.target.value,
              )
            }
          />
        </form>
      )}
    </section>
  );
}
