using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Asteria.ModAPI;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Content;

/// <summary>
/// Scans validated C# mod manifests, persists explicit opt-in, and loads
/// enabled modules in deterministic dependency order at process startup.
/// Code mods are trusted native extensions, not a security sandbox.
/// </summary>
internal sealed class ModLoader
{
    private readonly string _root;
    private readonly string _selectionPath;
    private readonly Dictionary<string, ModPreference> _preferences;
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly List<(string ModId, Func<BeforeBlockBreak, bool> Hook)> _breakHooks = new();
    private readonly List<AssemblyLoadContext> _contexts = new();
    private bool _loaded;
    private bool _restartRequired;
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);

    public ModLoader(string modsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsDirectory);
        _root = Path.GetFullPath(modsDirectory);
        _selectionPath = Path.Combine(_root, "selection.json");
        Directory.CreateDirectory(_root);
        _preferences = LoadPreferences();
    }

    public IReadOnlyList<ModDescriptor> Installed()
    {
        var mods = new List<ModDescriptor>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateDirectories(_root)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var folder = Path.GetFileName(path);
            try
            {
                // Use the existing trusted pack-name validation for folders.
                _ = new Asteria.Core.Content.PackSelection(folder);
                var manifest = Path.Combine(path, "mod.json");
                if (!File.Exists(manifest)) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = document.RootElement;
                if (root.GetProperty("format").GetInt32() != 1)
                    throw new FormatException("Unsupported mod manifest format.");
                var id = root.GetProperty("id").GetString()!;
                if (string.IsNullOrWhiteSpace(id) ||
                    id.Split(':').Length != 2 || id.Contains(' '))
                    throw new FormatException("Mod id must be namespaced.");
                var name = root.GetProperty("name").GetString()!;
                var version = root.GetProperty("version").GetString()!;
                var dllName = root.GetProperty("assembly").GetString()!;
                var entryType = root.GetProperty("entryType").GetString()!;
                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(version) ||
                    string.IsNullOrWhiteSpace(entryType) ||
                    string.IsNullOrWhiteSpace(dllName) ||
                    !dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    dllName.Contains('/') || dllName.Contains('\\') ||
                    dllName is "." or "..")
                    throw new FormatException("Invalid mod manifest fields.");
                if (!ids.Add(id))
                    throw new FormatException($"Duplicate installed mod id: {id}");
                string[] dependencies = Array.Empty<string>();
                if (root.TryGetProperty("dependencies", out var deps))
                {
                    if (deps.ValueKind != JsonValueKind.Array)
                        throw new FormatException("Dependencies must be an array.");
                    dependencies = deps.EnumerateArray().Select(value =>
                        value.GetString() ?? throw new FormatException("Invalid dependency."))
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray();
                }
                mods.Add(new ModDescriptor(id, folder, name, version,
                    Path.Combine(path, dllName), entryType, dependencies));
            }
            catch (Exception error) when (error is IOException or JsonException
                or FormatException or ArgumentException or InvalidOperationException
                or KeyNotFoundException)
            {
                _errors["invalid:" + folder] = error.Message;
                GD.PushWarning($"Invalid mod in '{folder}': {error.Message}");
            }
        }
        return mods;
    }

    public object Catalog()
    {
        var descriptors = Installed();
        return new
        {
            mods = descriptors.Select(mod => new
            {
                id = mod.Id, name = mod.Name, version = mod.Version,
                enabled = IsEnabled(mod.Id),
                settings = SettingsText(mod.Id),
                status = _errors.TryGetValue(mod.Id, out var error)
                    ? "error" : _active.Contains(mod.Id) ? "loaded" : "disabled",
                error = _errors.GetValueOrDefault(mod.Id),
            }).ToArray(),
            restartRequired = _restartRequired,
            invalid = _errors.Where(item => item.Key.StartsWith("invalid:", StringComparison.Ordinal))
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new { folder = item.Key["invalid:".Length..], error = item.Value }).ToArray(),
        };
    }

    public bool IsEnabled(string modId) =>
        _preferences.TryGetValue(modId, out var setting) && setting.Enabled;

    public string SettingsText(string modId) =>
        _preferences.TryGetValue(modId, out var setting)
            ? setting.Settings.GetRawText() : "{}";

    public void Configure(string id, bool enabled, JsonElement settings)
    {
        if (_loaded && !Installed().Any(mod => mod.Id == id))
            throw new InvalidDataException($"Mod '{id}' is not installed.");
        if (settings.ValueKind != JsonValueKind.Object)
            throw new FormatException("Mod settings must be a JSON object.");
        var existing = Installed().Select(mod => mod.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (!existing.Contains(id))
            throw new InvalidDataException($"Mod '{id}' is not installed.");

        var next = new Dictionary<string, ModPreference>(_preferences, StringComparer.Ordinal)
        {
            [id] = new ModPreference(enabled, settings.Clone())
        };
        var payload = JsonSerializer.Serialize(
            next.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => new
                {
                    enabled = item.Value.Enabled,
                    settings = item.Value.Settings
                }, StringComparer.Ordinal));
        var tmp = _selectionPath + ".tmp";
        File.WriteAllText(tmp, payload);
        File.Move(tmp, _selectionPath, overwrite: true);
        _preferences.Clear();
        foreach (var item in next) _preferences.Add(item.Key, item.Value);
        _restartRequired = true;
    }

    public void LoadEnabled()
    {
        if (_loaded) throw new InvalidOperationException("Mods already initialized.");
        _loaded = true;
        var installed = Installed().ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        bool Load(string id)
        {
            if (loaded.Contains(id)) return true;
            if (!installed.TryGetValue(id, out var mod) || !IsEnabled(id))
            {
                _errors[id] = "Missing or disabled mod dependency.";
                return false;
            }
            if (!visiting.Add(id))
            {
                _errors[id] = "Cyclic mod dependencies.";
                return false;
            }
            try
            {
                foreach (var dependency in mod.Dependencies)
                    if (!Load(dependency))
                    {
                        _errors[id] = $"Unsatisfied dependency: {dependency}";
                        return false;
                    }
                LoadAssembly(mod);
                loaded.Add(id);
                _active.Add(id);
                return true;
            }
            catch (Exception error)
            {
                _errors[id] = error.GetBaseException().Message;
                GD.PushWarning($"Cannot load mod {id}: {_errors[id]}");
                return false;
            }
            finally
            {
                visiting.Remove(id);
            }
        }

        foreach (var id in installed.Keys.OrderBy(id => id, StringComparer.Ordinal))
            if (IsEnabled(id)) Load(id);
    }

    private void LoadAssembly(ModDescriptor mod)
    {
        if (!File.Exists(mod.AssemblyPath))
            throw new FileNotFoundException("Mod DLL not found.", mod.AssemblyPath);

        var context = new ModAssemblyContext(mod.Id, mod.AssemblyPath);
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(mod.AssemblyPath));
        var type = assembly.GetType(mod.EntryType, throwOnError: true)
            ?? throw new TypeLoadException(mod.EntryType);
        if (!typeof(IAsteriaMod).IsAssignableFrom(type))
            throw new InvalidDataException($"Entry point {mod.EntryType} must implement IAsteriaMod.");
        var instance = Activator.CreateInstance(type) as IAsteriaMod
            ?? throw new InvalidDataException("Could not construct mod entry point.");
        var settings = _preferences.TryGetValue(mod.Id, out var config)
            ? config.Settings : EmptySettings();
        var hooks = new ModHooks(mod.Id, _breakHooks);
        instance.Initialize(new ModContext(mod.Id, settings, hooks));
        _contexts.Add(context);
    }

    public bool AllowBlockBreak(string blockId, WorldVoxelCoord position)
    {
        var snapshot = new BeforeBlockBreak(blockId, position.X, position.Y, position.Z);
        foreach (var entry in _breakHooks)
        {
            if (_errors.ContainsKey(entry.ModId)) continue;
            try
            {
                if (!entry.Hook(snapshot)) return false;
            }
            catch (Exception error)
            {
                _errors[entry.ModId] = $"Block hook failed: {error.Message}";
                GD.PushWarning($"Disabling hooks from {entry.ModId}: {error.Message}");
                return false;
            }
        }
        return true;
    }

    private Dictionary<string, ModPreference> LoadPreferences()
    {
        var result = new Dictionary<string, ModPreference>(StringComparer.Ordinal);
        if (!File.Exists(_selectionPath)) return result;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_selectionPath));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var item = property.Value;
                if (!item.TryGetProperty("enabled", out var enabled) ||
                    enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !item.TryGetProperty("settings", out var settings) ||
                    settings.ValueKind != JsonValueKind.Object)
                    throw new FormatException("Invalid mod settings file.");
                result.Add(property.Name, new ModPreference(enabled.GetBoolean(), settings.Clone()));
            }
        }
        catch (Exception error) when (error is IOException or JsonException or FormatException
            or InvalidOperationException or ArgumentException)
        {
            GD.PushWarning($"Mod configuration invalid; no mods enabled: {error.Message}");
            result.Clear();
        }
        return result;
    }

    private static JsonElement EmptySettings() =>
        JsonDocument.Parse("{}").RootElement.Clone();

    private sealed record ModPreference(bool Enabled, JsonElement Settings);
    private sealed record ModContext(string ModId, JsonElement Settings, IGameplayHooks Hooks)
        : IModContext;

    private sealed class ModHooks : IGameplayHooks
    {
        private readonly string _id;
        private readonly List<(string ModId, Func<BeforeBlockBreak, bool> Hook)> _handlers;

        public ModHooks(string id,
            List<(string ModId, Func<BeforeBlockBreak, bool> Hook)> handlers)
        {
            _id = id;
            _handlers = handlers;
        }

        public void RegisterBeforeBlockBreak(Func<BeforeBlockBreak, bool> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (_handlers.Count >= 256)
                throw new InvalidOperationException("Too many block-break hooks.");
            _handlers.Add((_id, callback));
        }
    }

    private sealed class ModAssemblyContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public ModAssemblyContext(string id, string assemblyPath)
            : base("asteria.mod." + id, isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(Path.GetFullPath(assemblyPath));
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is "Asteria.ModAPI" or "Asteria.Core")
                return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
                    assembly => assembly.GetName().Name == assemblyName.Name);
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}

internal sealed record ModDescriptor(
    string Id, string Folder, string Name, string Version,
    string AssemblyPath, string EntryType, IReadOnlyList<string> Dependencies);
