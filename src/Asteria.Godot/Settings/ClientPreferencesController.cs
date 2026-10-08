using System.Text.Json;
using Asteria.Core.Settings;

namespace Asteria.Client.Settings;

public enum ClientPreferenceUpdate : byte
{
    NotHandled,
    Unchanged,
    Changed,
    InvalidValue,
    ReservedKey,
    KeyConflict,
    SaveFailed,
}

/// <summary>
/// Translates validated semantic WebUI commands into one Core preference
/// owner. No UI input capture, state mirroring or streaming work lives here.
/// </summary>
public sealed class ClientPreferencesController
{
    private readonly ClientPreferencesStore _store;

    public ClientPreferencesController(ClientPreferencesStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Preferences = store.Load();
    }

    public ClientPreferences Preferences { get; }

    public ClientPreferenceUpdate Apply(
        string? messageType,
        JsonElement message)
    {
        if (messageType is null ||
            !messageType.StartsWith(
                "ui.client_preferences.",
                StringComparison.Ordinal))
        {
            return ClientPreferenceUpdate.NotHandled;
        }

        if (!message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object)
        {
            return ClientPreferenceUpdate.InvalidValue;
        }

        var update = messageType switch
        {
            "ui.client_preferences.render_distance" =>
                UpdateRenderDistance(payload),
            "ui.client_preferences.hide_hints" =>
                UpdateHideHints(payload),
            "ui.client_preferences.target_block_position" =>
                UpdateTargetPosition(payload),
            "ui.client_preferences.hint" =>
                UpdateHint(payload),
            "ui.client_preferences.keybind" =>
                UpdateKeybind(payload),
            _ => ClientPreferenceUpdate.NotHandled,
        };

        if (update != ClientPreferenceUpdate.Changed)
        {
            return update;
        }

        return _store.TrySave(Preferences)
            ? ClientPreferenceUpdate.Changed
            : ClientPreferenceUpdate.SaveFailed;
    }

    private ClientPreferenceUpdate UpdateRenderDistance(JsonElement payload)
    {
        if (!TryReadInt(payload, "value", out var value))
        {
            return ClientPreferenceUpdate.InvalidValue;
        }

        return Changed(Preferences.SetRenderDistanceChunks(value));
    }

    private ClientPreferenceUpdate UpdateHideHints(JsonElement payload)
    {
        if (!TryReadBool(payload, "value", out var value))
        {
            return ClientPreferenceUpdate.InvalidValue;
        }

        return Changed(Preferences.SetHideHints(value));
    }

    private ClientPreferenceUpdate UpdateTargetPosition(JsonElement payload)
    {
        if (!TryReadEnum<TargetBlockPosition>(payload, "value", out var value))
        {
            return ClientPreferenceUpdate.InvalidValue;
        }

        return Changed(Preferences.SetTargetBlockPosition(value));
    }

    private ClientPreferenceUpdate UpdateHint(JsonElement payload)
    {
        if (!TryReadEnum<HintKind>(payload, "kind", out var kind) ||
            !TryReadBool(payload, "value", out var value))
        {
            return ClientPreferenceUpdate.InvalidValue;
        }

        return Changed(Preferences.SetHintPreference(kind, value));
    }

    private ClientPreferenceUpdate UpdateKeybind(JsonElement payload)
    {
        if (!TryReadEnum<KeybindAction>(payload, "action", out var action) ||
            !TryReadEnum<KeyboardKey>(payload, "key", out var key))
        {
            return ClientPreferenceUpdate.InvalidValue;
        }

        return Preferences.SetKeybind(action, key) switch
        {
            KeybindChangeResult.Changed => ClientPreferenceUpdate.Changed,
            KeybindChangeResult.Unchanged => ClientPreferenceUpdate.Unchanged,
            KeybindChangeResult.Reserved => ClientPreferenceUpdate.ReservedKey,
            KeybindChangeResult.Conflict => ClientPreferenceUpdate.KeyConflict,
            _ => throw new InvalidOperationException(
                "Unhandled keybind change result."),
        };
    }

    private static ClientPreferenceUpdate Changed(bool changed) =>
        changed ? ClientPreferenceUpdate.Changed : ClientPreferenceUpdate.Unchanged;

    private static bool TryReadInt(
        JsonElement payload,
        string property,
        out int value)
    {
        value = default;
        return payload.TryGetProperty(property, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value);
    }

    private static bool TryReadBool(
        JsonElement payload,
        string property,
        out bool value)
    {
        value = default;
        if (!payload.TryGetProperty(property, out var element)) return false;
        if (element.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.GetBoolean();
        return true;
    }

    private static bool TryReadEnum<T>(
        JsonElement payload,
        string property,
        out T value) where T : struct, Enum
    {
        value = default;
        return payload.TryGetProperty(property, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            Enum.TryParse(element.GetString(), ignoreCase: false, out value) &&
            Enum.IsDefined(value);
    }
}
