using System.Text.Json;
using System.Text.Json.Nodes;

namespace Asteria.Core.Content;

/// <summary>
/// Namespaced IDs are standard for pack-owned content. Some existing
/// category registries have their own validated local ID contracts.
/// </summary>
public enum PackDocumentIdFormat : byte
{
    Namespaced = 0,
    RegistryDefined = 1,
}

/// <summary>
/// Composes immutable authored documents in explicit low-to-high pack priority.
/// A document with { "id": "...", "enabled": false } removes that definition.
/// An enabled overlay patches existing objects recursively; arrays replace in full.
/// </summary>
public static class PackDocumentOverlay
{
    public static IReadOnlyList<string> Compose(
        IEnumerable<string> builtInDocuments,
        IEnumerable<IEnumerable<string>> overlays,
        PackDocumentIdFormat idFormat = PackDocumentIdFormat.Namespaced)
    {
        ArgumentNullException.ThrowIfNull(builtInDocuments);
        ArgumentNullException.ThrowIfNull(overlays);
        if (idFormat is not (PackDocumentIdFormat.Namespaced or
            PackDocumentIdFormat.RegistryDefined))
            throw new ArgumentOutOfRangeException(nameof(idFormat));

        var definitions = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        Apply(definitions, builtInDocuments, "default", idFormat);

        var index = 0;
        foreach (var overlay in overlays)
        {
            Apply(definitions,
                overlay ?? throw new ArgumentException("Null overlay document set.", nameof(overlays)),
                $"overlay[{index++}]", idFormat);
        }

        return definitions.Values
            .Select(document => document.ToJsonString())
            .ToArray();
    }

    private static void Apply(
        SortedDictionary<string, JsonObject> definitions,
        IEnumerable<string> documents,
        string source,
        PackDocumentIdFormat idFormat)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var json in documents)
        {
            JsonObject document;
            try
            {
                document = JsonNode.Parse(json)?.AsObject()
                    ?? throw new FormatException("Definition must be a JSON object.");
            }
            catch (JsonException error)
            {
                throw new FormatException($"Invalid definition in {source}: {error.Message}", error);
            }
            catch (InvalidOperationException error)
            {
                throw new FormatException($"Invalid definition in {source}: {error.Message}", error);
            }

            var id = document["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
                throw new FormatException($"Definition in {source} has no id.");
            if (idFormat == PackDocumentIdFormat.Namespaced && !id.Contains(':'))
                throw new FormatException($"Definition in {source} has no namespaced id.");

            if (!seen.Add(id))
                throw new FormatException($"Duplicate definition {id} in {source}.");

            var enabled = true;
            if (document.TryGetPropertyValue("enabled", out var enabledNode))
            {
                if (enabledNode is not JsonValue value ||
                    !value.TryGetValue<bool>(out enabled))
                    throw new FormatException($"Definition {id} has invalid enabled flag.");
                document.Remove("enabled");
            }

            if (!enabled)
            {
                definitions.Remove(id);
                continue;
            }

            if (definitions.TryGetValue(id, out var existing))
                Merge(existing, document);
            else
                definitions[id] = document;
        }
    }

    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var field in patch)
        {
            if (field.Value is JsonObject incoming &&
                target[field.Key] is JsonObject original)
            {
                Merge(original, incoming);
            }
            else
            {
                target[field.Key] = field.Value?.DeepClone();
            }
        }
    }
}
