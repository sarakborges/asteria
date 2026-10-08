namespace Asteria.Core.World;

public enum CaveSpikeDirection : byte
{
    Up = 0,
    Down = 1,
}

/// <summary>Validated, authored placement and clearance for one spike material.</summary>
public sealed class BiomeCaveSpikeDefinition
{
    public BiomeCaveSpikeDefinition(
        string block,
        float chance,
        int minHeight,
        int maxHeight,
        int minClearance,
        IEnumerable<CaveSpikeDirection> directions,
        IEnumerable<string>? surfaceBiomes = null,
        CaveSpikeClusterDefinition? cluster = null,
        int minSpacing = 0)
    {
        BlockDefinition.ValidateId(block);
        if (!float.IsFinite(chance) || chance <= 0f || chance > 1f)
            throw new ArgumentOutOfRangeException(nameof(chance));
        if (minHeight is < 1 or > SpikeSegmentState.MaximumHeight ||
            maxHeight < minHeight || maxHeight > SpikeSegmentState.MaximumHeight)
            throw new ArgumentOutOfRangeException(nameof(maxHeight));
        if (minClearance < minHeight || minClearance > 64)
            throw new ArgumentOutOfRangeException(nameof(minClearance));
        if (minSpacing is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(minSpacing));

        var values = directions?.ToArray() ??
            throw new ArgumentNullException(nameof(directions));
        if (values.Length == 0 ||
            values.Distinct().Count() != values.Length ||
            values.Any(value => !Enum.IsDefined(value)))
            throw new ArgumentException(
                "Spike directions must be unique valid orientations.",
                nameof(directions));

        var allowedBiomes = surfaceBiomes?.ToArray() ??
            Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var biomeId in allowedBiomes)
        {
            BiomeDefinition.ValidateId(biomeId);
            if (!seen.Add(biomeId))
                throw new ArgumentException(
                    $"Duplicate cave spike surface biome: {biomeId}",
                    nameof(surfaceBiomes));
        }

        SurfaceBiomes = Array.AsReadOnly(allowedBiomes);
        Cluster = cluster;
        MinSpacing = minSpacing;
        Block = block;
        Chance = chance;
        MinHeight = minHeight;
        MaxHeight = maxHeight;
        MinClearance = minClearance;
        Directions = Array.AsReadOnly(values);
    }

    public string Block { get; }
    public float Chance { get; }
    public int MinHeight { get; }
    public int MaxHeight { get; }
    public int MinClearance { get; }
    public IReadOnlyList<CaveSpikeDirection> Directions { get; }
    /// <summary>Empty means any surface biome in the same Sphere.</summary>
    public IReadOnlyList<string> SurfaceBiomes { get; }
    public CaveSpikeClusterDefinition? Cluster { get; }
    /// <summary>Horizontal Chebyshev radius around each accepted column.</summary>
    public int MinSpacing { get; }
}
