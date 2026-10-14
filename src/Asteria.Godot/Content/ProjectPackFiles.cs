using System.Reflection;
using System.Text;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

/// <summary>
/// Resolves authored files from enabled external packs, then the built-in
/// pack. The latter is embedded in the managed game assembly for exports.
/// </summary>
internal static class ProjectPackFiles
{
    private const string EmbeddedPrefix = "asteria.pack.default/";
    private static readonly Assembly BuiltInAssembly = typeof(Main).Assembly;
    private static readonly HashSet<string> EmbeddedNames =
        BuiltInAssembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(EmbeddedPrefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    public static string ExternalRoot(string name) =>
        ProjectPackDirectories.Catalog.Resolve(name);

    public static string AbsoluteResourcePath(PackSelection selection, string relativePath)
    {
        ValidateRelativePath(relativePath);
        foreach (var layer in selection.Overlays.Reverse())
        {
            if ((layer.Domains & PackDomains.Resources) == 0) continue;
            var path = Path.Combine(ExternalRoot(layer.Name), "resources", relativePath);
            if (File.Exists(path)) return path;
        }
        return BuiltInAbsolutePath("resources/" + relativePath);
    }

    public static bool ResourceExists(PackSelection selection, string relativePath)
    {
        ValidateRelativePath(relativePath);
        foreach (var layer in selection.Overlays.Reverse())
        {
            if ((layer.Domains & PackDomains.Resources) != 0 &&
                File.Exists(Path.Combine(ExternalRoot(layer.Name), "resources", relativePath)))
                return true;
        }
        return BuiltInExists("resources/" + relativePath);
    }

    public static string ReadUiText(PackSelection selection, string relativePath)
    {
        ValidateRelativePath(relativePath);
        foreach (var layer in selection.Overlays.Reverse())
        {
            if ((layer.Domains & PackDomains.Resources) == 0) continue;
            var path = Path.Combine(ExternalRoot(layer.Name), "ui", relativePath);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        return ReadBuiltInText("ui/" + relativePath);
    }

    public static IReadOnlyList<string> ReadData(
        PackSelection selection, string category, bool required)
    {
        ValidateRelativePath(category);
        var documents = ReadBuiltInData(category);
        if (required && documents.Count == 0)
            throw new InvalidDataException($"No built-in definitions in {category}.");

        var overlays = selection.Overlays
            .Where(entry => (entry.Domains & PackDomains.Data) != 0)
            .Select(entry => ReadExternalData(entry.Name, category));
        // Creative inventory category IDs are intentionally unnamespaced;
        // their registry validates the category-specific format after overlay.
        // All other pack definitions retain the namespaced-ID requirement.
        var idFormat = category == "inventory_categories"
            ? PackDocumentIdFormat.RegistryDefined
            : PackDocumentIdFormat.Namespaced;
        return PackDocumentOverlay.Compose(documents, overlays, idFormat);
    }

    private static IReadOnlyList<string> ReadBuiltInData(string category)
    {
        var local = Path.Combine(
            ProjectSettings.GlobalizePath("res://packs/default/data"), category);
        if (Directory.Exists(local))
            return ReadJsonDirectory(local);

        var prefix = EmbeddedPrefix + "data/" + category + "/";
        return EmbeddedNames
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) &&
                !name[(prefix.Length)..].Contains('/'))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => ReadEmbeddedText(name))
            .ToArray();
    }

    private static IReadOnlyList<string> ReadExternalData(string name, string category)
    {
        var path = Path.Combine(ExternalRoot(name), "data", category);
        return Directory.Exists(path) ? ReadJsonDirectory(path) : Array.Empty<string>();
    }

    private static IReadOnlyList<string> ReadJsonDirectory(string directory) =>
        Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();

    private static bool BuiltInExists(string relativePath)
    {
        var local = Path.Combine(
            ProjectSettings.GlobalizePath("res://packs/default"), relativePath);
        return File.Exists(local) || EmbeddedNames.Contains(EmbeddedPrefix + relativePath.Replace('\\', '/'));
    }

    private static string ReadBuiltInText(string relativePath)
    {
        var local = Path.Combine(
            ProjectSettings.GlobalizePath("res://packs/default"), relativePath);
        return File.Exists(local)
            ? File.ReadAllText(local)
            : ReadEmbeddedText(EmbeddedPrefix + relativePath);
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = BuiltInAssembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Built-in pack resource is missing: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string BuiltInAbsolutePath(string relativePath)
    {
        var local = Path.Combine(
            ProjectSettings.GlobalizePath("res://packs/default"), relativePath);
        if (File.Exists(local)) return local;

        var name = EmbeddedPrefix + relativePath;
        using var stream = BuiltInAssembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Built-in pack resource is missing: {relativePath}");

        // Resource consumers may need an absolute path (e.g. native image
        // decoders). Extract on demand into a versioned internal cache,
        // never distribute a loose default pack beside the executable.
        var cache = Path.Combine(OS.GetUserDataDir(), ".internal-pack-cache");
        var stamp = Path.Combine(cache, ".assembly-version");
        var version = BuiltInAssembly.ManifestModule.ModuleVersionId.ToString("N");
        if (!File.Exists(stamp) || File.ReadAllText(stamp) != version)
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
            Directory.CreateDirectory(cache);
            File.WriteAllText(stamp, version);
        }
        var target = Path.Combine(cache, relativePath);
        if (!File.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = File.Create(target);
            stream.CopyTo(output);
        }
        return target;
    }

    public static Image LoadImage(PackSelection selection, string relativePath)
    {
        var absolute = AbsoluteResourcePath(selection, relativePath);
        var image = Image.LoadFromFile(absolute);
        if (image is null || image.IsEmpty())
            throw new InvalidDataException($"Pack image could not be decoded: {relativePath}");
        return image;
    }

    public static Texture2D LoadTexture(PackSelection selection, string relativePath)
    {
        var image = LoadImage(selection, relativePath);
        return ImageTexture.CreateFromImage(image)
            ?? throw new InvalidDataException($"Pack image could not be converted: {relativePath}");
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') ||
            path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new ArgumentException($"Invalid pack-relative path: {path}", nameof(path));
    }
}
