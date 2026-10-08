import { useEffect, useMemo, useState, type KeyboardEvent } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { ChatMessageView } from "../../../presentation/chatModels";
import { ChatPanel } from "../ChatPanel/ChatPanel";
import "./ChatDock.css";

export type ChatDockProps = {
  open: boolean;
  visible: boolean;
  history: readonly ChatMessageView[];
  commands: readonly string[];
  onSubmit(text: string): void;
  onClose(): void;
};

export function ChatDock({
  open, visible, history, commands, onSubmit, onClose,
}: ChatDockProps) {
  const { t } = useLocalization();
  const [draft, setDraft] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);

  useEffect(() => {
    if (!open) {
      setDraft("");
      setSelectedIndex(0);
    }
  }, [open]);

  const suggestions = useMemo(() => {
    if (!draft.startsWith("/") || draft.includes(" ")) return [];
    return commands.filter(command => command.startsWith(draft))
      .map(value => ({
        value,
        description: t("chat.commandHint." + value.slice(1)),
      }));
  }, [commands, draft, t]);
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
      setDraft(suggestions[index].value + " ");
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
      onDraftChange={value => {
        setDraft(value);
        setSelectedIndex(0);
      }}
      onSubmit={() => onSubmit(draft)}
    />
    </div>
  );
}
