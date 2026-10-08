import { useEffect, useMemo, useState, type KeyboardEvent } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { ChatMessageView } from "../../../presentation/chatModels";
import type { ChatCompletionCatalog } from "../../../state/uiState";
import { replaceChatToken, suggestChatArguments } from "../../../presentation/chatAutocomplete";
import { ChatPanel } from "../ChatPanel/ChatPanel";
import "./ChatDock.css";

export type ChatDockProps = {
  open: boolean;
  visible: boolean;
  history: readonly ChatMessageView[];
  commands: readonly string[];
  catalog: ChatCompletionCatalog;
  onSubmit(text: string): void;
  onClose(): void;
};

export function ChatDock({
  open, visible, history, commands, catalog, onSubmit, onClose,
}: ChatDockProps) {
  const { t } = useLocalization();
  const [draft, setDraft] = useState("");
  const [caret, setCaret] = useState(0);
  const [selectedIndex, setSelectedIndex] = useState(0);

  useEffect(() => {
    if (!open) {
      setDraft("");
      setCaret(0);
      setSelectedIndex(0);
    }
  }, [open]);

  const completion = useMemo(
    () => suggestChatArguments(draft, caret, commands, catalog, t),
    [draft, caret, commands, catalog, t],
  );
  const suggestions = completion.suggestions;
  const index = Math.max(0, Math.min(selectedIndex, suggestions.length - 1));

  const handleKey = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.nativeEvent.isComposing) return;
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      onClose();
    } else if (event.key === "ArrowDown" && suggestions.length > 0) {
      event.preventDefault();
      setSelectedIndex((index + 1) % suggestions.length);
    } else if (event.key === "ArrowUp" && suggestions.length > 0) {
      event.preventDefault();
      setSelectedIndex((index + suggestions.length - 1) % suggestions.length);
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
        history={history}
        draft={draft}
        suggestions={suggestions}
        selectedSuggestionIndex={index}
        onInputKeyDown={handleKey}
        onInputSelect={setCaret}
        onDraftChange={(value, nextCaret) => {
          setDraft(value);
          setCaret(nextCaret);
          setSelectedIndex(0);
        }}
        onSubmit={() => onSubmit(draft)}
      />
    </div>
  );
}
