export type ChatMessageTone =
  | "normal"
  | "error"
  | "link";

export type ChatMessageView = {
  id: string;
  text: string;
  tone?: ChatMessageTone;
  actionLabel?: string;
};

export type ChatSuggestionView = {
  value: string;
  description: string;
};
