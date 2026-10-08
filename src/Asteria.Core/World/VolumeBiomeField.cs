namespace Asteria.Core.World;

/// <summary>
/// 3D biome-identity overlay. Horizontal placement is deterministic and
/// region-shaped; surface biomes participate as explicit "no volume biome"
/// competitors so volume regions stay bounded without becoming surface owners.
/// </summary>
public sealed class VolumeBiomeField
{
    private readonly BiomeField? _placement;
    private readonly BiomeField? _cavePlacement;
    private readonly IReadOnlyDictionary<
        string,
        IReadOnlyList<BiomeAdditiveDensityDefinition>> _formations;

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

        var additiveDefinitions = volumeDefinitions
            .Where(biome => biome.VolumeLayout!.Placement == VolumeBiomePlacement.Additive)
            .ToArray();
        var caveDefinitions = volumeDefinitions
            .Where(biome => biome.VolumeLayout!.Placement == VolumeBiomePlacement.CarvedVoid)
            .ToArray();

        _formations =
            additiveDefinitions
                .ToDictionary(
                    definition =>
                        definition.Id,
                    definition =>
                    {
                        var formations = definition.Terrain3d?.Additive;
                        if (formations is null || formations.Count == 0)
                        {
                            throw new ArgumentException(
                                $"Volume biome {definition.Id} requires non-empty terrain3d.additive.");
                        }

                        return formations;
                    },
                    StringComparer.Ordinal);

        if (caveDefinitions.Length > 0)
            _cavePlacement = new BiomeField(
                seed,
                caveDefinitions.Select(biome => new BiomeFieldRuleSource(
                    biome.Id, biome.VolumeLayout!)).ToArray());

        if (additiveDefinitions.Length == 0)
            return;

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
                    additiveDefinitions
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
                placementRules,
                blending: dimension.BiomeBlending);
    }

    public bool HasBiomes =>
        _placement is not null || _cavePlacement is not null;

    /// <summary>Volume identity at a carved cave surface. Density owns occupancy.</summary>
    public BiomeSample? SampleCave(int worldX, int worldZ) =>
        _cavePlacement?.Sample(worldX, worldZ);

    public BiomeSample? Sample(
        int worldX,
        int worldY,
        int worldZ) =>
        SampleAtY(
            SamplePlacement(
                worldX,
                worldZ),
            worldY);

    internal BiomeSample? SampleAtY(
        BiomeSample? sample,
        int worldY)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldY));
        }

        if (sample is null ||
            !_formations.TryGetValue(
                sample.Primary,
                out var formation) ||
            !formation.Any(rule =>
                worldY >= rule.MinY &&
                worldY <= rule.MaxY))
        {
            return null;
        }

        var ownedInfluences =
            sample.Influences
                .Where(influence =>
                    _formations.ContainsKey(
                        influence.BiomeId))
                .ToArray();
        var total =
            ownedInfluences.Sum(
                influence =>
                    influence.Weight);

        if (total <= 0f)
        {
            throw new InvalidOperationException(
                "Volume biome placement selected an owner without a volume influence.");
        }

        return new BiomeSample(
            sample.Primary,
            ownedInfluences
                .Select(influence =>
                    new BiomeInfluence(
                        influence.BiomeId,
                        influence.Weight /
                        total))
                .ToArray());
    }

    internal VolumeBiomePlacementGrid SamplePlacementGrid(
        int originX,
        int originZ,
        int width,
        int depth)
    {
        if (width <= 0 ||
            depth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Volume biome placement grid must be non-empty.");
        }

        _ = checked(originX + width - 1);
        _ = checked(originZ + depth - 1);
        var placements =
            new BiomeSample?[
                checked(width * depth)];

        if (_placement is null)
        {
            return new VolumeBiomePlacementGrid(
                width,
                depth,
                placements);
        }

        var samples =
            _placement.SampleGrid(
                originX,
                originZ,
                width,
                depth);

        for (var z = 0; z < depth; z++)
        {
            for (var x = 0; x < width; x++)
            {
                var sample =
                    samples[x, z];
                if (_formations.ContainsKey(
                        sample.Primary))
                {
                    placements[
                        z * width +
                        x] = sample;
                }
            }
        }

        return new VolumeBiomePlacementGrid(
            width,
            depth,
            placements);
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

internal sealed class VolumeBiomePlacementGrid
{
    private readonly BiomeSample?[] _placements;

    internal VolumeBiomePlacementGrid(
        int width,
        int depth,
        BiomeSample?[] placements)
    {
        Width = width;
        Depth = depth;
        _placements = placements;
    }

    public int Width { get; }

    public int Depth { get; }

    public BiomeSample? this[int x, int z]
    {
        get
        {
            if ((uint)x >= Width ||
                (uint)z >= Depth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x));
            }

            return _placements[
                z * Width +
                x];
        }
    }
}
