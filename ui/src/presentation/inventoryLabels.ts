/** Presentation-only labels; the authoritative inventory identity never changes. */
export function inventoryDisplayName(
  id: string,
  metadata: Readonly<Record<string, string>> | undefined,
  contentName: (id: string) => string,
  authoredName?: string,
): string {
  const base = authoredName && authoredName !== id ? authoredName : contentName(id);
  const values = Object.entries(metadata ?? {})
    .sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)
    .map(([, value]) => contentName(value));
  return values.length ? `${base} (${values.join(", ")})` : base;
}

export function inventorySearchMatches(
  item: {
    id: string;
    name?: string;
    metadata?: Readonly<Record<string, string>>;
  } | null,
  query: string,
  contentName: (id: string) => string,
): boolean {
  const term = normalizeSearch(query.trim());
  if (!term) return true;
  if (!item) return false;
  const properties = Object.entries(item.metadata ?? {}).flatMap(
    ([key, value]) => [key, value, contentName(value)]);
  return [item.id, contentName(item.id), item.name ?? "", ...properties]
    .some(value => normalizeSearch(value).includes(term));
}

const normalizeSearch = (value: string): string =>
  value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLocaleLowerCase();
