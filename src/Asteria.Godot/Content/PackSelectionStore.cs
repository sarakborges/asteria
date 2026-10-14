using System.Text.Json;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

/// <summary>Persists next-launch pack selection; never changes live registries.</summary>
internal sealed class PackSelectionStore
{
    private readonly string _root;
    private readonly string _settingsPath;

    public PackSelectionStore(string userDataRoot)
    {
        _root = Path.Combine(userDataRoot, "packs");
        _settingsPath = Path.Combine(userDataRoot, "pack-selection.json");
        Directory.CreateDirectory(_root);
        Current = ReadSelection();
    }

    public PackSelection Current { get; private set; }

    public IReadOnlyList<InstalledPack> Installed()
    {
        var packs = new List<InstalledPack>();
        foreach (var path in Directory.EnumerateDirectories(_root)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var folder = Path.GetFileName(path);
            if (folder == PackSelection.DefaultPackName) continue;
            try
            {
                _ = new PackOverlayEntry(folder, PackDomains.Both);
                var manifest = Path.Combine(path, "pack.json");
                if (!File.Exists(manifest)) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = document.RootElement;
                if (root.GetProperty("format").GetInt32() != 1) continue;
                var id = root.GetProperty("id").GetString()!;
                if (string.IsNullOrWhiteSpace(id) || !id.Contains(':')) continue;
                var name = root.GetProperty("name").GetString()!;
                var version = root.GetProperty("version").GetString()!;
                var description = root.TryGetProperty("description", out var desc)
                    ? desc.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;
                packs.Add(new InstalledPack(folder, id, name, version, description));
            }
            catch (Exception error) when (error is IOException or JsonException
                or InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                GD.PushWarning($"Invalid external pack '{folder}': {error.Message}");
            }
        }
        return packs;
    }

    public void Configure(IReadOnlyList<PackOverlayEntry> overlays)
    {
        var next = new PackSelection(overlays);
        var installed = Installed().Select(pack => pack.Folder)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var entry in next.Overlays)
            if (!installed.Contains(entry.Name))
                throw new InvalidDataException($"Pack '{entry.Name}' is missing or invalid.");

        var payload = JsonSerializer.Serialize(new
        {
            packs = next.Overlays.Select(entry => new {
                name = entry.Name, domains = entry.Domains.ToString()
            }).ToArray()
        });
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, payload);
        File.Move(temporary, _settingsPath, overwrite: true);
        Current = next;
    }

    private PackSelection ReadSelection()
    {
        if (!File.Exists(_settingsPath)) return PackSelection.Default;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var selection = new PackSelection(ReadEntries(document.RootElement));
            var installed = Installed().Select(pack => pack.Folder)
                .ToHashSet(StringComparer.Ordinal);
            if (selection.Overlays.Any(entry => !installed.Contains(entry.Name)))
                throw new InvalidDataException("Enabled pack is missing or invalid.");
            return selection;
        }
        catch (Exception error) when (error is IOException or JsonException
            or InvalidOperationException or ArgumentException or FormatException)
        {
            GD.PushWarning($"Pack selection invalid; using default: {error.Message}");
            return PackSelection.Default;
        }
    }

    public static IReadOnlyList<PackOverlayEntry> ReadEntries(JsonElement payload)
    {
        if (!payload.TryGetProperty("packs", out var list) ||
            list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 64)
            throw new FormatException("Expected packs array of at most 64 entries.");
        var entries = new List<PackOverlayEntry>();
        foreach (var value in list.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String ||
                !value.TryGetProperty("domains", out var domain) ||
                domain.ValueKind != JsonValueKind.String ||
                !Enum.TryParse<PackDomains>(domain.GetString(), false, out var flags))
                throw new FormatException("Invalid pack selection entry.");
            entries.Add(new PackOverlayEntry(name.GetString()!, flags));
        }
        return entries;
    }
}

internal sealed record InstalledPack(
    string Folder, string Id, string Name, string Version, string Description);
