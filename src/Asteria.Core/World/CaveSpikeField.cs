namespace Asteria.Core.World;

/// <summary>
/// Immutable spike candidate queries. The chunk materializer alone places
/// segments; anchors are world-space and independent of chunk order.
/// </summary>
public sealed class CaveSpikeField
{
    private readonly ulong _seed;
    private readonly SurfaceTerrainField _terrain;
    private readonly IReadOnlyDictionary<string, SpikeRule[]> _rules;

    public CaveSpikeField(
        ulong seed,
        IEnumerable<BiomeDefinition> undergroundBiomes,
        BlockRegistry blocks,
        SurfaceTerrainField terrain)
    {
        ArgumentNullException.ThrowIfNull(undergroundBiomes);
        ArgumentNullException.ThrowIfNull(blocks);
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _seed = seed;
        _rules = undergroundBiomes
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(
                biome => biome.Id,
                biome => biome.CaveSpikes
                    .OrderBy(spike => spike.Block, StringComparer.Ordinal)
                    .Select((spike, index) =>
                    {
                        var blockId = blocks.GetId(spike.Block);
                        if (blocks.GetDefinition(blockId).Shape.Kind != BlockShapeKind.Spike)
                            throw new ArgumentException(
                                $"Cave spike {spike.Block} must use spike geometry.");
                        var domain =
                            $"worldgen/cave-spike/{biome.Id}/{spike.Block}/{index}/v1";
                        return new SpikeRule(
                            blockId, spike,
                            GenerationDomain.Named(domain + "/up"),
                            GenerationDomain.Named(domain + "/down"),
                            GenerationDomain.Named(domain + "/height"));
                    }).ToArray(),
                StringComparer.Ordinal);
        MaximumHeight = _rules.Values
            .SelectMany(rules => rules)
            .Select(rule => rule.Definition.MaxHeight)
            .DefaultIfEmpty(0).Max();
    }

    public bool HasRules => MaximumHeight > 0;
    public int MaximumHeight { get; }
    public bool HasRulesFor(string biomeId) =>
        _rules.TryGetValue(biomeId, out var rules) && rules.Length > 0;

    public bool TrySample(
        string undergroundBiome,
        BiomeSample surfaceBiome,
        BiomeSample? volumeBiome,
        int baseY,
        int x,
        int y,
        int z,
        bool down,
        out CaveSpikePlacement placement)
    {
        placement = default;
        if (!_rules.TryGetValue(undergroundBiome, out var rules) ||
            y <= 0 || y >= baseY)
            return false;

        foreach (var rule in rules)
        {
            var direction = down ? CaveSpikeDirection.Down : CaveSpikeDirection.Up;
            if (!rule.Definition.Directions.Contains(direction))
                continue;
            if (rule.Definition.SurfaceBiomes.Count > 0 &&
                !rule.Definition.SurfaceBiomes.Contains(
                    surfaceBiome.Primary, StringComparer.Ordinal))
                continue;
            var domain = down ? rule.DownDomain : rule.UpDomain;
            if (WorldGenerationEntropy.Unit(
                    WorldGenerationEntropy.Sample3D(_seed, domain, x, y, z)) >=
                rule.Definition.Chance)
                continue;

            bool IsVoid(int probeY) =>
                probeY > 0 && probeY < baseY &&
                _terrain.DensityAt(
                    surfaceBiome, baseY, x, probeY, z, volumeBiome) < 0d;
            var supportY = y + (down ? 1 : -1);
            if (!IsVoid(y) || IsVoid(supportY))
                continue;

            var clear = 0;
            var probeLength = Math.Max(
                rule.Definition.MinClearance, rule.Definition.MaxHeight);
            for (var i = 0; i < probeLength; i++)
            {
                if (!IsVoid(y + (down ? -i : i)))
                    break;
                clear++;
            }

            if (clear < rule.Definition.MinClearance)
                continue;

            var variation = WorldGenerationEntropy.Unit(
                WorldGenerationEntropy.Sample3D(
                    _seed, rule.HeightDomain, x, y, z));
            var authoredHeight = rule.Definition.MinHeight +
                (int)(variation *
                    (rule.Definition.MaxHeight - rule.Definition.MinHeight + 1));
            var height = Math.Min(clear, authoredHeight);
            if (height < rule.Definition.MinHeight)
                continue;

            placement = new CaveSpikePlacement(rule.Block, height);
            return true;
        }

        return false;
    }

    private sealed record SpikeRule(
        BlockRuntimeId Block,
        BiomeCaveSpikeDefinition Definition,
        GenerationDomain UpDomain,
        GenerationDomain DownDomain,
        GenerationDomain HeightDomain);
}

public readonly record struct CaveSpikePlacement(
    BlockRuntimeId Block,
    int Height);
