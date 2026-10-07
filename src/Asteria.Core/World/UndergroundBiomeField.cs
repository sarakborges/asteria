namespace Asteria.Core.World;

/// <summary>
/// Deterministic underground-biome identity placement. It never decides
/// whether a cave exists; the terrain-density owner gates this field to
/// actual carved cave voids.
/// </summary>
public sealed class UndergroundBiomeField
{
    private readonly BiomeField? _placement;

    public UndergroundBiomeField(
        ulong seed,
        DimensionDefinition dimension,
        BiomeRegistry biomes)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            biomes);

        if (dimension.UndergroundBiomes.Count == 0)
        {
            return;
        }

        var rules =
            dimension.UndergroundBiomes
                .Select(
                    biomeId =>
                    {
                        var definition =
                            biomes.Get(
                                biomeId);
                        return new BiomeFieldRuleSource(
                            definition.Id,
                            definition.UndergroundLayout ??
                            throw new ArgumentException(
                                $"Underground biome {definition.Id} requires undergroundLayout."));
                    })
                .ToArray();

        _placement =
            new BiomeField(
                seed,
                rules);
    }

    public bool HasBiomes =>
        _placement is not null;

    public BiomeSample? Sample(
        int worldX,
        int worldZ) =>
        _placement?.Sample(
            worldX,
            worldZ);
}
