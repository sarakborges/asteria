namespace Asteria.Core.World;

public readonly record struct BiomeInfluence(
    string BiomeId,
    float Weight);

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
        Influences
            .First(influence =>
                string.Equals(
                    influence.BiomeId,
                    Primary,
                    StringComparison.Ordinal))
            .Weight;
}

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
    private const double JitterFraction = 0.32;
    private const double BlendScoreBand = 0.18;

    private readonly ulong _seed;
    private readonly BiomeRule[] _rules;
    private readonly int _seedSpacing;
    private readonly GenerationDomain _seedPickDomain =
        GenerationDomain.Named(
            "biome-layout/seed-pick/v1");
    private readonly GenerationDomain _jitterXDomain =
        GenerationDomain.Named(
            "biome-layout/seed-jitter-x/v1");
    private readonly GenerationDomain _jitterZDomain =
        GenerationDomain.Named(
            "biome-layout/seed-jitter-z/v1");
    private readonly GenerationDomain _shapeADomain =
        GenerationDomain.Named(
            "biome-layout/seed-shape-a/v1");
    private readonly GenerationDomain _shapeBDomain =
        GenerationDomain.Named(
            "biome-layout/seed-shape-b/v1");
    private readonly GenerationDomain _seedBiasDomain =
        GenerationDomain.Named(
            "biome-layout/seed-bias/v1");
    private readonly GenerationDomain _warpCoarseXDomain =
        GenerationDomain.Named(
            "biome-layout/warp-coarse-x/v1");
    private readonly GenerationDomain _warpCoarseZDomain =
        GenerationDomain.Named(
            "biome-layout/warp-coarse-z/v1");
    private readonly GenerationDomain _warpFineXDomain =
        GenerationDomain.Named(
            "biome-layout/warp-fine-x/v1");
    private readonly GenerationDomain _warpFineZDomain =
        GenerationDomain.Named(
            "biome-layout/warp-fine-z/v1");

    public BiomeField(
        ulong seed,
        DimensionDefinition dimension,
        BiomeRegistry biomes)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            biomes);

        _seed = seed;
        _rules =
            dimension
                .Biomes
                .Select(
                    biomes.Get)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .Select(
                    (definition, index) =>
                        BiomeRule.Create(
                            definition,
                            index))
                .ToArray();

        if (_rules.Length == 0)
        {
            throw new ArgumentException(
                $"Dimension {dimension.Id} has no surface biomes.",
                nameof(dimension));
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

    public int SeedSpacing =>
        _seedSpacing;

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
                double Weight)>(
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
                BlendScoreBand)
            {
                continue;
            }

            var proximity =
                1d -
                delta /
                BlendScoreBand;
            var weight =
                WorldGenerationEntropy
                    .SmoothStep(
                        proximity);
            weighted.Add(
                (
                    rule,
                    weight));
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
                            total))))
                .ToArray();

        return new BiomeSample(
            _rules[
                primaryRule].Id,
            influences);
    }

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

        cache.Add(
            bucket,
            assignment);
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
            JitterFraction;
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
            JitterFraction;

        return (
            baseX + jitterX,
            baseZ + jitterZ);
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
            spacing * 4d;
        var finePeriod =
            spacing * 1.35d;
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
            0.42d +
            fineX *
            spacing *
            0.16d,
            zValue +
            coarseZ *
            spacing *
            0.42d +
            fineZ *
            spacing *
            0.16d);
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
            uint regionMin,
            uint regionMax,
            HashSet<string> cannotBorder,
            GenerationDomain targetDomain)
        {
            Id = id;
            WeightUnits = weightUnits;
            RegionMin = regionMin;
            RegionMax = regionMax;
            CannotBorder = cannotBorder;
            TargetDomain = targetDomain;
        }

        public string Id { get; }

        public ulong WeightUnits { get; }

        public uint RegionMin { get; }

        public uint RegionMax { get; }

        public HashSet<string> CannotBorder { get; }

        public GenerationDomain TargetDomain { get; }

        public static BiomeRule Create(
            BiomeDefinition definition,
            int _)
        {
            var layout =
                definition.SurfaceLayout;

            return new BiomeRule(
                definition.Id,
                Math.Max(
                    1UL,
                    checked((ulong)
                        Math.Round(
                            layout.Weight *
                            4096d))),
                layout.RegionMin,
                layout.RegionMax,
                new HashSet<string>(
                    layout.CannotBorder,
                    StringComparer.Ordinal),
                GenerationDomain.Named(
                    "biome-layout/formation-target/v1/" +
                    definition.Id));
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
