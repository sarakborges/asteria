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
    private static readonly GenerationDomain SpacingDomain =
        GenerationDomain.Named("worldgen/cave-spike/horizontal-spacing/v1");

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
                            GenerationDomain.Named(domain + "/height"),
                            GenerationDomain.Named(domain + "/cluster"));
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

        // A specific surface material takes precedence over the generic
        // stone fallback. One column cannot spawn competing materials.
        SpikeRule? primaryRule = null;
        foreach (var candidate in rules)
        {
            if (candidate.Definition.SurfaceBiomes.Contains(
                    surfaceBiome.Primary, StringComparer.Ordinal))
            {
                primaryRule = candidate;
                break;
            }
        }

        foreach (var rule in rules)
        {
            if (primaryRule is not null && !ReferenceEquals(rule, primaryRule))
                continue;
            if (primaryRule is null && rule.Definition.SurfaceBiomes.Count != 0)
                continue;

            var definition = rule.Definition;
            var direction = down ? CaveSpikeDirection.Down : CaveSpikeDirection.Up;
            if (!definition.Directions.Contains(direction))
                continue;

            var domain = down ? rule.DownDomain : rule.UpDomain;
            var roll = WorldGenerationEntropy.Unit(
                WorldGenerationEntropy.Sample3D(_seed, domain, x, y, z));
            if (roll >= definition.Chance)
                continue;

            // Stable 2D priorities prevent near-identical neighboring
            // columns from becoming a uniform carpet of spikes. Sampling
            // the same horizontal priority at every Y keeps the spacing
            // invariant independent of cave shelves and chunk order.
            if (!HasSpacingPriority(x, z, definition.MinSpacing))
                continue;

            bool IsVoid(int probeY) =>
                probeY > 0 && probeY < baseY &&
                _terrain.DensityAt(
                    surfaceBiome, baseY, x, probeY, z, volumeBiome) < 0d;
            var supportY = y + (down ? 1 : -1);
            if (!IsVoid(y) || IsVoid(supportY))
                continue;

            if (definition.Cluster is { } cluster)
            {
                var noise = WorldGenerationEntropy.ValueNoise3D(
                    _seed, rule.ClusterDomain, x, y, z,
                    (uint)cluster.HorizontalScale,
                    (uint)cluster.VerticalScale);
                var coverage = cluster.TransitionWidth == 0f
                    ? (noise >= cluster.Threshold ? 1d : 0d)
                    : WorldGenerationEntropy.SmoothStep(
                        (noise - cluster.Threshold) / cluster.TransitionWidth);
                if (roll >= definition.Chance * coverage)
                    continue;
            }

            // Probe far enough to know whether opposite-facing spikes
            // could meet in a narrow gallery. Larger chambers still
            // permit the authored maximum height.
            var clear = 0;
            var probeLength = Math.Max(
                definition.MinClearance, definition.MaxHeight * 2 + 1);
            for (var i = 0; i < probeLength; i++)
            {
                if (!IsVoid(y + (down ? -i : i)))
                    break;
                clear++;
            }

            if (clear < definition.MinClearance)
                continue;

            var variation = WorldGenerationEntropy.Unit(
                WorldGenerationEntropy.Sample3D(
                    _seed, rule.HeightDomain, x, y, z));
            var authoredHeight = definition.MinHeight +
                Math.Min(definition.MaxHeight - definition.MinHeight,
                    (int)(variation *
                        (definition.MaxHeight - definition.MinHeight + 1)));
            var height = Math.Min(
                authoredHeight, (clear - 1) / 2);
            if (height < definition.MinHeight)
                continue;

            placement = new CaveSpikePlacement(rule.Block, height);
            return true;
        }

        return false;
    }

    private bool HasSpacingPriority(int x, int z, int radius)
    {
        if (radius == 0)
            return true;

        var priority = WorldGenerationEntropy.Sample2D(
            _seed, SpacingDomain, x, z);
        for (var offsetZ = -radius; offsetZ <= radius; offsetZ++)
        for (var offsetX = -radius; offsetX <= radius; offsetX++)
        {
            if (offsetX == 0 && offsetZ == 0)
                continue;

            var rival = WorldGenerationEntropy.Sample2D(
                _seed, SpacingDomain,
                unchecked(x + offsetX), unchecked(z + offsetZ));
            if (rival < priority)
                return false;
            if (rival == priority &&
                (offsetZ < 0 || (offsetZ == 0 && offsetX < 0)))
                return false;
        }

        return true;
    }

    private sealed record SpikeRule(
        BlockRuntimeId Block,
        BiomeCaveSpikeDefinition Definition,
        GenerationDomain UpDomain,
        GenerationDomain DownDomain,
        GenerationDomain HeightDomain,
        GenerationDomain ClusterDomain);
}

public readonly record struct CaveSpikePlacement(
    BlockRuntimeId Block,
    int Height);
