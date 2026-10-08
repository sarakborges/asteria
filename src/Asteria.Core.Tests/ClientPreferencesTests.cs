using System.Text.Json;
using Asteria.Core.Settings;

namespace Asteria.Core.Tests;

public sealed class ClientPreferencesTests
{
    [Fact]
    public void DefaultsAreDeterministicAndUseAsteriaStreamingBaseline()
    {
        var settings = new ClientPreferences();
        Assert.Equal(4, settings.RenderDistanceChunks);
        Assert.Equal(0ul, settings.Revision);
        Assert.False(settings.HideHints);
        Assert.Equal(TargetBlockPosition.Center, settings.TargetBlockPosition);
        foreach (var hint in Enum.GetValues<HintKind>())
        {
            Assert.True(settings.HintVisible(hint));
        }
        Assert.Equal(KeyboardKey.Space, settings.KeyFor(KeybindAction.Jump));
        Assert.Equal(KeyboardKey.ShiftLeft, settings.KeyFor(KeybindAction.Descend));
        Assert.Equal(KeyboardKey.KeyE, settings.KeyFor(KeybindAction.Inventory));
        Assert.Equal(KeyboardKey.KeyT, settings.KeyFor(KeybindAction.Chat));
        Assert.Equal(KeyboardKey.KeyR, settings.KeyFor(KeybindAction.ToolAction));
        Assert.Equal(KeyboardKey.KeyQ, settings.KeyFor(KeybindAction.DropItem));
        Assert.Equal(KeyboardKey.F5, settings.KeyFor(KeybindAction.ChangePerspective));
    }

    [Fact]
    public void RenderDistanceIsBoundedAndChangeDriven()
    {
        var settings = new ClientPreferences();
        Assert.False(settings.SetRenderDistanceChunks(0));
        Assert.True(settings.SetRenderDistanceChunks(100));
        Assert.Equal(24, settings.RenderDistanceChunks);
        Assert.False(settings.SetRenderDistanceChunks(24));
        Assert.True(settings.SetRenderDistanceChunks(-1));
        Assert.Equal(4, settings.RenderDistanceChunks);
        Assert.Equal(2ul, settings.Revision);
    }

    [Fact]
    public void HudPreferencesKeepIndependentHintFlags()
    {
        var settings = new ClientPreferences();
        Assert.True(settings.SetHintPreference(HintKind.Shears, false));
        Assert.False(settings.HintVisible(HintKind.Shears));
        Assert.True(settings.HintVisible(HintKind.BreakBlock));
        Assert.True(settings.SetHideHints(true));
        Assert.False(settings.HintVisible(HintKind.BreakBlock));
        Assert.True(settings.SetTargetBlockPosition(TargetBlockPosition.Hidden));
        Assert.False(settings.SetTargetBlockPosition(TargetBlockPosition.Hidden));
        Assert.Equal(3ul, settings.Revision);
    }

    [Fact]
    public void KeybindsRejectReservedAndConflictingKeys()
    {
        var settings = new ClientPreferences();
        Assert.Equal(KeybindChangeResult.Reserved,
            settings.SetKeybind(KeybindAction.Jump, KeyboardKey.KeyW));
        Assert.Equal(KeybindChangeResult.Reserved,
            settings.SetKeybind(KeybindAction.Jump, KeyboardKey.Digit1));
        Assert.Equal(KeybindChangeResult.Conflict,
            settings.SetKeybind(KeybindAction.Jump, KeyboardKey.KeyE));
        Assert.Equal(KeybindChangeResult.Unchanged,
            settings.SetKeybind(KeybindAction.Jump, KeyboardKey.Space));
        Assert.Equal(KeybindChangeResult.Changed,
            settings.SetKeybind(KeybindAction.Jump, KeyboardKey.KeyJ));
        Assert.Equal(KeyboardKey.KeyJ, settings.KeyFor(KeybindAction.Jump));
        Assert.Equal(1ul, settings.Revision);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            settings.SetKeybind(KeybindAction.Chat, KeyboardKey.None));
    }

    [Fact]
    public void SnapshotSerializesAndRestoresAllGroups()
    {
        var settings = new ClientPreferences();
        settings.SetRenderDistanceChunks(18);
        settings.SetHideHints(true);
        settings.SetTargetBlockPosition(TargetBlockPosition.TopRight);
        settings.SetHintPreference(HintKind.StructureTool, false);
        settings.SetKeybind(KeybindAction.Jump, KeyboardKey.KeyJ);
        var serialized = ClientPreferencesJson.Serialize(settings);
        var restored = ClientPreferencesJson.Deserialize(serialized);
        Assert.Equal(settings.Capture(), restored.Capture());
        Assert.Equal(0ul, restored.Revision);
        Assert.Contains("\"renderDistanceChunks\": 18", serialized);
        Assert.Contains("\"KeyJ\"", serialized);
    }

    [Fact]
    public void InvalidConfigsAreRejectedWithoutPartiallyApplyingThem()
    {
        var valid = ClientPreferencesJson.Serialize(new ClientPreferences());
        Assert.Throws<ArgumentException>(() =>
            ClientPreferencesJson.Deserialize(
                valid.Replace("\"renderDistanceChunks\": 4",
                    "\"renderDistanceChunks\": 100")));
        Assert.Throws<ArgumentException>(() =>
            ClientPreferencesJson.Deserialize(
                valid.Replace("\"jump\": \"Space\"",
                    "\"jump\": \"KeyW\"")));
        Assert.Throws<JsonException>(() =>
            ClientPreferencesJson.Deserialize("{ invalid"));
    }
}
