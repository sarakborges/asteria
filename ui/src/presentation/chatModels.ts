export type ChatMessageTone =
  | "normal"
  | "error"
  | "link";

export type ChatMessageView = {
  id: string;
  text: string;
  tone?: ChatMessageTone;
  actionLabel?: string;
  localizationKey?: "chat.local.help" | "chat.local.position" | "chat.local.time" | "chat.local.unknown";
};

export type ChatSuggestionView = {
  value: string;
  description: string;
};
