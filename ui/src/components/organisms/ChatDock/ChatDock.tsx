import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { ChatMessageView } from "../../../presentation/chatModels";
import type { ChatCompletionCatalog } from "../../../state/uiState";
import { replaceChatToken, suggestChatArguments } from "../../../presentation/chatAutocomplete";
import { ChatPanel } from "../ChatPanel/ChatPanel";
import "./ChatDock.css";

export type ChatDockProps = {
  open: boolean;
  visible: boolean;
  focusRequest?: number;
  history: readonly ChatMessageView[];
  commands: readonly string[];
  catalog: ChatCompletionCatalog;
  onSubmit(text: string): void;
  onClose(): void;
  onMessageAction(messageId: string): void;
};

export function ChatDock({
  open, visible, focusRequest = 0, history, commands, catalog, onSubmit, onClose, onMessageAction,
}: ChatDockProps) {
  const { t, contentName } = useLocalization();
  const [draft, setDraft] = useState("");
  const [caret, setCaret] = useState(0);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [dismissedSuggestions, setDismissedSuggestions] = useState<string | null>(null);
  // This is editable-field recall only, never a second chat transcript or
  // gameplay command owner. It is bounded and lives with the chat UI.
  const submissions = useRef<string[]>([]);
  const recallIndex = useRef<number | null>(null);
  const pendingDraft = useRef("");

  useEffect(() => {
    if (!open) {
      setDraft("");
      setCaret(0);
      setSelectedIndex(0);
      setDismissedSuggestions(null);
      recallIndex.current = null;
      pendingDraft.current = "";
    }
  }, [open]);

  const completion = useMemo(
    () => suggestChatArguments(draft, caret, commands, catalog, t, contentName),
    [draft, caret, commands, catalog, t, contentName],
  );
  const suggestions = dismissedSuggestions === draft + ":" + caret
    ? [] : completion.suggestions;
  const index = Math.max(0, Math.min(selectedIndex, suggestions.length - 1));

  const submit = () => {
    const line = draft.trim();
    if (line) {
      const recent = submissions.current;
      if (recent.at(-1) !== line) {
        recent.push(line);
        if (recent.length > 32) recent.shift();
      }
    }
    recallIndex.current = null;
    pendingDraft.current = "";
    onSubmit(draft);
  };

  const recall = (direction: -1 | 1, input: HTMLInputElement) => {
    const recent = submissions.current;
    if (recent.length === 0) return;
    if (recallIndex.current === null) {
      if (direction > 0) return;
      pendingDraft.current = draft;
      recallIndex.current = recent.length - 1;
    } else {
      const next = recallIndex.current + direction;
      recallIndex.current = next >= recent.length ? null
        : Math.max(0, next);
    }
    const updated = recallIndex.current === null
      ? pendingDraft.current : recent[recallIndex.current];
    setDraft(updated);
    setCaret(updated.length);
    setSelectedIndex(0);
    // Recalled commands should stay editable; autocomplete must not steal
    // the next ArrowDown used to restore the original unsent draft.
    setDismissedSuggestions(updated + ":" + updated.length);
    input.setSelectionRange(updated.length, updated.length);
  };

  const handleKey = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.nativeEvent.isComposing) return;
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      if (suggestions.length > 0) {
        setDismissedSuggestions(draft + ":" + caret);
        return;
      }
      onClose();
    } else if (event.key === "ArrowDown" && suggestions.length > 0) {
      event.preventDefault();
      setSelectedIndex((index + 1) % suggestions.length);
    } else if (event.key === "ArrowUp" && suggestions.length > 0) {
      event.preventDefault();
      setSelectedIndex((index + suggestions.length - 1) % suggestions.length);
    } else if (event.key === "ArrowUp" || event.key === "ArrowDown") {
      event.preventDefault();
      recall(event.key === "ArrowUp" ? -1 : 1, event.currentTarget);
    } else if (event.key === "Tab" && suggestions.length > 0) {
      event.preventDefault();
      const updated = replaceChatToken(draft, completion, suggestions[index].value);
      // Only the focused chat input is edited. Godot still owns gameplay keys.
      event.currentTarget.setRangeText(
        updated.text, 0, event.currentTarget.value.length, "end",
      );
      event.currentTarget.setSelectionRange(updated.caret, updated.caret);
      setDraft(updated.text);
      setCaret(updated.caret);
      setSelectedIndex(0);
    }
  };

  return (
    <div className="chat-dock">
      <ChatPanel
        open={open}
        visible={visible}
        focusRequest={focusRequest}
        history={history}
        onMessageAction={onMessageAction}
        draft={draft}
        suggestions={suggestions}
        selectedSuggestionIndex={index}
        onInputKeyDown={handleKey}
        onInputSelect={setCaret}
        onDraftChange={(value, nextCaret) => {
          setDraft(value);
          setCaret(nextCaret);
          setSelectedIndex(0);
          setDismissedSuggestions(null);
          recallIndex.current = null;
        }}
        onSubmit={submit}
      />
    </div>
  );
}
