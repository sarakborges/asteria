using System.Numerics;

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

    // Cave-spike spacing is purely horizontal: the same (X,Z,radius) is
    // reconsidered for each anchor Y, both directions, and adjacent vertical
    // chunks. A bounded direct-mapped table avoids allocating an LRU node,
    // Lazy<T> and capturing factory for every newly encountered column.
    // Collisions only evict a cached answer and therefore cause recomputation.
    private const int SpacingCacheCapacity = 32768;
    private const int SpacingCacheMask = SpacingCacheCapacity - 1;
    private const int SpacingCacheLockCount = 256;
    private readonly SpacingPriorityEntry[] _spacingPriorities =
        new SpacingPriorityEntry[SpacingCacheCapacity];
    private readonly object[] _spacingPriorityLocks = CreateSpacingPriorityLocks();

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
        out CaveSpikePlacement placement) =>
        TrySampleCore(
            undergroundBiome, surfaceBiome, volumeBiome, baseY,
            x, y, z, down, densityVolume: null, densityOriginY: 0,
            localX: 0, localZ: 0, solidColumnMask: 0,
            anchorKnownVoid: false, out placement);

    internal bool TrySample(
        string undergroundBiome,
        BiomeSample surfaceBiome,
        BiomeSample? volumeBiome,
        int baseY,
        int x,
        int y,
        int z,
        bool down,
        TerrainDensityVolume densityVolume,
        int densityOriginY,
        int localX,
        int localZ,
        ushort solidColumnMask,
        bool anchorKnownVoid,
        out CaveSpikePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(densityVolume);
        return TrySampleCore(
            undergroundBiome, surfaceBiome, volumeBiome, baseY,
            x, y, z, down, densityVolume, densityOriginY,
            localX, localZ, solidColumnMask, anchorKnownVoid, out placement);
    }

    private bool TrySampleCore(
        string undergroundBiome,
        BiomeSample surfaceBiome,
        BiomeSample? volumeBiome,
        int baseY,
        int x,
        int y,
        int z,
        bool down,
        TerrainDensityVolume? densityVolume,
        int densityOriginY,
        int localX,
        int localZ,
        ushort solidColumnMask,
        bool anchorKnownVoid,
        out CaveSpikePlacement placement)
    {
        placement = default;
        if (!_rules.TryGetValue(undergroundBiome, out var rules) ||
            y <= 0 || y >= baseY)
            return false;

        var supportY = y + (down ? 1 : -1);
        var anchorValidated = anchorKnownVoid;
        var supportValidated = false;

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

            // Resolve occupancy only after confirming this rule supports the
            // requested direction, but before any expensive stochastic work.
            // Several matching rules share the same anchor and support.
            if (!anchorValidated)
            {
                if (!IsVoidAt(
                        densityVolume, densityOriginY, localX, localZ,
                        surfaceBiome, volumeBiome, baseY, x, z, y))
                    return false;
                anchorValidated = true;
            }
            if (!supportValidated)
            {
                if (!IsSolidSupportAt(
                        densityVolume, densityOriginY, localX, localZ,
                        solidColumnMask, surfaceBiome, volumeBiome,
                        baseY, x, z, supportY))
                    return false;
                supportValidated = true;
            }

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
            var probeLength = Math.Max(
                definition.MinClearance, definition.MaxHeight * 2 + 1);
            var clear = CountClearance(
                densityVolume, densityOriginY, localX, localZ,
                solidColumnMask, surfaceBiome, volumeBiome, baseY,
                x, y, z, down, probeLength);

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

    internal static int CountContiguousVoidDepthInMask(
        ushort solidColumnMask,
        int height,
        int localY,
        bool down)
    {
        if (height is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(height));
        if ((uint)localY >= height)
            throw new ArgumentOutOfRangeException(nameof(localY));

        var validMask = (uint)solidColumnMask & ((1u << height) - 1u);
        if (down)
        {
            var throughAnchor = validMask & ((1u << (localY + 1)) - 1u);
            return throughAnchor == 0u
                ? localY + 1
                : localY - BitOperations.Log2(throughAnchor);
        }

        var available = height - localY;
        var fromAnchor = (validMask >> localY) & ((1u << available) - 1u);
        return BitOperations.TrailingZeroCount(
            fromAnchor | (1u << available));
    }

    private bool IsSolidSupportAt(
        TerrainDensityVolume? densityVolume,
        int densityOriginY,
        int localX,
        int localZ,
        ushort solidColumnMask,
        BiomeSample surfaceBiome,
        BiomeSample? volumeBiome,
        int baseY,
        int x,
        int z,
        int supportY)
    {
        // The world floor and biome surface are authored solid boundaries,
        // matching IsVoidAt even when the stored mask ends before them.
        if (supportY <= 0 || supportY >= baseY)
            return true;

        var localSupportY = supportY - densityOriginY;
        if (densityVolume is not null &&
            (uint)localSupportY < densityVolume.Height)
        {
            return (solidColumnMask & (1u << localSupportY)) != 0;
        }

        // Chunk-edge support belongs to the neighboring vertical chunk and
        // must keep the world-space density fallback.
        return !IsVoidAt(
            densityVolume, densityOriginY, localX, localZ,
            surfaceBiome, volumeBiome, baseY, x, z, supportY);
    }

    private int CountClearance(
        TerrainDensityVolume? densityVolume,
        int densityOriginY,
        int localX,
        int localZ,
        ushort solidColumnMask,
        BiomeSample surfaceBiome,
        BiomeSample? volumeBiome,
        int baseY,
        int x,
        int y,
        int z,
        bool down,
        int probeLength)
    {
        // IsVoidAt treats the world floor and the biome surface as support,
        // even if the stored density mask contains empty bits beyond them.
        probeLength = Math.Min(probeLength, down ? y : baseY - y);
        var localY = y - densityOriginY;
        if (densityVolume is not null &&
            (uint)localY < densityVolume.Height)
        {
            // The density volume already stores a 16-bit occupancy column.
            // Count the contiguous empty run with bit operations, then query
            // world density only if the run reaches this chunk's vertical edge.
            var availableInside = down
                ? localY + 1
                : densityVolume.Height - localY;
            var clearInside = CountContiguousVoidDepthInMask(
                solidColumnMask, densityVolume.Height, localY, down);
            var clear = Math.Min(probeLength, clearInside);
            if (clear < probeLength && clear == availableInside)
            {
                for (; clear < probeLength; clear++)
                {
                    var probeY = y + (down ? -clear : clear);
                    if (!IsVoidAt(
                            densityVolume, densityOriginY, localX, localZ,
                            surfaceBiome, volumeBiome, baseY, x, z, probeY))
                        break;
                }
            }

            return clear;
        }

        var fallbackClear = 0;
        for (; fallbackClear < probeLength; fallbackClear++)
        {
            var probeY = y + (down ? -fallbackClear : fallbackClear);
            if (!IsVoidAt(
                    densityVolume, densityOriginY, localX, localZ,
                    surfaceBiome, volumeBiome, baseY, x, z, probeY))
                break;
        }

        return fallbackClear;
    }

    private bool IsVoidAt(
        TerrainDensityVolume? densityVolume,
        int densityOriginY,
        int localX,
        int localZ,
        BiomeSample surfaceBiome,
        BiomeSample? volumeBiome,
        int baseY,
        int x,
        int z,
        int probeY)
    {
        if (probeY <= 0 || probeY >= baseY)
            return false;

        var localProbeY = probeY - densityOriginY;
        if (densityVolume is not null &&
            (uint)localProbeY < densityVolume.Height)
        {
            // The caller has already bounded the X/Z column to this chunk.
            // Avoid repeating the public coordinate validation for each
            // clearance voxel in a candidate spike.
            return !densityVolume.IsSolidAtUnchecked(
                localX, localProbeY, localZ);
        }

        return _terrain.DensityAt(
            surfaceBiome, baseY, x, probeY, z, volumeBiome) < 0d;
    }

    private bool HasSpacingPriority(int x, int z, int radius)
    {
        if (radius == 0)
            return true;

        var index = SpacingCacheIndex(x, z, radius);
        var gate = _spacingPriorityLocks[
            index & (SpacingCacheLockCount - 1)];

        lock (gate)
        {
            ref var entry = ref _spacingPriorities[index];
            if (entry.IsValid &&
                entry.X == x &&
                entry.Z == z &&
                entry.Radius == radius)
            {
                return entry.Value;
            }
        }

        // Do not serialize independent cache misses behind a stripe lock.
        // Competing misses may repeat the same deterministic calculation;
        // publishing either answer is equivalent and only affects eviction.
        var value = HasSpacingPriorityUncached(x, z, radius);
        lock (gate)
        {
            ref var entry = ref _spacingPriorities[index];
            if (entry.IsValid &&
                entry.X == x &&
                entry.Z == z &&
                entry.Radius == radius)
            {
                return entry.Value;
            }

            entry = new SpacingPriorityEntry(x, z, radius, value);
        }

        return value;
    }

    private static object[] CreateSpacingPriorityLocks()
    {
        var locks = new object[SpacingCacheLockCount];
        for (var index = 0; index < locks.Length; index++)
            locks[index] = new object();
        return locks;
    }

    private static int SpacingCacheIndex(int x, int z, int radius)
    {
        unchecked
        {
            var hash = (uint)x * 0x9E3779B9u;
            hash ^= (uint)z * 0x85EBCA6Bu;
            hash ^= (uint)radius * 0xC2B2AE35u;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return (int)(hash & SpacingCacheMask);
        }
    }

    private struct SpacingPriorityEntry(
        int x,
        int z,
        int radius,
        bool value)
    {
        public int X = x;
        public int Z = z;
        public int Radius = radius;
        public bool Value = value;
        public bool IsValid = true;
    }

    private bool HasSpacingPriorityUncached(int x, int z, int radius)
    {
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
