namespace Asteria.Core.World;

public readonly record struct BiomeInfluence(
    string BiomeId,
    float Weight,
    float TerrainStrength = 1f);

public sealed class BiomeSample
{
    public BiomeSample(
        string primary,
        IReadOnlyList<BiomeInfluence> influences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            primary);
        ArgumentNullException.ThrowIfNull(
            influences);

        if (influences.Count == 0)
        {
            throw new ArgumentException(
                "Biome sample must contain at least one influence.",
                nameof(influences));
        }

        Primary = primary;
        Influences = influences;
    }

    public string Primary { get; }

    public IReadOnlyList<BiomeInfluence> Influences { get; }

    public float PrimaryWeight =>
        PrimaryInfluence.Weight;

    public float PrimaryTerrainStrength =>
        PrimaryInfluence.TerrainStrength;

    private BiomeInfluence PrimaryInfluence =>
        Influences
            .First(influence =>
                string.Equals(
                    influence.BiomeId,
                    Primary,
                    StringComparison.Ordinal));
}

public sealed record SurfaceBiomeSearchResult(
    int X,
    int Z,
    BiomeSample Sample);

public sealed class BiomeSampleGrid
{
    private readonly BiomeSample[] _samples;

    internal BiomeSampleGrid(
        int originX,
        int originZ,
        int width,
        int depth,
        BiomeSample[] samples)
    {
        OriginX = originX;
        OriginZ = originZ;
        Width = width;
        Depth = depth;
        _samples = samples;
    }

    public int OriginX { get; }

    public int OriginZ { get; }

    public int Width { get; }

    public int Depth { get; }

    public BiomeSample this[
        int localX,
        int localZ]
    {
        get
        {
            if ((uint)localX >=
                    (uint)Width ||
                (uint)localZ >=
                    (uint)Depth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localX));
            }

            return _samples[
                localZ * Width +
                localX];
        }
    }
}

public sealed class BiomeField
{
    private const int MinimumSeedSpacing = 64;
    private const int MaximumSeedSpacing = 256;
    private const int CandidateRadiusBuckets = 2;
    private const int CompatibilityRadiusBuckets =
        CandidateRadiusBuckets * 2;
    private const int CompatibilityClassPeriod =
        CompatibilityRadiusBuckets * 2 + 1;
    private readonly BiomeBlendingDefinition _blending;

    private readonly ulong _seed;
    private readonly BiomeRule[] _rules;
    private readonly BoundedMemoCache<SeedBucket, SeedAssignment>
        _assignments;
    private readonly int _seedSpacing;
    private readonly GenerationDomain _seedPickDomain =
        GenerationDomain.Named(
            "biome-layout/seed-pick/v3");
    private readonly GenerationDomain _jitterXDomain =
        GenerationDomain.Named(
            "biome-layout/seed-jitter-x/v3");
    private readonly GenerationDomain _jitterZDomain =
        GenerationDomain.Named(
            "biome-layout/seed-jitter-z/v3");
    private readonly GenerationDomain _shapeADomain =
        GenerationDomain.Named(
            "biome-layout/seed-shape-a/v3");
    private readonly GenerationDomain _shapeBDomain =
        GenerationDomain.Named(
            "biome-layout/seed-shape-b/v3");
    private readonly GenerationDomain _seedBiasDomain =
        GenerationDomain.Named(
            "biome-layout/seed-bias/v3");
    private readonly GenerationDomain _spawnPickDomain =
        GenerationDomain.Named(
            "biome-layout/spawn-pick/v1");
    private readonly GenerationDomain _warpCoarseXDomain =
        GenerationDomain.Named(
            "biome-layout/warp-coarse-x/v3");
    private readonly GenerationDomain _warpCoarseZDomain =
        GenerationDomain.Named(
            "biome-layout/warp-coarse-z/v3");
    private readonly GenerationDomain _warpFineXDomain =
        GenerationDomain.Named(
            "biome-layout/warp-fine-x/v3");
    private readonly GenerationDomain _warpFineZDomain =
        GenerationDomain.Named(
            "biome-layout/warp-fine-z/v3");

    public BiomeField(
        ulong seed,
        DimensionDefinition dimension,
        BiomeRegistry biomes,
        int assignmentCacheCapacity = 4096)
        : this(
            seed,
            dimension?.SurfaceBiomes ??
                throw new ArgumentNullException(nameof(dimension)),
            biomes,
            assignmentCacheCapacity,
            dimension.BiomeBlending)
    {
    }

    public BiomeField(
        ulong seed,
        IEnumerable<string> biomeIds,
        BiomeRegistry biomes,
        int assignmentCacheCapacity = 4096,
        BiomeBlendingDefinition? blending = null)
        : this(
            seed,
            SurfaceRules(
                biomeIds,
                biomes),
            assignmentCacheCapacity,
            blending)
    {
    }

    internal BiomeField(
        ulong seed,
        IEnumerable<BiomeFieldRuleSource> ruleSources,
        int assignmentCacheCapacity = 4096,
        BiomeBlendingDefinition? blending = null)
    {
        ArgumentNullException.ThrowIfNull(
            ruleSources);
        _blending = blending ?? BiomeBlendingDefinition.Default;

        _seed = seed;
        _assignments =
            new BoundedMemoCache<SeedBucket, SeedAssignment>(
                assignmentCacheCapacity);
        _rules =
            ruleSources
                .OrderBy(
                    source =>
                        source.Id,
                    StringComparer.Ordinal)
                .Select(
                    (source, index) =>
                        BiomeRule.Create(
                            source,
                            index))
                .ToArray();

        if (_rules.Length == 0)
        {
            throw new ArgumentException(
                "Biome field requires at least one active biome.",
                nameof(ruleSources));
        }

        _seedSpacing =
            Math.Clamp(
                checked((int)_rules
                    .Min(rule =>
                        rule.RegionMin)
                    .DivCeiling(3)),
                MinimumSeedSpacing,
                MaximumSeedSpacing);
    }

    private static IEnumerable<BiomeFieldRuleSource>
        SurfaceRules(
            IEnumerable<string> biomeIds,
            BiomeRegistry biomes)
    {
        ArgumentNullException.ThrowIfNull(
            biomeIds);
        ArgumentNullException.ThrowIfNull(
            biomes);

        return biomeIds
            .Select(
                biomeId =>
                {
                    var definition =
                        biomes.Get(
                            biomeId);
                    var layout =
                        definition.SurfaceLayout ??
                        throw new ArgumentException(
                            $"Biome {biomeId} does not author surfaceLayout.");

                    return new BiomeFieldRuleSource(
                        definition.Id,
                        layout);
                })
            .ToArray();
    }

    public int SeedSpacing =>
        _seedSpacing;

    public string? SelectSpawnBiome(
        string? excludedBiomeId = null)
    {
        var total = 0UL;

        foreach (var rule in _rules)
        {
            if (rule.SpawnWeightUnits == 0 ||
                string.Equals(
                    rule.Id,
                    excludedBiomeId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            total =
                SaturatingAdd(
                    total,
                    rule.SpawnWeightUnits);
        }

        if (total == 0)
        {
            return null;
        }

        var pick =
            WorldGenerationEntropy.Sample2D(
                _seed,
                _spawnPickDomain,
                0,
                0) %
            total;
        var cursor = 0UL;

        foreach (var rule in _rules)
        {
            if (rule.SpawnWeightUnits == 0 ||
                string.Equals(
                    rule.Id,
                    excludedBiomeId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            cursor =
                SaturatingAdd(
                    cursor,
                    rule.SpawnWeightUnits);
            if (pick < cursor)
            {
                return rule.Id;
            }
        }

        return _rules
            .Last(rule =>
                rule.SpawnWeightUnits > 0 &&
                !string.Equals(
                    rule.Id,
                    excludedBiomeId,
                    StringComparison.Ordinal))
            .Id;
    }

    public BiomeSample Sample(
        int x,
        int z)
    {
        var assignments =
            new Dictionary<
                SeedBucket,
                SeedAssignment>();

        return SampleCached(
            x,
            z,
            assignments);
    }

    public BiomeSampleGrid SampleGrid(
        int originX,
        int originZ,
        int width,
        int depth,
        int step = 1)
    {
        if (width <= 0 ||
            depth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Biome sample grid must be non-empty.");
        }

        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(step),
                "Biome sample grid step must be positive.");
        }

        var sampleCount =
            checked(width * depth);
        var samples =
            new BiomeSample[
                sampleCount];
        var assignments =
            new Dictionary<
                SeedBucket,
                SeedAssignment>();

        for (var z = 0;
             z < depth;
             z++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                samples[
                    z * width +
                    x] =
                    SampleCached(
                        checked(
                            originX +
                            x * step),
                        checked(
                            originZ +
                            z * step),
                        assignments);
            }
        }

        return new BiomeSampleGrid(
            originX,
            originZ,
            width,
            depth,
            samples);
    }

    public SurfaceBiomeSearchResult?
        FindNearestSurfaceBiome(
            string biomeId,
            int originX,
            int originZ,
            int maxDistance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            biomeId);

        if (maxDistance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDistance),
                "Biome search distance must be non-negative.");
        }

        var targetRule =
            Array.FindIndex(
                _rules,
                rule =>
                    string.Equals(
                        rule.Id,
                        biomeId,
                        StringComparison.Ordinal));
        if (targetRule < 0)
        {
            return null;
        }

        var originSample =
            Sample(
                originX,
                originZ);
        if (string.Equals(
                originSample.Primary,
                biomeId,
                StringComparison.Ordinal))
        {
            return new SurfaceBiomeSearchResult(
                originX,
                originZ,
                originSample);
        }

        var warped =
            WarpedPosition(
                originX,
                originZ);
        var originBucket =
            BucketForPosition(
                warped.X,
                warped.Z);
        var bucketRadius =
            checked(
                maxDistance /
                    _seedSpacing +
                (maxDistance %
                     _seedSpacing ==
                 0
                    ? 0
                    : 1) +
                CandidateRadiusBuckets +
                2);
        var maximumDistanceSquared =
            (Int128)maxDistance *
            maxDistance;
        var assignments =
            new Dictionary<
                SeedBucket,
                SeedAssignment>();
        (
            Int128 DistanceSquared,
            SurfaceBiomeSearchResult Result)?
            best = null;

        for (var ring = 0;
             ring <= bucketRadius;
             ring++)
        {
            foreach (var bucket in
                     RingBuckets(
                         originBucket,
                         ring))
            {
                if (SeedAssignmentFor(
                        bucket,
                        assignments)
                    .Rule !=
                    targetRule)
                {
                    continue;
                }

                var seed =
                    FormationSeedFor(
                        bucket,
                        assignments);

                foreach (var point in
                         SearchProbePoints(
                             seed))
                {
                    var dx =
                        (Int128)point.X -
                        originX;
                    var dz =
                        (Int128)point.Z -
                        originZ;
                    var distanceSquared =
                        dx * dx +
                        dz * dz;

                    if (distanceSquared >
                            maximumDistanceSquared ||
                        (best is
                             { } current &&
                         distanceSquared >=
                            current.DistanceSquared))
                    {
                        continue;
                    }

                    var sample =
                        SampleCached(
                            point.X,
                            point.Z,
                            assignments);
                    if (!string.Equals(
                            sample.Primary,
                            biomeId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    best =
                        (
                            distanceSquared,
                            new SurfaceBiomeSearchResult(
                                point.X,
                                point.Z,
                                sample));
                }
            }

            if (best is
                { } found)
            {
                var conservativeRing =
                    Math.Max(
                        0,
                        ring -
                        (CandidateRadiusBuckets +
                         2));
                var conservativeDistance =
                    (Int128)conservativeRing *
                    _seedSpacing;

                if (conservativeDistance *
                        conservativeDistance >
                    found.DistanceSquared)
                {
                    break;
                }
            }
        }

        return best?.Result;
    }

    public bool AreCompatible(
        string leftId,
        string rightId)
    {
        var left =
            Array.FindIndex(
                _rules,
                rule =>
                    string.Equals(
                        rule.Id,
                        leftId,
                        StringComparison.Ordinal));
        var right =
            Array.FindIndex(
                _rules,
                rule =>
                    string.Equals(
                        rule.Id,
                        rightId,
                        StringComparison.Ordinal));

        if (left < 0 ||
            right < 0)
        {
            throw new KeyNotFoundException(
                "Biome compatibility query referenced an inactive biome.");
        }

        return Compatible(
            left,
            right);
    }

    private BiomeSample SampleCached(
        int x,
        int z,
        Dictionary<
            SeedBucket,
            SeedAssignment> assignments)
    {
        var (warpedX, warpedZ) =
            WarpedPosition(
                x,
                z);
        var center =
            BucketForPosition(
                warpedX,
                warpedZ);

        Span<double> bestScores =
            _rules.Length <= 64
                ? stackalloc double[
                    _rules.Length]
                : new double[
                    _rules.Length];
        bestScores.Fill(
            double.NegativeInfinity);
        Span<FormationSeed> bestSeeds =
            _rules.Length <= 64
                ? stackalloc FormationSeed[_rules.Length]
                : new FormationSeed[_rules.Length];
        Span<byte> hasBestSeed =
            _rules.Length <= 64
                ? stackalloc byte[_rules.Length]
                : new byte[_rules.Length];

        for (var dz =
                 -CandidateRadiusBuckets;
             dz <=
                 CandidateRadiusBuckets;
             dz++)
        {
            for (var dx =
                     -CandidateRadiusBuckets;
                 dx <=
                     CandidateRadiusBuckets;
                 dx++)
            {
                if (!TryOffset(
                        center,
                        dx,
                        dz,
                        out var bucket))
                {
                    continue;
                }

                var seed =
                    FormationSeedFor(
                        bucket,
                        assignments);
                var score =
                    SeedScore(
                        seed,
                        warpedX,
                        warpedZ);

                if (score >
                    bestScores[
                        seed.Assignment.Rule])
                {
                    bestScores[
                        seed.Assignment.Rule] =
                        score;
                    bestSeeds[
                        seed.Assignment.Rule] =
                        seed;
                    hasBestSeed[
                        seed.Assignment.Rule] =
                        1;
                }
            }
        }

        var primaryRule =
            -1;
        var primaryScore =
            double.NegativeInfinity;

        for (var rule = 0;
             rule < _rules.Length;
             rule++)
        {
            var score =
                bestScores[rule];
            if (double.IsNegativeInfinity(
                    score))
            {
                continue;
            }

            if (score >
                    primaryScore ||
                (score ==
                     primaryScore &&
                 (primaryRule < 0 ||
                  string.CompareOrdinal(
                      _rules[rule].Id,
                      _rules[primaryRule].Id) <
                  0)))
            {
                primaryRule = rule;
                primaryScore = score;
            }
        }

        if (primaryRule < 0)
        {
            throw new InvalidOperationException(
                "Biome field did not produce a candidate.");
        }

        var weighted =
            new List<(
                int Rule,
                double Weight,
                double TerrainStrength)>(
                _rules.Length);
        var total =
            0d;

        for (var rule = 0;
             rule < _rules.Length;
             rule++)
        {
            var score =
                bestScores[rule];
            if (double.IsNegativeInfinity(
                    score))
            {
                continue;
            }

            var delta =
                primaryScore -
                score;
            if (delta >
                _blending.ScoreBand)
            {
                continue;
            }

            var proximity =
                1d -
                delta /
                _blending.ScoreBand;
            var weight = _blending.WeightAt(proximity);
            if (hasBestSeed[rule] == 0)
            {
                throw new InvalidOperationException(
                    "Biome score is missing its formation seed.");
            }

            weighted.Add(
                (
                    rule,
                    weight,
                    FormationStrength(
                        bestSeeds[rule].Assignment,
                        warpedX,
                        warpedZ)));
            total += weight;
        }

        weighted.Sort(
            (left, right) =>
            {
                var leftPrimary =
                    left.Rule ==
                    primaryRule;
                var rightPrimary =
                    right.Rule ==
                    primaryRule;

                if (leftPrimary !=
                    rightPrimary)
                {
                    return rightPrimary
                        .CompareTo(
                            leftPrimary);
                }

                var byWeight =
                    right.Weight
                        .CompareTo(
                            left.Weight);
                return byWeight != 0
                    ? byWeight
                    : string.CompareOrdinal(
                        _rules[left.Rule].Id,
                        _rules[right.Rule].Id);
            });

        var influences =
            weighted
                .Select(entry =>
                    new BiomeInfluence(
                        _rules[
                            entry.Rule].Id,
                        checked((float)(
                            entry.Weight /
                            total)),
                        checked((float)
                            entry.TerrainStrength)))
                .ToArray();

        return new BiomeSample(
            _rules[
                primaryRule].Id,
            influences);
    }

    private (int X, int Z)[]
        SearchProbePoints(
            FormationSeed seed)
    {
        var centerX =
            ClampWorldAxis(
                checked(
                    (long)Math.Round(
                        seed.CenterX,
                        MidpointRounding.AwayFromZero)));
        var centerZ =
            ClampWorldAxis(
                checked(
                    (long)Math.Round(
                        seed.CenterZ,
                        MidpointRounding.AwayFromZero)));
        var offset =
            _seedSpacing /
            3;
        var offsets =
            new[]
            {
                -offset,
                0,
                offset,
            };
        var points =
            new (int X, int Z)[9];
        var index =
            0;

        foreach (var zOffset in
                 offsets)
        {
            foreach (var xOffset in
                     offsets)
            {
                points[index++] =
                    (
                        ClampWorldAxis(
                            (long)centerX +
                            xOffset),
                        ClampWorldAxis(
                            (long)centerZ +
                            zOffset));
            }
        }

        return points;
    }

    private static IEnumerable<SeedBucket>
        RingBuckets(
            SeedBucket center,
            int ring)
    {
        if (ring < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ring));
        }

        if (ring == 0)
        {
            yield return center;
            yield break;
        }

        for (var dx = -ring;
             dx <= ring;
             dx++)
        {
            if (TryOffset(
                    center,
                    dx,
                    -ring,
                    out var bucket))
            {
                yield return bucket;
            }
        }

        for (var dz = -ring + 1;
             dz <= ring;
             dz++)
        {
            if (TryOffset(
                    center,
                    ring,
                    dz,
                    out var bucket))
            {
                yield return bucket;
            }
        }

        for (var dx = ring - 1;
             dx >= -ring;
             dx--)
        {
            if (TryOffset(
                    center,
                    dx,
                    ring,
                    out var bucket))
            {
                yield return bucket;
            }
        }

        for (var dz = ring - 1;
             dz >= -ring + 1;
             dz--)
        {
            if (TryOffset(
                    center,
                    -ring,
                    dz,
                    out var bucket))
            {
                yield return bucket;
            }
        }
    }

    private static int ClampWorldAxis(
        long value) =>
        value switch
        {
            <= int.MinValue =>
                int.MinValue,
            >= int.MaxValue =>
                int.MaxValue,
            _ =>
                (int)value,
        };

    private FormationSeed FormationSeedFor(
        SeedBucket bucket,
        Dictionary<
            SeedBucket,
            SeedAssignment> cache)
    {
        var assignment =
            SeedAssignmentFor(
                bucket,
                cache);
        var (centerX, centerZ) =
            SeedCenter(
                bucket);

        return new FormationSeed(
            bucket,
            assignment,
            centerX,
            centerZ);
    }

    private SeedAssignment SeedAssignmentFor(
        SeedBucket bucket,
        Dictionary<
            SeedBucket,
            SeedAssignment> cache)
    {
        if (cache.TryGetValue(
                bucket,
                out var cached))
        {
            return cached;
        }

        var assignment = _assignments.GetOrAdd(
            bucket,
            () => ComputeSeedAssignment(bucket, cache));
        cache.Add(bucket, assignment);
        return assignment;
    }

    private SeedAssignment ComputeSeedAssignment(
        SeedBucket bucket,
        Dictionary<
            SeedBucket,
            SeedAssignment> cache)
    {
        var classification =
            bucket.Classification();
        var established =
            new List<SeedAssignment>(
                (CompatibilityRadiusBuckets * 2 + 1) *
                (CompatibilityRadiusBuckets * 2 + 1));

        for (var dz =
                 -CompatibilityRadiusBuckets;
             dz <=
                 CompatibilityRadiusBuckets;
             dz++)
        {
            for (var dx =
                     -CompatibilityRadiusBuckets;
                 dx <=
                     CompatibilityRadiusBuckets;
                 dx++)
            {
                if (dx == 0 &&
                    dz == 0)
                {
                    continue;
                }

                if (!TryOffset(
                        bucket,
                        dx,
                        dz,
                        out var neighbor) ||
                    neighbor.Classification() >=
                    classification)
                {
                    continue;
                }

                established.Add(
                    SeedAssignmentFor(
                        neighbor,
                        cache));
            }
        }

        Span<int> eligible =
            _rules.Length <= 64
                ? stackalloc int[
                    _rules.Length]
                : new int[
                    _rules.Length];
        var eligibleCount =
            0;

        for (var candidate = 0;
             candidate < _rules.Length;
             candidate++)
        {
            var compatible =
                true;

            foreach (var neighbor in
                     established)
            {
                if (!Compatible(
                        candidate,
                        neighbor.Rule))
                {
                    compatible =
                        false;
                    break;
                }
            }

            if (compatible)
            {
                eligible[
                    eligibleCount++] =
                    candidate;
            }
        }

        SeedAssignment assignment;

        if (eligibleCount == 0)
        {
            if (established.Count == 0)
            {
                throw new InvalidOperationException(
                    "Biome compatibility eliminated every seed without an established neighbor.");
            }

            assignment =
                established
                    .OrderByDescending(
                        value =>
                            GrowthAffinity(
                                value,
                                bucket))
                    .ThenBy(
                        value =>
                            value.Root.X)
                    .ThenBy(
                        value =>
                            value.Root.Z)
                    .First();
        }
        else
        {
            Span<ulong> weights =
                eligibleCount <= 64
                    ? stackalloc ulong[
                        eligibleCount]
                    : new ulong[
                        eligibleCount];
            ulong total =
                0;

            for (var index = 0;
                 index < eligibleCount;
                 index++)
            {
                var candidate =
                    eligible[index];
                var continuation =
                    0UL;

                foreach (var neighbor in
                         established)
                {
                    if (neighbor.Rule !=
                        candidate)
                    {
                        continue;
                    }

                    continuation =
                        Math.Max(
                            continuation,
                            GrowthAffinity(
                                neighbor,
                                bucket));
                }

                var multiplier =
                    SaturatingAdd(
                        1UL,
                        SaturatingMultiply(
                            continuation,
                            3UL));
                var weight =
                    SaturatingMultiply(
                        _rules[
                            candidate]
                            .WeightUnits,
                        multiplier);
                weights[index] =
                    weight;
                total =
                    SaturatingAdd(
                        total,
                        weight);
            }

            var pick =
                WorldGenerationEntropy
                    .Sample2D(
                        _seed,
                        _seedPickDomain,
                        bucket.X,
                        bucket.Z) %
                Math.Max(
                    1UL,
                    total);
            ulong cursor =
                0;
            var selected =
                eligible[
                    eligibleCount -
                    1];

            for (var index = 0;
                 index < eligibleCount;
                 index++)
            {
                cursor =
                    SaturatingAdd(
                        cursor,
                        weights[index]);

                if (pick <
                    cursor)
                {
                    selected =
                        eligible[index];
                    break;
                }
            }

            var continued =
                established
                    .Where(value =>
                        value.Rule ==
                        selected)
                    .Select(value =>
                        (
                            Assignment: value,
                            Affinity:
                                GrowthAffinity(
                                    value,
                                    bucket)))
                    .Where(value =>
                        value.Affinity > 0)
                    .OrderByDescending(
                        value =>
                            value.Affinity)
                    .ThenBy(value =>
                        value.Assignment
                            .Root.X)
                    .ThenBy(value =>
                        value.Assignment
                            .Root.Z)
                    .Select(value =>
                        (SeedAssignment?)
                            value.Assignment)
                    .FirstOrDefault();

            assignment =
                continued ??
                new SeedAssignment(
                    selected,
                    bucket,
                    FormationTargetSpan(
                        selected,
                        bucket));
        }

        return assignment;
    }

    private uint FormationTargetSpan(
        int rule,
        SeedBucket root)
    {
        var definition =
            _rules[rule];
        var hash =
            WorldGenerationEntropy
                .Sample2D(
                    _seed,
                    definition.TargetDomain,
                    root.X,
                    root.Z);

        if (definition.RegionMin ==
            definition.RegionMax)
        {
            return definition.RegionMin;
        }

        var range =
            (ulong)definition.RegionMax -
            definition.RegionMin +
            1UL;

        return checked(
            definition.RegionMin +
            (uint)(hash % range));
    }

    private ulong GrowthAffinity(
        SeedAssignment assignment,
        SeedBucket target)
    {
        var (rootX, rootZ) =
            SeedCenter(
                assignment.Root);
        var (targetX, targetZ) =
            SeedCenter(
                target);
        var dx =
            targetX -
            rootX;
        var dz =
            targetZ -
            rootZ;
        var distance =
            Math.Sqrt(
                dx * dx +
                dz * dz);
        var radius =
            assignment.TargetSpan *
            0.5d;

        if (radius <= 0d ||
            distance >
            radius)
        {
            return 0;
        }

        var remaining =
            Math.Clamp(
                1d -
                distance /
                radius,
                0d,
                1d);

        return checked(
            (ulong)Math.Round(
                remaining *
                16d) +
            1UL);
    }

    private bool Compatible(
        int left,
        int right)
    {
        if (left == right)
        {
            return true;
        }

        return !_rules[left]
                    .CannotBorder
                    .Contains(
                        _rules[right].Id) &&
               !_rules[right]
                    .CannotBorder
                    .Contains(
                        _rules[left].Id);
    }

    private (double X, double Z)
        SeedCenter(
            SeedBucket bucket)
    {
        var spacing =
            (double)_seedSpacing;
        var baseX =
            (bucket.X + 0.5d) *
            spacing;
        var baseZ =
            (bucket.Z + 0.5d) *
            spacing;
        var jitterX =
            WorldGenerationEntropy
                .SignedUnit(
                    WorldGenerationEntropy
                        .Sample2D(
                            _seed,
                            _jitterXDomain,
                            bucket.X,
                            bucket.Z)) *
            spacing *
            _blending.JitterFraction;
        var jitterZ =
            WorldGenerationEntropy
                .SignedUnit(
                    WorldGenerationEntropy
                        .Sample2D(
                            _seed,
                            _jitterZDomain,
                            bucket.X,
                            bucket.Z)) *
            spacing *
            _blending.JitterFraction;

        return (
            baseX + jitterX,
            baseZ + jitterZ);
    }

    private double FormationStrength(
        SeedAssignment assignment,
        double x,
        double z)
    {
        var (centerX, centerZ) =
            SeedCenter(
                assignment.Root);
        var dx =
            x -
            centerX;
        var dz =
            z -
            centerZ;
        var distance =
            Math.Sqrt(
                dx * dx +
                dz * dz);
        var angle =
            Math.Atan2(
                dz,
                dx);
        var phaseA =
            WorldGenerationEntropy
                .Unit(
                    WorldGenerationEntropy
                        .Sample2D(
                            _seed,
                            _shapeADomain,
                            assignment.Root.X,
                            assignment.Root.Z)) *
            Math.Tau;
        var phaseB =
            WorldGenerationEntropy
                .Unit(
                    WorldGenerationEntropy
                        .Sample2D(
                            _seed,
                            _shapeBDomain,
                            assignment.Root.X,
                            assignment.Root.Z)) *
            Math.Tau;
        var shape =
            1d +
            0.13d *
            Math.Sin(
                angle * 3d +
                phaseA) +
            0.07d *
            Math.Sin(
                angle * 5d +
                phaseB);
        var radius =
            Math.Max(
                _seedSpacing,
                assignment.TargetSpan) *
            0.5d *
            shape;

        if (radius <= 0d)
        {
            return 0d;
        }

        return WorldGenerationEntropy
            .SmoothStep(
                1d -
                distance /
                radius);
    }

    private double SeedScore(
        FormationSeed seed,
        double x,
        double z)
    {
        var spacing =
            (double)_seedSpacing;
        var dx =
            x -
            seed.CenterX;
        var dz =
            z -
            seed.CenterZ;
        var distance =
            Math.Sqrt(
                dx * dx +
                dz * dz) /
            spacing;
        var angle =
            Math.Atan2(
                dz,
                dx);
        var phaseA =
            WorldGenerationEntropy
                .Unit(
                    WorldGenerationEntropy
                        .Sample2D(
                            _seed,
                            _shapeADomain,
                            seed.Bucket.X,
                            seed.Bucket.Z)) *
            Math.Tau;
        var phaseB =
            WorldGenerationEntropy
                .Unit(
                    WorldGenerationEntropy
                        .Sample2D(
                            _seed,
                            _shapeBDomain,
                            seed.Bucket.X,
                            seed.Bucket.Z)) *
            Math.Tau;
        var shape =
            1d +
            0.13d *
            Math.Sin(
                angle * 3d +
                phaseA) +
            0.07d *
            Math.Sin(
                angle * 5d +
                phaseB);
        var targetRatio =
            Math.Clamp(
                seed.Assignment.TargetSpan /
                (spacing * 3d),
                0.5d,
                3d);
        var sizeScale =
            Math.Pow(
                targetRatio,
                0.12d);
        var bias =
            WorldGenerationEntropy
                .SignedUnit(
                    WorldGenerationEntropy
                        .Sample2D(
                            _seed,
                            _seedBiasDomain,
                            seed.Bucket.X,
                            seed.Bucket.Z)) *
            0.045d;
        var continuationBonus =
            seed.Assignment.Root !=
            seed.Bucket
                ? 0.055d
                : 0d;

        return -distance /
                   (shape *
                    sizeScale) +
               bias +
               continuationBonus;
    }

    private (double X, double Z)
        WarpedPosition(
            int x,
            int z)
    {
        var spacing =
            (double)_seedSpacing;
        var coarsePeriod =
            spacing * _blending.CoarseWarpPeriod;
        var finePeriod =
            spacing * _blending.FineWarpPeriod;
        var xValue =
            (double)x;
        var zValue =
            (double)z;

        var coarseX =
            WorldGenerationEntropy
                .SmoothNoise2D(
                    _seed,
                    _warpCoarseXDomain,
                    xValue,
                    zValue,
                    coarsePeriod);
        var coarseZ =
            WorldGenerationEntropy
                .SmoothNoise2D(
                    _seed,
                    _warpCoarseZDomain,
                    xValue +
                    coarsePeriod *
                    0.37d,
                    zValue -
                    coarsePeriod *
                    0.61d,
                    coarsePeriod);
        var fineX =
            WorldGenerationEntropy
                .SmoothNoise2D(
                    _seed,
                    _warpFineXDomain,
                    xValue -
                    finePeriod *
                    0.43d,
                    zValue +
                    finePeriod *
                    0.29d,
                    finePeriod);
        var fineZ =
            WorldGenerationEntropy
                .SmoothNoise2D(
                    _seed,
                    _warpFineZDomain,
                    xValue +
                    finePeriod *
                    0.71d,
                    zValue +
                    finePeriod *
                    0.53d,
                    finePeriod);

        return (
            xValue +
            coarseX *
            spacing *
            _blending.CoarseWarpStrength +
            fineX *
            spacing *
            _blending.FineWarpStrength,
            zValue +
            coarseZ *
            spacing *
            _blending.CoarseWarpStrength +
            fineZ *
            spacing *
            _blending.FineWarpStrength);
    }

    private SeedBucket BucketForPosition(
        double x,
        double z) =>
        new(
            ClampBucket(
                Math.Floor(
                    x /
                    _seedSpacing)),
            ClampBucket(
                Math.Floor(
                    z /
                    _seedSpacing)));

    private static bool TryOffset(
        SeedBucket source,
        int dx,
        int dz,
        out SeedBucket result)
    {
        var x =
            (long)source.X +
            dx;
        var z =
            (long)source.Z +
            dz;

        if (x is <
                int.MinValue or
                > int.MaxValue ||
            z is <
                int.MinValue or
                > int.MaxValue)
        {
            result =
                default;
            return false;
        }

        result =
            new SeedBucket(
                (int)x,
                (int)z);
        return true;
    }

    private static int ClampBucket(
        double value) =>
        value switch
        {
            <= int.MinValue => int.MinValue,
            >= int.MaxValue => int.MaxValue,
            _ => (int)value,
        };

    private static ulong SaturatingAdd(
        ulong left,
        ulong right) =>
        ulong.MaxValue -
            left <
        right
            ? ulong.MaxValue
            : left + right;

    private static ulong SaturatingMultiply(
        ulong left,
        ulong right)
    {
        if (left == 0 ||
            right == 0)
        {
            return 0;
        }

        return left >
               ulong.MaxValue /
               right
            ? ulong.MaxValue
            : left * right;
    }

    private sealed class BiomeRule
    {
        private BiomeRule(
            string id,
            ulong weightUnits,
            ulong spawnWeightUnits,
            uint regionMin,
            uint regionMax,
            HashSet<string> cannotBorder,
            GenerationDomain targetDomain)
        {
            Id = id;
            WeightUnits = weightUnits;
            SpawnWeightUnits = spawnWeightUnits;
            RegionMin = regionMin;
            RegionMax = regionMax;
            CannotBorder = cannotBorder;
            TargetDomain = targetDomain;
        }

        public string Id { get; }

        public ulong WeightUnits { get; }

        public ulong SpawnWeightUnits { get; }

        public uint RegionMin { get; }

        public uint RegionMax { get; }

        public HashSet<string> CannotBorder { get; }

        public GenerationDomain TargetDomain { get; }

        public static BiomeRule Create(
            BiomeFieldRuleSource source,
            int _)
        {
            var layout =
                source.Layout;

            return new BiomeRule(
                source.Id,
                Math.Max(
                    1UL,
                    checked((ulong)
                        Math.Round(
                            layout.Weight *
                            4096d))),
                layout is BiomeSurfaceLayoutDefinition surface
                    ? surface.SpawnWeight <= 0f
                        ? 0UL
                        : Math.Max(
                            1UL,
                            checked((ulong)
                                Math.Round(
                                    surface.SpawnWeight *
                                    4096d)))
                    : 0UL,
                layout.RegionMin,
                layout.RegionMax,
                new HashSet<string>(
                    layout.CannotBorder,
                    StringComparer.Ordinal),
                GenerationDomain.Named(
                    "biome-layout/formation-target/v2/" +
                    source.Id));
        }
    }

    private readonly record struct SeedBucket(
        int X,
        int Z)
    {
        public int Classification()
        {
            var x =
                Mod(
                    X,
                    CompatibilityClassPeriod);
            var z =
                Mod(
                    Z,
                    CompatibilityClassPeriod);

            return x *
                   CompatibilityClassPeriod +
                   z;
        }

        private static int Mod(
            int value,
            int divisor)
        {
            var remainder =
                value %
                divisor;
            return remainder < 0
                ? remainder +
                  divisor
                : remainder;
        }
    }

    private readonly record struct SeedAssignment(
        int Rule,
        SeedBucket Root,
        uint TargetSpan);

    private readonly record struct FormationSeed(
        SeedBucket Bucket,
        SeedAssignment Assignment,
        double CenterX,
        double CenterZ);
}

internal readonly record struct BiomeFieldRuleSource(
    string Id,
    BiomePlacementLayoutDefinition Layout);

internal static class UInt32MathExtensions
{
    public static uint DivCeiling(
        this uint value,
        uint divisor)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        return value /
                   divisor +
               (value %
                    divisor ==
                0
                   ? 0u
                   : 1u);
    }
}
