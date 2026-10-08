namespace Asteria.Core.World;

/// <summary>
/// Resolves authored ground decorators from a sampled surface biome and
/// its actual supporting surface material; never mutates a world.
/// </summary>
public sealed class SurfaceDecorationField
{
    private readonly ulong _seed;
    private readonly SurfaceTerrainField? _terrain;
    private readonly SurfaceHabitatField _habitats;
    private readonly IReadOnlyDictionary<string, DecorationRule[]> _rules;
    private readonly HashSet<string> _verticalBiomes;

    public SurfaceDecorationField(
        ulong seed,
        IEnumerable<BiomeDefinition> biomes,
        BlockRegistry blocks,
        SurfaceTerrainField? terrain = null,
        SurfaceHabitatField? habitats = null)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(blocks);
        _seed = seed;
        _terrain = terrain;
        var definitions = biomes.ToArray();
        _verticalBiomes = definitions
            .Where(biome => biome.SurfaceLayout is null &&
                            biome.Decorations.Count > 0)
            .Select(biome => biome.Id)
            .ToHashSet(StringComparer.Ordinal);
        _habitats = habitats ?? new SurfaceHabitatField(seed, definitions);
        _rules = definitions
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(
                biome => biome.Id,
                biome => biome.Decorations
                    .OrderBy(
                        decoration => decoration.Block,
                        StringComparer.Ordinal)
                    .Select(
                        decoration => new DecorationRule(
                            blocks.GetId(decoration.Block),
                            decoration.Chance,
                            decoration.SurfaceBlocks
                                .Select(blocks.GetId)
                                .ToHashSet(),
                            GenerationDomain.Named(
                                $"worldgen/decorator/{biome.Id}/{decoration.Block}/v1"),
                            decoration.Cluster,
                            decoration.Conditions,
                            biome.SurfaceLayout is not null,
                            GenerationDomain.Named(
                                $"worldgen/decorator-cluster/{biome.Id}/{decoration.Block}/v1"),
                            decoration.HabitatWeights))
                    .ToArray(),
                StringComparer.Ordinal);
        foreach (var biome in definitions)
            foreach (var decoration in biome.Decorations)
                _habitats.Validate(biome.Id, decoration.HabitatWeights);
    }

    public bool HasVerticalDecorations => _verticalBiomes.Count > 0;

    public bool HasVerticalDecorationsFor(string biomeId) =>
        _verticalBiomes.Contains(biomeId);

    public BlockRuntimeId BlockAt(
        BiomeSample sample,
        BlockRuntimeId surfaceBlock,
        int worldX,
        int worldZ,
        SurfacePlacementContext? suppliedPlacement = null,
        int? verticalY = null)
    {
        SurfacePlacementContext? placement = suppliedPlacement;
        var slopeSampled = suppliedPlacement.HasValue;
        foreach (var influence in sample.Influences)
        {
            double? sampledHabitat = null;
            foreach (var rule in _rules[influence.BiomeId])
            {
                if (!rule.SurfaceBlocks.Contains(surfaceBlock))
                {
                    continue;
                }

                if (rule.Conditions is { } conditions)
                {
                    if (placement is null ||
                        (conditions.RequiresSlope && !slopeSampled))
                    {
                        if (_terrain is null)
                        {
                            throw new InvalidOperationException(
                                "Conditional ground decorators require a terrain field.");
                        }

                        placement = SurfacePlacementContext.Sample(
                            _terrain, worldX, worldZ,
                            conditions.RequiresSlope,
                            rule.UsesBaseSurface);
                        slopeSampled = conditions.RequiresSlope;
                    }

                    if (!conditions.Allows(placement.Value))
                    {
                        continue;
                    }
                }

                double effectiveChance = rule.Chance * influence.Weight;
                if (rule.HabitatWeights is { } weights)
                {
                    sampledHabitat ??= _habitats.Sample(
                        influence.BiomeId, worldX, worldZ);
                    effectiveChance *= _habitats.Weight(
                        influence.BiomeId, weights, sampledHabitat.Value);
                    if (effectiveChance <= 0d)
                        continue;
                }
                if (rule.Cluster is { } cluster)
                {
                    var noise = WorldGenerationNoise.FractalNoise2D(
                        _seed,
                        rule.ClusterDomain,
                        worldX,
                        worldZ,
                        1d / cluster.Scale,
                        octaves: cluster.Octaves);
                    var distribution = cluster.TransitionWidth <= 0f
                        ? (noise >= cluster.Threshold ? 1d : 0d)
                        : WorldGenerationEntropy.SmoothStep(
                            Math.Clamp(
                                (noise - cluster.Threshold) /
                                cluster.TransitionWidth,
                                0d,
                                1d));
                    effectiveChance *= distribution;
                }

                var roll = WorldGenerationEntropy.Unit(
                    verticalY is { } y
                        ? WorldGenerationEntropy.Sample3D(
                            _seed, rule.Domain, worldX, y, worldZ)
                        : WorldGenerationEntropy.Sample2D(
                            _seed, rule.Domain, worldX, worldZ));
                if (roll < effectiveChance)
                {
                    return rule.Block;
                }
            }
        }

        return BlockRuntimeId.Air;
    }

    private sealed record DecorationRule(
        BlockRuntimeId Block,
        float Chance,
        HashSet<BlockRuntimeId> SurfaceBlocks,
        GenerationDomain Domain,
        BiomeDecorationClusterDefinition? Cluster,
        SurfacePlacementConditions? Conditions,
        bool UsesBaseSurface,
        GenerationDomain ClusterDomain,
        SurfaceHabitatWeights? HabitatWeights);
}
