namespace Asteria.Core.Settings;

public enum TargetBlockPosition : byte
{
    Center,
    TopRight,
    Hidden,
}

public enum HintKind : byte
{
    OpenInventory,
    CloseInventory,
    RotateBlock,
    BreakBlock,
    BreakOrPlaceBlock,
    BrushPaint,
    BrushClear,
    ArtisansKit,
    Shears,
    StructureTool,
}

public enum KeybindAction : byte
{
    Jump,
    Descend,
    Inventory,
    Chat,
    ToolAction,
    DropItem,
    ChangePerspective,
}

public enum KeyboardKey : byte
{
    None,
    Space, ShiftLeft, ShiftRight, ControlLeft, ControlRight,
    AltLeft, AltRight, Tab, Backspace, Delete, Insert, Home, End,
    PageUp, PageDown, ArrowUp, ArrowDown, ArrowLeft, ArrowRight,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Digit0, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6,
    Digit7, Digit8, Digit9,
    KeyA, KeyB, KeyC, KeyD, KeyE, KeyF, KeyG, KeyH, KeyI,
    KeyJ, KeyK, KeyL, KeyM, KeyN, KeyO, KeyP, KeyQ, KeyR,
    KeyS, KeyT, KeyU, KeyV, KeyW, KeyX, KeyY, KeyZ,
}

public enum KeybindChangeResult : byte
{
    Unchanged,
    Changed,
    Reserved,
    Conflict,
}

public sealed record HintPreferencesSnapshot(
    bool OpenInventory,
    bool CloseInventory,
    bool RotateBlock,
    bool BreakBlock,
    bool BreakOrPlaceBlock,
    bool BrushPaint,
    bool BrushClear,
    bool ArtisansKit,
    bool Shears,
    bool StructureTool);

public sealed record HudPreferencesSnapshot(
    bool HideHints,
    TargetBlockPosition TargetBlockPosition,
    HintPreferencesSnapshot Hints);

public sealed record KeybindsSnapshot(
    KeyboardKey Jump,
    KeyboardKey Descend,
    KeyboardKey Inventory,
    KeyboardKey Chat,
    KeyboardKey ToolAction,
    KeyboardKey DropItem,
    KeyboardKey ChangePerspective);

public sealed record ClientPreferencesSnapshot(
    int RenderDistanceChunks,
    HudPreferencesSnapshot Hud,
    KeybindsSnapshot Keybinds);

/// <summary>
/// One mutable owner for persisted client preferences, independent of any
/// world, Sphere or WebUI presentation snapshot.
/// </summary>
public sealed class ClientPreferences
{
    public const int MinRenderDistanceChunks = 4;
    public const int MaxRenderDistanceChunks = 24;

    // Asteria keeps its original lower startup radius until worldgen capacity
    // is profiled. MineClone's default is 12, but its adjustable range is kept.
    public const int DefaultRenderDistanceChunks = 4;

    private static readonly KeybindAction[] Actions =
        Enum.GetValues<KeybindAction>();

    private static readonly HintKind[] Hints =
        Enum.GetValues<HintKind>();

    private readonly KeyboardKey[] _keys =
    [
        KeyboardKey.Space, KeyboardKey.ShiftLeft, KeyboardKey.KeyE,
        KeyboardKey.KeyT, KeyboardKey.KeyR, KeyboardKey.KeyQ,
        KeyboardKey.F5,
    ];

    private readonly bool[] _hints =
        Enumerable.Repeat(true, Hints.Length).ToArray();

    public int RenderDistanceChunks { get; private set; } =
        DefaultRenderDistanceChunks;

    public bool HideHints { get; private set; }

    public TargetBlockPosition TargetBlockPosition { get; private set; } =
        TargetBlockPosition.Center;

    public ulong Revision { get; private set; }

    public KeyboardKey KeyFor(KeybindAction action)
    {
        ValidateAction(action);
        return _keys[(int)action];
    }

    public bool HintPreference(HintKind kind)
    {
        ValidateHint(kind);
        return _hints[(int)kind];
    }

    public bool HintVisible(HintKind kind) =>
        !HideHints && HintPreference(kind);

    public bool SetRenderDistanceChunks(int value)
    {
        var next = Math.Clamp(
            value,
            MinRenderDistanceChunks,
            MaxRenderDistanceChunks);
        if (next == RenderDistanceChunks)
        {
            return false;
        }

        RenderDistanceChunks = next;
        Revision++;
        return true;
    }

    public bool SetHideHints(bool value)
    {
        if (HideHints == value) return false;
        HideHints = value;
        Revision++;
        return true;
    }

    public bool SetTargetBlockPosition(TargetBlockPosition value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (TargetBlockPosition == value) return false;
        TargetBlockPosition = value;
        Revision++;
        return true;
    }

    public bool SetHintPreference(HintKind kind, bool value)
    {
        ValidateHint(kind);
        if (_hints[(int)kind] == value) return false;
        _hints[(int)kind] = value;
        Revision++;
        return true;
    }

    public KeybindChangeResult SetKeybind(
        KeybindAction action,
        KeyboardKey key)
    {
        ValidateAction(action);
        if (!Enum.IsDefined(key) || key == KeyboardKey.None)
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }

        if (IsReserved(key)) return KeybindChangeResult.Reserved;

        if (_keys[(int)action] == key)
        {
            return KeybindChangeResult.Unchanged;
        }

        if (Actions.Any(other =>
            other != action && _keys[(int)other] == key))
        {
            return KeybindChangeResult.Conflict;
        }

        _keys[(int)action] = key;
        Revision++;
        return KeybindChangeResult.Changed;
    }

    public ClientPreferencesSnapshot Capture() =>
        new(
            RenderDistanceChunks,
            new HudPreferencesSnapshot(
                HideHints,
                TargetBlockPosition,
                new HintPreferencesSnapshot(
                    _hints[0], _hints[1], _hints[2], _hints[3],
                    _hints[4], _hints[5], _hints[6], _hints[7],
                    _hints[8], _hints[9])),
            new KeybindsSnapshot(
                _keys[0], _keys[1], _keys[2], _keys[3],
                _keys[4], _keys[5], _keys[6]));

    public static ClientPreferences Restore(
        ClientPreferencesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Hud?.Hints is null || snapshot.Keybinds is null)
        {
            throw new ArgumentException(
                "Missing HUD or keybind preferences.", nameof(snapshot));
        }

        if (snapshot.RenderDistanceChunks is <
                MinRenderDistanceChunks or > MaxRenderDistanceChunks ||
            !Enum.IsDefined(snapshot.Hud.TargetBlockPosition))
        {
            throw new ArgumentException(
                "Invalid client preferences.", nameof(snapshot));
        }

        var keys = new[]
        {
            snapshot.Keybinds.Jump,
            snapshot.Keybinds.Descend,
            snapshot.Keybinds.Inventory,
            snapshot.Keybinds.Chat,
            snapshot.Keybinds.ToolAction,
            snapshot.Keybinds.DropItem,
            snapshot.Keybinds.ChangePerspective,
        };
        if (keys.Any(key =>
                !Enum.IsDefined(key) || key == KeyboardKey.None ||
                IsReserved(key)) ||
            keys.Distinct().Count() != keys.Length)
        {
            throw new ArgumentException(
                "Invalid or conflicting key bindings.", nameof(snapshot));
        }

        var hints = snapshot.Hud.Hints;
        var result = new ClientPreferences
        {
            RenderDistanceChunks = snapshot.RenderDistanceChunks,
            HideHints = snapshot.Hud.HideHints,
            TargetBlockPosition = snapshot.Hud.TargetBlockPosition,
        };
        var values = new[]
        {
            hints.OpenInventory, hints.CloseInventory,
            hints.RotateBlock, hints.BreakBlock,
            hints.BreakOrPlaceBlock, hints.BrushPaint, hints.BrushClear,
            hints.ArtisansKit, hints.Shears, hints.StructureTool,
        };
        Array.Copy(keys, result._keys, keys.Length);
        Array.Copy(values, result._hints, values.Length);
        return result;
    }

    public static bool IsReserved(KeyboardKey key) =>
        key is KeyboardKey.KeyW or KeyboardKey.KeyA or
            KeyboardKey.KeyS or KeyboardKey.KeyD or
            KeyboardKey.F3 or KeyboardKey.F4 or
            >= KeyboardKey.Digit1 and <= KeyboardKey.Digit9;

    private static void ValidateAction(KeybindAction action)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private static void ValidateHint(HintKind hint)
    {
        if (!Enum.IsDefined(hint))
        {
            throw new ArgumentOutOfRangeException(nameof(hint));
        }
    }
}
