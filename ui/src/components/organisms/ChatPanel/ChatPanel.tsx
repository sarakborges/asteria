import { useLayoutEffect, useRef } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  ChatMessageView, ChatSuggestionView,
} from "../../../presentation/chatModels";
import { TextInput } from "../../atoms/TextInput/TextInput";
import "./ChatPanel.css";

const MAX_VISIBLE_SUGGESTIONS = 7;
const MAX_VISIBLE_HISTORY = 64;
const MAX_DRAFT_CHARS = 256;

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
  open, visible, history, suggestions = [], selectedSuggestionIndex = 0,
  draft, onDraftChange, onSubmit, onMessageAction,
}: ChatPanelProps) {
  const { t } = useLocalization();
  const historyRef = useRef<HTMLDivElement>(null);
  const messages = history.slice(-MAX_VISIBLE_HISTORY);
  const newestMessageId = messages.at(-1)?.id;

  useLayoutEffect(() => {
    if (visible && historyRef.current) {
      historyRef.current.scrollTop = historyRef.current.scrollHeight;
    }
  }, [visible, newestMessageId]);

  if (!visible) return null;

  const selected = Math.max(0, Math.min(selectedSuggestionIndex, suggestions.length - 1));
  const start = Math.max(0, Math.min(
    selected - Math.floor(MAX_VISIBLE_SUGGESTIONS / 2),
    suggestions.length - MAX_VISIBLE_SUGGESTIONS,
  ));
  const visibleSuggestions = suggestions.slice(start, start + MAX_VISIBLE_SUGGESTIONS);

  return (
    <section className={open ? "chat-panel chat-panel--open" : "chat-panel"}>
      <div className="chat-panel__history" ref={historyRef} role="log" aria-live="polite">
        {messages.map(message => (
          <div key={message.id}
            className={"chat-panel__message chat-panel__message--" +
              (message.tone ?? "normal")}>
            <span>{message.text}</span>
            {message.actionLabel && (
              <button type="button" className="chat-panel__link"
                disabled={!onMessageAction}
                onClick={() => onMessageAction?.(message.id)}>
                {message.actionLabel}
              </button>
            )}
          </div>
        ))}
      </div>
      {open && visibleSuggestions.length > 0 && (
        <div className="chat-panel__suggestions" role="listbox"
          aria-label={t("ui.chatHint")}>
          <span className="chat-panel__suggestion-hint">{t("ui.chatHint")}</span>
          {visibleSuggestions.map((suggestion, index) => (
            <div key={suggestion.value} role="option"
              aria-selected={start + index === selected}
              className={"chat-panel__suggestion" +
                (start + index === selected ? " chat-panel__suggestion--selected" : "")}>
              <strong>{suggestion.value}</strong>
              <span>{suggestion.description}</span>
            </div>
          ))}
        </div>
      )}
      {open && (
        <form className="chat-panel__input" onSubmit={event => {
          event.preventDefault();
          onSubmit?.();
        }}>
          <span aria-hidden="true">&gt;</span>
          <TextInput
            autoFocus
            value={draft}
            maxLength={MAX_DRAFT_CHARS}
            aria-label={t("ui.chatMessage")}
            readOnly={!onDraftChange}
            onChange={event => onDraftChange?.(event.target.value)}
          />
        </form>
      )}
    </section>
  );
}
