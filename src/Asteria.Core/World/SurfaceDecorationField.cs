namespace Asteria.Core.World;

/// <summary>
/// Resolves authored ground decorators from a sampled surface biome and
/// its actual supporting surface material; never mutates a world.
/// </summary>
public sealed class SurfaceDecorationField
{
    private readonly ulong _seed;
    private readonly SurfaceTerrainField? _terrain;
    private readonly IReadOnlyDictionary<string, DecorationRule[]> _rules;

    public SurfaceDecorationField(
        ulong seed,
        IEnumerable<BiomeDefinition> biomes,
        BlockRegistry blocks,
        SurfaceTerrainField? terrain = null)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(blocks);
        _seed = seed;
        _terrain = terrain;
        _rules = biomes
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
                            GenerationDomain.Named(
                                $"worldgen/decorator-cluster/{biome.Id}/{decoration.Block}/v1")))
                    .ToArray(),
                StringComparer.Ordinal);
    }

    public BlockRuntimeId BlockAt(
        BiomeSample sample,
        BlockRuntimeId surfaceBlock,
        int worldX,
        int worldZ,
        SurfacePlacementContext? suppliedPlacement = null)
    {
        SurfacePlacementContext? placement = suppliedPlacement;
        var slopeSampled = suppliedPlacement.HasValue;
        foreach (var influence in sample.Influences)
        {
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
                            conditions.RequiresSlope);
                        slopeSampled = conditions.RequiresSlope;
                    }

                    if (!conditions.Allows(placement.Value))
                    {
                        continue;
                    }
                }

                var effectiveChance = rule.Chance * influence.Weight;
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
                    WorldGenerationEntropy.Sample2D(
                        _seed,
                        rule.Domain,
                        worldX,
                        worldZ));
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
        GenerationDomain ClusterDomain);
}
