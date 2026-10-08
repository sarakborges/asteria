import type { ChatSuggestionView } from "./chatModels";
import type { ChatCompletionCatalog } from "../state/uiState";

export type ChatCompletion = {
  start: number;
  end: number;
  suggestions: readonly ChatSuggestionView[];
};

const SUGGESTION_LIMIT = 64;
const META_TAGS = ["NO_AI", "PERSISTENT"] as const;
const ACTIONS = ["add", "remove", "edit"] as const;
const LOCATE_KINDS = ["biome", "structure"] as const;

function matching(
  values: readonly string[],
  prefix: string,
  description: string,
): ChatSuggestionView[] {
  const query = prefix.toLowerCase();
  return values
    .filter(value => value.toLowerCase().includes(query))
    .slice(0, SUGGESTION_LIMIT)
    .map(value => ({ value, description }));
}

/** Match the token under the caret, not only the last token in the draft. */
export function suggestChatArguments(
  draft: string,
  caret: number,
  commands: readonly string[],
  catalog: ChatCompletionCatalog,
  t: (key: string) => string,
): ChatCompletion {
  const empty: ChatCompletion = { start: caret, end: caret, suggestions: [] };
  if (!draft.startsWith("/") || caret < 0 || caret > draft.length) return empty;

  let start = caret;
  while (start > 0 && !/\s/.test(draft[start - 1])) start--;
  let end = caret;
  while (end < draft.length && !/\s/.test(draft[end])) end++;

  const tokens = draft.slice(0, start).trim();
  const wordsBefore = tokens ? tokens.split(/\s+/) : [];
  const index = wordsBefore.length;
  const prefix = draft.slice(start, caret);
  const command = draft.split(/\s+/)[0];
  const first = wordsBefore[1];
  const second = wordsBefore[2];
  const descriptor = (kind: string) => t("chat.completion." + kind);
  let suggestions: ChatSuggestionView[] = [];

  if (index === 0) {
    suggestions = commands.filter(cmd => cmd.includes(prefix.toLowerCase()))
      .slice(0, SUGGESTION_LIMIT)
      .map(value => ({
        value, description: t("chat.commandHint." + value.slice(1)),
      }));
  } else {
    switch (command) {
      case "/spawn":
        suggestions = index === 1
          ? matching(catalog.creatures, prefix, descriptor("creature"))
          : index === 2
            ? matching(META_TAGS, prefix, descriptor("metaTag"))
            : [];
        break;
      case "/modify":
        suggestions = index === 1
          ? matching(ACTIONS, prefix, descriptor("modifyAction"))
          : index === 2
            ? matching(META_TAGS, prefix, descriptor("metaTag"))
            : [];
        break;
      case "/place":
        suggestions = index === 1
          ? matching(["structure"], prefix, descriptor("structure"))
          : first !== "structure" ? []
            : index === 2
              ? matching(catalog.structures, prefix, descriptor("structure"))
              : index === 3
                ? matching(
                    (catalog.variations[second ?? ""] ?? []).map((_, i) => String(i + 1)),
                    prefix, descriptor("variation"))
                : [];
        break;
      case "/locate":
        suggestions = index === 1
          ? matching(LOCATE_KINDS, prefix, descriptor("locateKind"))
          : index === 2
            ? matching(first === "biome" ? catalog.biomes :
                first === "structure" ? catalog.structures : [],
                prefix, descriptor(first === "biome" ? "biome" : "structure"))
            : index === 3 && first === "structure"
              ? matching(
                  (catalog.variations[second ?? ""] ?? []).map((_, i) => String(i + 1)),
                  prefix, descriptor("variation"))
              : [];
        break;
      case "/warp": {
        const position = catalog.position;
        if (index >= 1 && index <= 3 && position) {
          const axis = index === 1 ? "x" : index === 2 ? "z" : "y";
          suggestions = matching([String(position[axis])], prefix,
            descriptor("coordinate") + " " + axis.toUpperCase());
        } else if (index === 4) {
          suggestions = matching(catalog.dimensions, prefix, descriptor("dimension"));
        }
        break;
      }
    }
  }

  return {
    start, end,
    suggestions: suggestions.sort((a, b) => a.value.localeCompare(b.value, "en")),
  };
}

export function replaceChatToken(
  draft: string,
  completion: ChatCompletion,
  selected: string,
): { text: string; caret: number } {
  const suffix = draft.slice(completion.end);
  const insertion = selected + (suffix.length === 0 || !/^\s/.test(suffix) ? " " : "");
  const caret = completion.start + insertion.length;
  return {
    text: draft.slice(0, completion.start) + insertion + suffix,
    caret,
  };
}
