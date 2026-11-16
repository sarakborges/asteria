namespace Asteria.Core.Content;

/// <summary>Immutable lookup with deterministic iteration and unique namespaced IDs.</summary>
public sealed class PackContentRegistry<T> where T : class
{
    private readonly IReadOnlyDictionary<string, T> _byId;
    private readonly T[] _definitions;

    private PackContentRegistry(IReadOnlyDictionary<string, T> byId, T[] definitions)
    {
        _byId = byId;
        _definitions = definitions;
    }

    public int Count => _definitions.Length;

    public IReadOnlyList<T> Definitions => _definitions;

    public T Get(string id) =>
        _byId.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown content id: {id}");

    public bool TryGet(string id, out T? definition) =>
        _byId.TryGetValue(id, out definition);

    public static PackContentRegistry<T> FromJson(
        IEnumerable<string> documents,
        Func<string, T> parse,
        Func<T, string> idOf)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(idOf);

        var definitions = documents
            .Select(parse)
            .OrderBy(idOf, StringComparer.Ordinal)
            .ToArray();

        var byId = new Dictionary<string, T>(definitions.Length, StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var id = idOf(definition);
            if (!byId.TryAdd(id, definition))
            {
                throw new ArgumentException($"Duplicate content id: {id}", nameof(documents));
            }
        }

        return new PackContentRegistry<T>(
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, T>(byId),
            definitions);
    }
}
