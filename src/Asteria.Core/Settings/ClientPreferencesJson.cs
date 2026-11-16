using System.Text.Json;
using System.Text.Json.Serialization;

namespace Asteria.Core.Settings;

/// <summary>
/// Validates an external client config before creating a mutable owner.
/// No implicit fallback or format compatibility is introduced here.
/// </summary>
public static class ClientPreferencesJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ClientPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return JsonSerializer.Serialize(preferences.Capture(), Options);
    }

    public static ClientPreferences Deserialize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var snapshot = JsonSerializer.Deserialize<ClientPreferencesSnapshot>(
            text, Options);
        if (snapshot is null)
        {
            throw new JsonException("Missing client preferences.");
        }

        return ClientPreferences.Restore(snapshot);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
