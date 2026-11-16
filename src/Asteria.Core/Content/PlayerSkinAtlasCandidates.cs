namespace Asteria.Core.Content;

/// <summary>
/// A canonical player skin may be supplied at any MineClone atlas resolution.
/// Higher-resolution siblings take priority; pack-specific named skins do not
/// acquire unrelated fallback paths.
/// </summary>
public static class PlayerSkinAtlasCandidates
{
    private static readonly string[] ResolutionFiles =
        ["512.png", "256.png", "128.png", "64.png"];

    public static IReadOnlyList<string> Paths(string authoredSkin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredSkin);
        var lastSlash = authoredSkin.LastIndexOf('/');
        var file = authoredSkin[(lastSlash + 1)..];
        if (!ResolutionFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
            return [authoredSkin];

        var directory = authoredSkin[..(lastSlash + 1)];
        return ResolutionFiles.Select(fileName => directory + fileName).ToArray();
    }
}
