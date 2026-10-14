namespace Asteria.Core.Content;

[Flags]
public enum PackDomains : byte
{
    Data = 1,
    Resources = 2,
    Both = Data | Resources,
}

public readonly record struct PackOverlayEntry
{
    public PackOverlayEntry(string name, PackDomains domains)
    {
        Name = new PackSelection(name).Name;
        if (name == PackSelection.DefaultPackName)
            throw new ArgumentException("Default is always the built-in base pack.", nameof(name));
        if (domains is not (PackDomains.Data or PackDomains.Resources or PackDomains.Both))
            throw new ArgumentOutOfRangeException(nameof(domains));
        Domains = domains;
    }

    public string Name { get; }
    public PackDomains Domains { get; }
}

public readonly record struct PackSelection
{
    public const string DefaultPackName = "default";

    public PackSelection(string name)
    {
        Name = ValidatePackName(name, nameof(name));
        Overlays = Array.Empty<PackOverlayEntry>();
    }

    public PackSelection(IEnumerable<PackOverlayEntry> overlays)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        Name = DefaultPackName;
        var result = overlays.ToArray();
        if (result.Any(entry => entry.Name is null ||
            entry.Domains is not (PackDomains.Data or PackDomains.Resources or PackDomains.Both)))
            throw new ArgumentException("Invalid pack overlay.", nameof(overlays));
        if (result.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new ArgumentException("A pack cannot be enabled twice.", nameof(overlays));
        Overlays = Array.AsReadOnly(result);
    }

    public string Name { get; }
    public IReadOnlyList<PackOverlayEntry> Overlays { get; }
    public static PackSelection Default => new(DefaultPackName);

    private static string ValidatePackName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Pack name cannot be empty.", parameterName);

        foreach (var character in value)
        {
            if (!(character is >= 'a' and <= 'z' ||
                  character is >= '0' and <= '9' ||
                  character is '.' or '_' or '-'))
                throw new ArgumentException(
                    "Pack names must use lowercase ASCII letters, digits, '.', '_' or '-'.",
                    parameterName);
        }

        if (value is "." or "..")
            throw new ArgumentException("Pack name cannot be a relative path segment.", parameterName);

        return value;
    }
}
