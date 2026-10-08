using Asteria.Core.Settings;
using Godot;

namespace Asteria.Client.Settings;

/// <summary>
/// Platform-local persistence only. Core validates every deserialized value.
/// Preferences are independent of packs, worlds and active Spheres.
/// </summary>
public sealed class ClientPreferencesStore
{
    private readonly string _path;

    public ClientPreferencesStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Config path must not be empty.", nameof(path));
        }

        _path = path;
    }

    public static ClientPreferencesStore FromUserDataDirectory() =>
        new(Path.Combine(OS.GetUserDataDir(), "client-preferences.json"));

    public ClientPreferences Load()
    {
        if (!File.Exists(_path))
        {
            return new ClientPreferences();
        }

        try
        {
            return ClientPreferencesJson.Deserialize(
                File.ReadAllText(_path));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
                or System.Text.Json.JsonException
                or ArgumentException or NotSupportedException)
        {
            GD.PushWarning(
                $"client.preferences load failed path={_path} error={exception.Message}");
            return new ClientPreferences();
        }
    }

    public bool TrySave(ClientPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(_path)!);
            File.WriteAllText(
                temporary,
                ClientPreferencesJson.Serialize(preferences));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning(
                $"client.preferences save failed path={_path} error={exception.Message}");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException)
            {
                // A failed temporary-file cleanup must not hide the save result.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup is best-effort after reporting the primary error.
            }
        }
    }
}
