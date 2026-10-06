namespace Asteria.Client.Rendering;

public sealed class TerrainTextureLookup
{
    private readonly Dictionary<string, int> _indices;

    public TerrainTextureLookup(IReadOnlyDictionary<string, int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        _indices = new Dictionary<string, int>(indices, StringComparer.Ordinal);
    }

    public int GetIndex(string path) =>
        _indices.TryGetValue(path, out var index)
            ? index
            : throw new KeyNotFoundException($"Texture is not present in terrain lookup: {path}");
}
