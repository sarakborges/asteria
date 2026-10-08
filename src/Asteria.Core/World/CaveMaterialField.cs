namespace Asteria.Core.World;

/// <summary>
/// Immutable world-coordinate material selection for exposed cave faces.
/// Authored array order is the explicit tie-breaker for overlapping patches.
/// Only the chunk materializer may apply the resulting voxel replacement.
/// </summary>
public sealed class CaveMaterialField
{
    private readonly ulong _seed;
    private readonly IReadOnlyDictionary<string, Rule[]> _rules;

    public CaveMaterialField(
        ulong seed,
        IEnumerable<BiomeDefinition> undergroundBiomes,
        BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(undergroundBiomes);
        ArgumentNullException.ThrowIfNull(blocks);
        _seed = seed;
        _rules = undergroundBiomes
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(biome => biome.Id,
                biome => biome.CaveMaterials.Select((material, index) =>
                {
                    var domain = $"worldgen/cave-material/{biome.Id}/{index}/v1";
                    return new Rule(
                        blocks.GetId(material.Block),
                        material.ReplaceBlocks.Select(blocks.GetId).ToHashSet(),
                        material.Faces.ToHashSet(),
                        material.Chance,
                        material.Cluster,
                        GenerationDomain.Named(domain + "/chance"),
                        GenerationDomain.Named(domain + "/patch"));
                }).ToArray(),
                StringComparer.Ordinal);
        HasRules = _rules.Values.Any(rules => rules.Length != 0);
    }

    public bool HasRules { get; }

    public bool HasRulesFor(string biomeId) =>
        _rules.TryGetValue(biomeId, out var rules) && rules.Length > 0;

    public BlockRuntimeId Select(
        string biomeId, BlockRuntimeId existing,
        CaveSurfaceFace face, int x, int y, int z)
    {
        if (!_rules.TryGetValue(biomeId, out var rules))
            return existing;

        foreach (var rule in rules)
        {
            if (!rule.Replacements.Contains(existing) ||
                !rule.Faces.Contains(face))
                continue;

            var roll = WorldGenerationEntropy.Unit(
                WorldGenerationEntropy.Sample3D(
                    _seed, rule.ChanceDomain, x, y, z));
            if (roll >= rule.Chance)
                continue;

            if (rule.Cluster is { } cluster)
            {
                var noise = WorldGenerationEntropy.ValueNoise3D(
                    _seed, rule.PatchDomain, x, y, z,
                    (uint)cluster.HorizontalScale,
                    (uint)cluster.VerticalScale);
                var coverage = cluster.TransitionWidth <= 0f
                    ? (noise >= cluster.Threshold ? 1d : 0d)
                    : WorldGenerationEntropy.SmoothStep(
                        (noise - cluster.Threshold) / cluster.TransitionWidth);
                if (roll >= rule.Chance * coverage)
                    continue;
            }

            return rule.Block;
        }

        return existing;
    }

    private sealed record Rule(
        BlockRuntimeId Block,
        HashSet<BlockRuntimeId> Replacements,
        HashSet<CaveSurfaceFace> Faces,
        float Chance,
        CaveSpikeClusterDefinition? Cluster,
        GenerationDomain ChanceDomain,
        GenerationDomain PatchDomain);
}
