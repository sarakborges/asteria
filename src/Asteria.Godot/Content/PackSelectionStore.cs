using System.Text.Json;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

/// <summary>Persists next-launch pack selection; never changes live registries.</summary>
internal sealed class PackSelectionStore
{
    private readonly PackDirectoryCatalog _directories;
    private readonly string _settingsPath;

    public PackSelectionStore(string userDataRoot)
    {
        _directories = ProjectPackDirectories.Catalog;
        _settingsPath = Path.Combine(userDataRoot, "pack-selection.json");
        Directory.CreateDirectory(Path.Combine(userDataRoot, "packs"));
        Current = ReadSelection();
    }

    public PackSelection Current { get; private set; }

    public IReadOnlyList<InstalledPack> Installed() =>
        _directories.Discover()
            .Select(ReadPack)
            .ToArray();

    private static InstalledPack ReadPack(PackDirectoryLocation location)
    {
        var folder = location.Name;
        try
        {
            _ = new PackOverlayEntry(folder, PackDomains.Both);
            var manifest = Path.Combine(location.Directory, "pack.json");
            if (!File.Exists(manifest))
                throw new InvalidDataException("Missing pack.json manifest.");

            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("pack.json must be a JSON object.");
            var root = document.RootElement;
            if (root.GetProperty("format").GetInt32() != 1)
                throw new InvalidDataException("Unsupported pack format.");
            var id = root.GetProperty("id").GetString()!;
            if (string.IsNullOrWhiteSpace(id) || !id.Contains(':'))
                throw new InvalidDataException("Pack ID must be namespaced.");
            var name = root.GetProperty("name").GetString()!;
            var version = root.GetProperty("version").GetString()!;
            var description = root.TryGetProperty("description", out var desc)
                ? desc.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException("Pack name and version are required.");

            return new InstalledPack(folder, id, name, version, description);
        }
        catch (Exception error) when (error is IOException or JsonException
            or InvalidOperationException or ArgumentException or KeyNotFoundException
            or FormatException)
        {
            GD.PushWarning($"Invalid pack '{folder}': {error.Message}");
            return new InstalledPack(folder, "", folder, "", "", error.Message);
        }
    }

    public void Configure(IReadOnlyList<PackOverlayEntry> overlays)
    {
        var next = new PackSelection(overlays);
        var installed = Installed().Where(pack => pack.Error is null).Select(pack => pack.Folder)
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
            var installed = Installed().Where(pack => pack.Error is null).Select(pack => pack.Folder)
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
    string Folder, string Id, string Name, string Version, string Description,
    string? Error = null);
