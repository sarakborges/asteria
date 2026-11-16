import { useLayoutEffect, useRef, type KeyboardEvent } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  ChatMessageView, ChatSuggestionView,
} from "../../../presentation/chatModels";
import { TextInput } from "../../atoms/TextInput/TextInput";
import { ScrollArea } from "../../molecules/ScrollArea/ScrollArea";
import "./ChatPanel.css";

const MAX_VISIBLE_SUGGESTIONS = 7;
const MAX_VISIBLE_HISTORY = 64;
const MAX_DRAFT_CHARS = 256;

export type ChatPanelProps = {
  open: boolean;
  visible: boolean;
  focusRequest?: number;
  history: readonly ChatMessageView[];
  suggestions?: readonly ChatSuggestionView[];
  selectedSuggestionIndex?: number;
  draft: string;
  onDraftChange?(value: string, caret: number): void;
  onInputSelect?(caret: number): void;
  onSubmit?(): void;
  onInputKeyDown?(event: KeyboardEvent<HTMLInputElement>): void;
  onMessageAction?(messageId: string): void;
};

export function ChatPanel({
  open, visible, focusRequest = 0, history, suggestions = [], selectedSuggestionIndex = 0,
  draft, onDraftChange, onSubmit, onMessageAction, onInputKeyDown,
  onInputSelect,
}: ChatPanelProps) {
  const { t } = useLocalization();
  const historyRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const followingHistory = useRef(true);
  const previouslyVisible = useRef(false);
  const messages = history.slice(-MAX_VISIBLE_HISTORY);
  const newestMessageId = messages.at(-1)?.id;

  useLayoutEffect(() => {
    const viewport = historyRef.current;
    if (visible && viewport && (!previouslyVisible.current || followingHistory.current)) {
      // New messages should follow the bottom only while the user is already
      // there. Never pull a reader away from older messages.
      viewport.scrollTop = viewport.scrollHeight;
    }
    previouslyVisible.current = visible;
  }, [visible, newestMessageId]);

  // Mount, native WebView handoff and OS focus restoration can happen in
  // different frames. Keep the focused UI field authoritative for chat input.
  useLayoutEffect(() => {
    if (!open || !visible) return;
    // Native WRY focus may temporarily leave the browser even after the
    // input was first focused. Require several stable frames after the handoff
    // (not just one early DOM focus) and retry with a finite budget.
    const focusInput = () => {
      if (document.hasFocus() && inputRef.current !== document.activeElement)
        inputRef.current?.focus({ preventScroll: true });
    };
    let frame = 0;
    let attempts = 0;
    let stableFrames = 0;
    const focusDuringHandoff = () => {
      focusInput();
      stableFrames = document.hasFocus() && inputRef.current === document.activeElement
        ? stableFrames + 1 : 0;
      if (stableFrames < 8 && ++attempts < 120)
        frame = window.requestAnimationFrame(focusDuringHandoff);
    };
    focusDuringHandoff();
    window.addEventListener("focus", focusInput);
    return () => {
      window.cancelAnimationFrame(frame);
      window.removeEventListener("focus", focusInput);
    };
  }, [open, visible, focusRequest]);

  if (!visible) return null;

  const selected = Math.max(0, Math.min(selectedSuggestionIndex, suggestions.length - 1));
  const start = Math.max(0, Math.min(
    selected - Math.floor(MAX_VISIBLE_SUGGESTIONS / 2),
    suggestions.length - MAX_VISIBLE_SUGGESTIONS,
  ));
  const visibleSuggestions = suggestions.slice(start, start + MAX_VISIBLE_SUGGESTIONS);

  return (
    <section className={open ? "chat-panel chat-panel--open" : "chat-panel"}>
      <ScrollArea className="chat-panel__history"
        contentClassName="chat-panel__history-messages"
        viewportRef={historyRef}
        ariaLabel={t("ui.chatMessage")}
        onViewportScroll={event => {
          const viewport = event.currentTarget;
          followingHistory.current =
            viewport.scrollHeight - viewport.clientHeight - viewport.scrollTop <= 12;
        }}>
        <div role="log" aria-live="polite">
          {messages.map(message => (
            <div key={message.id}
              className={"chat-panel__message chat-panel__message--" +
                (message.tone ?? "normal")}>
              <span>{message.localizationKey
                ? t(message.localizationKey, message.parameters ?? {})
                : message.text}</span>
              {(message.action || message.actionLabel) && (
                <button type="button" className="chat-panel__link"
                  disabled={!onMessageAction}
                  onClick={() => onMessageAction?.(message.id)}>
                  {message.action === "warp" ? t("chat.action.warp") :
                    message.action === "structure_file" ? t("chat.action.openStructureFile") :
                      message.actionLabel}
                </button>
              )}
            </div>
          ))}
        </div>
      </ScrollArea>
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
            inputRef={inputRef}
            autoFocus={open}
            onKeyDown={onInputKeyDown}
            value={draft}
            maxLength={MAX_DRAFT_CHARS}
            aria-label={t("ui.chatMessage")}
            readOnly={!onDraftChange}
            onSelect={event => onInputSelect?.(event.currentTarget.selectionStart ?? draft.length)}
            onChange={event => onDraftChange?.(
              event.target.value, event.target.selectionStart ?? event.target.value.length,
            )}
          />
        </form>
      )}
    </section>
  );
}
