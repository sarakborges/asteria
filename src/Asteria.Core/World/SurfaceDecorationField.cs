namespace Asteria.Core.World;

/// <summary>
/// Resolves authored ground decorators from a sampled surface biome and
/// its actual supporting surface material; never mutates a world.
/// </summary>
public sealed class SurfaceDecorationField
{
    private readonly ulong _seed;
    private readonly IReadOnlyDictionary<string, DecorationRule[]> _rules;

    public SurfaceDecorationField(
        ulong seed,
        IEnumerable<BiomeDefinition> biomes,
        BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(blocks);
        _seed = seed;
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
                            GenerationDomain.Named(
                                $"worldgen/decorator-cluster/{biome.Id}/{decoration.Block}/v1")))
                    .ToArray(),
                StringComparer.Ordinal);
    }

    public BlockRuntimeId BlockAt(
        BiomeSample sample,
        BlockRuntimeId surfaceBlock,
        int worldX,
        int worldZ)
    {
        foreach (var influence in sample.Influences)
        {
            foreach (var rule in _rules[influence.BiomeId])
            {
                if (!rule.SurfaceBlocks.Contains(surfaceBlock))
                {
                    continue;
                }

                var effectiveChance = rule.Chance * influence.Weight;
                var roll = WorldGenerationEntropy.Unit(
                    WorldGenerationEntropy.Sample2D(
                        _seed,
                        rule.Domain,
                        worldX,
                        worldZ));

                if (roll >= effectiveChance)
                {
                    continue;
                }

                if (rule.Cluster is { } cluster &&
                    WorldGenerationNoise.FractalNoise2D(
                        _seed,
                        rule.ClusterDomain,
                        worldX,
                        worldZ,
                        1d / cluster.Scale,
                        octaves: 3) < cluster.Threshold)
                {
                    continue;
                }

                return rule.Block;
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
        GenerationDomain ClusterDomain);
}
