using Asteria.Core.Settings;
using Godot;

namespace Asteria.Client.Gameplay;

/// <summary>
/// Godot's native physical keys are the only gameplay input source.
/// Left/right modifiers use native location; unmapped keys remain unsupported.
/// </summary>
public static class GameplayKeyMap
{
    public static bool Matches(
        InputEventKey key,
        ClientPreferences preferences,
        KeybindAction action) =>
        FromEvent(key) == preferences.KeyFor(action);

    public static KeyboardKey FromEvent(InputEventKey input)
    {
        var code = input.PhysicalKeycode == Key.None
            ? input.Keycode
            : input.PhysicalKeycode;

        return code switch
        {
            Key.Space => KeyboardKey.Space,
            Key.Shift => input.Location == KeyLocation.Right
                ? KeyboardKey.ShiftRight : KeyboardKey.ShiftLeft,
            Key.Ctrl => input.Location == KeyLocation.Right
                ? KeyboardKey.ControlRight : KeyboardKey.ControlLeft,
            Key.Alt => input.Location == KeyLocation.Right
                ? KeyboardKey.AltRight : KeyboardKey.AltLeft,
            Key.Tab => KeyboardKey.Tab,
            Key.Backspace => KeyboardKey.Backspace,
            Key.Delete => KeyboardKey.Delete,
            Key.Insert => KeyboardKey.Insert,
            Key.Home => KeyboardKey.Home,
            Key.End => KeyboardKey.End,
            Key.Pageup => KeyboardKey.PageUp,
            Key.Pagedown => KeyboardKey.PageDown,
            Key.Up => KeyboardKey.ArrowUp,
            Key.Down => KeyboardKey.ArrowDown,
            Key.Left => KeyboardKey.ArrowLeft,
            Key.Right => KeyboardKey.ArrowRight,
            Key.F1 => KeyboardKey.F1,
            Key.F2 => KeyboardKey.F2,
            Key.F3 => KeyboardKey.F3,
            Key.F4 => KeyboardKey.F4,
            Key.F5 => KeyboardKey.F5,
            Key.F6 => KeyboardKey.F6,
            Key.F7 => KeyboardKey.F7,
            Key.F8 => KeyboardKey.F8,
            Key.F9 => KeyboardKey.F9,
            Key.F10 => KeyboardKey.F10,
            Key.F11 => KeyboardKey.F11,
            Key.F12 => KeyboardKey.F12,
            Key.Key0 => KeyboardKey.Digit0,
            Key.Key1 => KeyboardKey.Digit1,
            Key.Key2 => KeyboardKey.Digit2,
            Key.Key3 => KeyboardKey.Digit3,
            Key.Key4 => KeyboardKey.Digit4,
            Key.Key5 => KeyboardKey.Digit5,
            Key.Key6 => KeyboardKey.Digit6,
            Key.Key7 => KeyboardKey.Digit7,
            Key.Key8 => KeyboardKey.Digit8,
            Key.Key9 => KeyboardKey.Digit9,
            Key.A => KeyboardKey.KeyA,
            Key.B => KeyboardKey.KeyB,
            Key.C => KeyboardKey.KeyC,
            Key.D => KeyboardKey.KeyD,
            Key.E => KeyboardKey.KeyE,
            Key.F => KeyboardKey.KeyF,
            Key.G => KeyboardKey.KeyG,
            Key.H => KeyboardKey.KeyH,
            Key.I => KeyboardKey.KeyI,
            Key.J => KeyboardKey.KeyJ,
            Key.K => KeyboardKey.KeyK,
            Key.L => KeyboardKey.KeyL,
            Key.M => KeyboardKey.KeyM,
            Key.N => KeyboardKey.KeyN,
            Key.O => KeyboardKey.KeyO,
            Key.P => KeyboardKey.KeyP,
            Key.Q => KeyboardKey.KeyQ,
            Key.R => KeyboardKey.KeyR,
            Key.S => KeyboardKey.KeyS,
            Key.T => KeyboardKey.KeyT,
            Key.U => KeyboardKey.KeyU,
            Key.V => KeyboardKey.KeyV,
            Key.W => KeyboardKey.KeyW,
            Key.X => KeyboardKey.KeyX,
            Key.Y => KeyboardKey.KeyY,
            Key.Z => KeyboardKey.KeyZ,
            _ => KeyboardKey.None,
        };
    }
}
