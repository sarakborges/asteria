namespace Asteria.Core.World;

/// <summary>
/// 3D biome-identity overlay. Horizontal placement is deterministic and
/// region-shaped; surface biomes participate as explicit "no volume biome"
/// competitors so volume regions stay bounded without becoming surface owners.
/// </summary>
public sealed class VolumeBiomeField
{
    private readonly BiomeField? _placement;
    private readonly IReadOnlyDictionary<
        string,
        BiomeFloatingFormationDefinition> _formations;

    public VolumeBiomeField(
        ulong seed,
        DimensionDefinition dimension,
        BiomeRegistry biomes)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            biomes);

        var volumeDefinitions =
            dimension.VolumeBiomes
                .Select(
                    biomes.Get)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();

        _formations =
            volumeDefinitions
                .ToDictionary(
                    definition =>
                        definition.Id,
                    definition =>
                        definition.Terrain3d?.FloatingFormation ??
                        throw new ArgumentException(
                            $"Volume biome {definition.Id} requires terrain3d.floatingFormation."),
                    StringComparer.Ordinal);

        if (volumeDefinitions.Length == 0)
        {
            return;
        }

        var placementRules =
            dimension.SurfaceBiomes
                .Select(
                    biomeId =>
                    {
                        var definition =
                            biomes.Get(
                                biomeId);
                        return new BiomeFieldRuleSource(
                            definition.Id,
                            definition.SurfaceLayout ??
                            throw new ArgumentException(
                                $"Surface biome {definition.Id} requires surfaceLayout."));
                    })
                .Concat(
                    volumeDefinitions
                        .Select(
                            definition =>
                                new BiomeFieldRuleSource(
                                    definition.Id,
                                    definition.VolumeLayout ??
                                    throw new ArgumentException(
                                        $"Volume biome {definition.Id} requires volumeLayout."))))
                .ToArray();

        _placement =
            new BiomeField(
                seed,
                placementRules);
    }

    public bool HasBiomes =>
        _placement is not null;

    public BiomeSample? Sample(
        int worldX,
        int worldY,
        int worldZ)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldY));
        }

        var sample =
            SamplePlacement(
                worldX,
                worldZ);

        if (sample is null ||
            !_formations.TryGetValue(
                sample.Primary,
                out var formation) ||
            worldY < formation.MinY ||
            worldY > formation.MaxY)
        {
            return null;
        }

        return sample;
    }

    internal BiomeSample? SamplePlacement(
        int worldX,
        int worldZ)
    {
        if (_placement is null)
        {
            return null;
        }

        var sample =
            _placement.Sample(
                worldX,
                worldZ);

        return _formations.ContainsKey(
            sample.Primary)
            ? sample
            : null;
    }
}
