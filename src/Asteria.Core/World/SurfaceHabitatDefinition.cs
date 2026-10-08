using System.Collections.ObjectModel;

namespace Asteria.Core.World;

/// <summary>Ordered, validated surface habitat bands within one biome.</summary>
public sealed record SurfaceHabitatBand
{
    public SurfaceHabitatBand(string id, float maximum)
    {
        SurfaceHabitatWeights.ValidateId(id);
        if (!float.IsFinite(maximum) || maximum is <= -1f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        Id = id;
        Maximum = maximum;
    }
    public string Id { get; }
    public float Maximum { get; }
}

public sealed class SurfaceHabitatDefinition
{
    public SurfaceHabitatDefinition(int scale, float transitionWidth,
        IEnumerable<SurfaceHabitatBand> bands)
    {
        if (scale is < 8 or > 2048)
            throw new ArgumentOutOfRangeException(nameof(scale));
        if (!float.IsFinite(transitionWidth) || transitionWidth is < 0f or > 0.5f)
            throw new ArgumentOutOfRangeException(nameof(transitionWidth));
        var values = bands?.ToArray() ?? throw new ArgumentNullException(nameof(bands));
        if (values.Length is < 2 or > 8 || values.Any(value => value is null))
            throw new ArgumentException("Habitats require 2..8 ordered bands.", nameof(bands));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var previous = -1f;
        foreach (var band in values)
        {
            if (!seen.Add(band.Id) || band.Maximum - previous <= transitionWidth)
                throw new ArgumentException(
                    "Habitat bands require unique ids and increasing bounds wider than transitionWidth.",
                    nameof(bands));
            previous = band.Maximum;
        }
        if (values[^1].Maximum != 1f)
            throw new ArgumentException("Last habitat band must end at 1.", nameof(bands));
        Scale = scale;
        TransitionWidth = transitionWidth;
        Bands = Array.AsReadOnly(values);
    }

    public int Scale { get; }
    public float TransitionWidth { get; }
    public IReadOnlyList<SurfaceHabitatBand> Bands { get; }

    public void ValidateWeights(SurfaceHabitatWeights? weights)
    {
        if (weights is null) return;
        foreach (var id in weights.Ids)
            if (!Bands.Any(band => string.Equals(band.Id, id, StringComparison.Ordinal)))
                throw new ArgumentException($"Unknown surface habitat band {id}.");
    }

    public double Weight(double noise, SurfaceHabitatWeights weights)
    {
        var half = TransitionWidth * 0.5d;
        for (var index = 0; index < Bands.Count - 1; index++)
        {
            var boundary = Bands[index].Maximum;
            if (noise < boundary - half)
                return weights.For(Bands[index].Id);
            if (half > 0d && noise <= boundary + half)
            {
                var progress = WorldGenerationEntropy.SmoothStep(Math.Clamp(
                    (noise - boundary + half) / TransitionWidth, 0d, 1d));
                return weights.For(Bands[index].Id) * (1d - progress) +
                       weights.For(Bands[index + 1].Id) * progress;
            }
        }
        return weights.For(Bands[^1].Id);
    }
}

/// <summary>Unspecified habitat weights intentionally mean zero density.</summary>
public sealed class SurfaceHabitatWeights
{
    private readonly IReadOnlyDictionary<string, float> _values;
    public SurfaceHabitatWeights(IEnumerable<KeyValuePair<string, float>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var map = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (id, weight) in values)
        {
            ValidateId(id);
            if (!float.IsFinite(weight) || weight is < 0f or > 4f ||
                !map.TryAdd(id, weight))
                throw new ArgumentException("Habitat weights require unique keys in 0..4.", nameof(values));
        }
        if (map.Count is < 1 or > 8)
            throw new ArgumentException("Habitat weights must contain 1..8 entries.", nameof(values));
        _values = new ReadOnlyDictionary<string, float>(map);
    }

    internal static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim() ||
            id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-'))
            throw new ArgumentException("Habitat id must be a simple local id.", nameof(id));
    }

    public IEnumerable<string> Ids => _values.Keys;
    public float For(string id) => _values.TryGetValue(id, out var value) ? value : 0f;
}

/// <summary>Immutable, deterministic distribution shared by decorators and structures.</summary>
public sealed class SurfaceHabitatField
{
    private readonly ulong _seed;
    private readonly Dictionary<string, (SurfaceHabitatDefinition Definition, GenerationDomain Domain)> _regions;

    public SurfaceHabitatField(ulong seed, IEnumerable<BiomeDefinition> biomes)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        _seed = seed;
        _regions = new(StringComparer.Ordinal);
        foreach (var biome in biomes)
        {
            if (biome.SurfaceHabitats is not { } definition) continue;
            _regions.Add(biome.Id, (definition,
                GenerationDomain.Named($"surface/habitat/v1/{biome.Id}")));
        }
    }

    public void Validate(string biomeId, SurfaceHabitatWeights? weights)
    {
        if (weights is null) return;
        if (!_regions.TryGetValue(biomeId, out var region))
            throw new ArgumentException($"Biome {biomeId} has habitat weights but no surfaceHabitats.");
        region.Definition.ValidateWeights(weights);
    }

    public double Sample(string biomeId, int x, int z)
    {
        var region = _regions[biomeId];
        return WorldGenerationEntropy.ValueNoise2D(
            _seed, region.Domain, x, z, checked((uint)region.Definition.Scale));
    }

    public double Weight(string biomeId, SurfaceHabitatWeights? weights, int x, int z) =>
        weights is null ? 1d :
        _regions[biomeId].Definition.Weight(Sample(biomeId, x, z), weights);

    public double Weight(string biomeId, SurfaceHabitatWeights weights, double noise) =>
        _regions[biomeId].Definition.Weight(noise, weights);
}
