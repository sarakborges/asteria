using System.Text.Json;
using Asteria.Core.Settings;
using Asteria.Client.Gameplay;
using Godot;

namespace Asteria.Client.Settings;

public enum KeyCaptureResult : byte
{
    NotCapturing,
    Cancelled,
    UnsupportedKey,
    ReservedKey,
    KeyConflict,
    Unchanged,
    Changed,
    SaveFailed,
}

/// <summary>
/// Owns the one pending native-key capture. Browser UI requests an action;
/// only Godot input events can complete it.
/// </summary>
public sealed class KeybindCaptureController
{
    private readonly ClientPreferencesController _preferences;
    private KeybindAction? _pendingAction;

    public KeybindCaptureController(ClientPreferencesController preferences)
    {
        _preferences = preferences ??
            throw new ArgumentNullException(nameof(preferences));
    }

    public bool IsCapturing => _pendingAction.HasValue;

    public KeybindAction? PendingAction => _pendingAction;

    public bool Begin(JsonElement message)
    {
        if (_pendingAction.HasValue ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("action", out var raw) ||
            raw.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<KeybindAction>(
                raw.GetString(), ignoreCase: false, out var action) ||
            !Enum.IsDefined(action))
        {
            return false;
        }

        _pendingAction = action;
        return true;
    }

    public bool Cancel()
    {
        if (!_pendingAction.HasValue) return false;
        _pendingAction = null;
        return true;
    }

    public KeyCaptureResult Press(InputEventKey input)
    {
        if (_pendingAction is not { } action)
        {
            return KeyCaptureResult.NotCapturing;
        }

        if (input.Keycode == Key.Escape)
        {
            Cancel();
            return KeyCaptureResult.Cancelled;
        }

        var key = GameplayKeyMap.FromEvent(input);
        if (key == KeyboardKey.None)
        {
            return KeyCaptureResult.UnsupportedKey;
        }

        var result = _preferences.SetCapturedKeybind(action, key) switch
        {
            ClientPreferenceUpdate.Changed => KeyCaptureResult.Changed,
            ClientPreferenceUpdate.Unchanged => KeyCaptureResult.Unchanged,
            ClientPreferenceUpdate.ReservedKey => KeyCaptureResult.ReservedKey,
            ClientPreferenceUpdate.KeyConflict => KeyCaptureResult.KeyConflict,
            ClientPreferenceUpdate.SaveFailed => KeyCaptureResult.SaveFailed,
            _ => throw new InvalidOperationException(
                "Unexpected keybind mutation result."),
        };

        if (result is KeyCaptureResult.Changed or
            KeyCaptureResult.Unchanged or
            KeyCaptureResult.SaveFailed)
        {
            _pendingAction = null;
        }

        return result;
    }
}
