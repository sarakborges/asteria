namespace Asteria.Core.World;

public sealed record SurfaceStructurePlacement(
    string Reference,
    int AnchorX,
    int AnchorZ,
    StructureDefinition Structure,
    StructureRotation Rotation,
    int OriginX,
    int OriginY,
    int OriginZ)
{
    public int MinimumY =>
        checked(
            OriginY +
            Structure.MinimumYOffset);

    public int MaximumY =>
        checked(
            OriginY +
            Structure.MaximumYOffset);

    public (int MinX, int MinZ, int MaxX, int MaxZ)
        HorizontalBounds()
    {
        var (minimum, maximum) =
            Structure.HorizontalBounds(
                Rotation);

        return (
            checked(OriginX + minimum.X),
            checked(OriginZ + minimum.Z),
            checked(OriginX + maximum.X),
            checked(OriginZ + maximum.Z));
    }
}

/// <summary>
/// Immutable deterministic owner for generated surface-structure placement.
/// It decides logical placements in world space; chunk materialization only
/// rasterizes placements whose already-authoritative bounds intersect a chunk.
/// </summary>
public sealed class SurfaceStructureField
{
    private readonly ulong _seed;
    private readonly BiomeField _biomes;
    private readonly SurfaceTerrainField _terrain;
    private readonly BiomeSurfaceMaterialField _materials;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly ResolvedRule[] _rules;
    private readonly int _minimumY;
    private readonly int _maximumY;
    private readonly int _queryPadding;

    public SurfaceStructureField(
        ulong seed,
        DimensionDefinition dimension,
        StructureRegistry structures,
        BiomeField biomes,
        SurfaceTerrainField terrain,
        BiomeSurfaceMaterialField materials,
        GeneratedFluidField generatedFluids,
        BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(generatedFluids);
        ArgumentNullException.ThrowIfNull(blocks);

        _seed = seed;
        _biomes = biomes;
        _terrain = terrain;
        _materials = materials;
        _generatedFluids = generatedFluids;
        _minimumY =
            dimension.Shell?.FloorY is { } floorY
                ? checked(floorY + 1)
                : 0;
        _maximumY =
            dimension.Shell?.RoofY is { } roofY
                ? checked(roofY - 1)
                : int.MaxValue;

        _rules =
            dimension.GeneratedSurfaceStructures
                .Select(
                    (definition, index) =>
                        ResolvedRule.Create(
                            definition,
                            index,
                            structures,
                            blocks))
                .ToArray();

        var maximumExtent =
            _rules.Length == 0
                ? 0
                : _rules.Max(rule => rule.MaximumHorizontalExtent);
        var maximumJitter =
            _rules.Length == 0
                ? 0
                : _rules.Max(rule => checked((int)rule.Jitter));
        _queryPadding =
            checked(
                maximumExtent * 2 +
                maximumJitter +
                2);
    }

    public bool HasRules =>
        _rules.Length > 0;

    public IReadOnlyList<SurfaceStructurePlacement>
        PlacementsIntersecting(
            int originX,
            int originZ,
            uint width,
            uint depth)
    {
        if (width == 0 ||
            depth == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width));
        }

        if (_rules.Length == 0)
        {
            return Array.Empty<SurfaceStructurePlacement>();
        }

        var maximumX =
            checked(
                originX +
                (int)width -
                1);
        var maximumZ =
            checked(
                originZ +
                (int)depth -
                1);
        var minimumQueryX =
            checked(
                originX -
                _queryPadding);
        var minimumQueryZ =
            checked(
                originZ -
                _queryPadding);
        var maximumQueryX =
            checked(
                maximumX +
                _queryPadding);
        var maximumQueryZ =
            checked(
                maximumZ +
                _queryPadding);

        var candidates =
            new List<Candidate>();

        for (var ruleIndex = 0;
             ruleIndex < _rules.Length;
             ruleIndex++)
        {
            var rule =
                _rules[ruleIndex];
            var spacing =
                checked((int)rule.Spacing);
            var minimumCellX =
                Math.DivRem(
                    minimumQueryX,
                    spacing,
                    out var minimumRemainderX);
            var minimumCellZ =
                Math.DivRem(
                    minimumQueryZ,
                    spacing,
                    out var minimumRemainderZ);

            if (minimumRemainderX < 0)
            {
                minimumCellX--;
            }

            if (minimumRemainderZ < 0)
            {
                minimumCellZ--;
            }

            var maximumCellX =
                Math.DivRem(
                    maximumQueryX,
                    spacing,
                    out var maximumRemainderX);
            var maximumCellZ =
                Math.DivRem(
                    maximumQueryZ,
                    spacing,
                    out var maximumRemainderZ);

            if (maximumRemainderX < 0)
            {
                maximumCellX--;
            }

            if (maximumRemainderZ < 0)
            {
                maximumCellZ--;
            }

            minimumCellX--;
            minimumCellZ--;
            maximumCellX++;
            maximumCellZ++;

            for (var cellZ = minimumCellZ;
                 cellZ <= maximumCellZ;
                 cellZ++)
            {
                for (var cellX = minimumCellX;
                     cellX <= maximumCellX;
                     cellX++)
                {
                    var candidate =
                        ResolveCandidate(
                            ruleIndex,
                            rule,
                            cellX,
                            cellZ);

                    if (candidate is not null)
                    {
                        candidates.Add(
                            candidate);
                    }
                }
            }
        }

        var result =
            new List<SurfaceStructurePlacement>();

        foreach (var candidate in
                 candidates)
        {
            if (!Intersects(
                    candidate,
                    originX,
                    originZ,
                    maximumX,
                    maximumZ))
            {
                continue;
            }

            if (candidates.Any(other =>
                    !ReferenceEquals(
                        candidate,
                        other) &&
                    Outranks(
                        other,
                        candidate) &&
                    Conflicts(
                        other,
                        candidate)))
            {
                continue;
            }

            result.Add(
                candidate.Placement);
        }

        return result
            .OrderBy(
                placement =>
                    placement.Reference,
                StringComparer.Ordinal)
            .ThenBy(
                placement =>
                    placement.AnchorX)
            .ThenBy(
                placement =>
                    placement.AnchorZ)
            .ThenBy(
                placement =>
                    placement.OriginY)
            .ToArray();
    }

    public ChunkSurfaceRange ExpandSurfaceRange(
        int chunkX,
        int chunkZ,
        ChunkSurfaceRange current)
    {
        if (_rules.Length == 0)
        {
            return current;
        }

        var originX =
            checked(
                chunkX *
                Chunk.Size);
        var originZ =
            checked(
                chunkZ *
                Chunk.Size);
        var placements =
            PlacementsIntersecting(
                originX,
                originZ,
                Chunk.Size,
                Chunk.Size);

        if (placements.Count == 0)
        {
            return current;
        }

        return new ChunkSurfaceRange(
            Math.Min(
                current.MinimumWorldY,
                placements.Min(
                    placement =>
                        placement.MinimumY)),
            Math.Max(
                current.MaximumWorldY,
                placements.Max(
                    placement =>
                        placement.MaximumY)));
    }

    private Candidate? ResolveCandidate(
        int ruleIndex,
        ResolvedRule rule,
        int cellX,
        int cellZ)
    {
        var presence =
            UnitProbability(
                WorldGenerationEntropy.Sample2D(
                    _seed,
                    rule.PresenceDomain,
                    cellX,
                    cellZ));

        if (presence >
            rule.Chance)
        {
            return null;
        }

        var spacing =
            checked((long)rule.Spacing);
        var anchorX =
            (long)cellX *
            spacing +
            spacing / 2 +
            JitterOffset(
                WorldGenerationEntropy.Sample2D(
                    _seed,
                    rule.JitterXDomain,
                    cellX,
                    cellZ),
                rule.Jitter);
        var anchorZ =
            (long)cellZ *
            spacing +
            spacing / 2 +
            JitterOffset(
                WorldGenerationEntropy.Sample2D(
                    _seed,
                    rule.JitterZDomain,
                    cellX,
                    cellZ),
                rule.Jitter);

        if (anchorX is <
                int.MinValue or >
                int.MaxValue ||
            anchorZ is <
                int.MinValue or >
                int.MaxValue)
        {
            return null;
        }

        var x =
            (int)anchorX;
        var z =
            (int)anchorZ;
        var sample =
            _biomes.Sample(
                x,
                z);

        if (!string.Equals(
                sample.Primary,
                rule.Biome,
                StringComparison.Ordinal))
        {
            return null;
        }

        var variantHash =
            WorldGenerationEntropy.Sample2D(
                _seed,
                rule.VariantDomain,
                cellX,
                cellZ);
        var structure =
            rule.Variants[
                (int)(
                    variantHash %
                    (ulong)rule.Variants.Length)];
        var rotation =
            structure.Rotation
                ? (StructureRotation)(
                    WorldGenerationEntropy.Sample2D(
                        _seed,
                        rule.RotationDomain,
                        cellX,
                        cellZ) %
                    4)
                : StructureRotation.Degrees0;

        if (!TryFitOrigin(
                rule,
                structure,
                rotation,
                x,
                z,
                out var originY))
        {
            return null;
        }

        var placement =
            new SurfaceStructurePlacement(
                rule.Reference,
                x,
                z,
                structure,
                rotation,
                x,
                originY,
                z);
        var bounds =
            placement.HorizontalBounds();

        return new Candidate(
            ruleIndex,
            cellX,
            cellZ,
            rule,
            placement,
            bounds.MinX,
            bounds.MinZ,
            bounds.MaxX,
            bounds.MaxZ);
    }

    private bool TryFitOrigin(
        ResolvedRule rule,
        StructureDefinition structure,
        StructureRotation rotation,
        int anchorX,
        int anchorZ,
        out int originY)
    {
        var minimumGround =
            int.MaxValue;
        var maximumGround =
            int.MinValue;

        foreach (var support in
                 structure.Supports(
                     rotation))
        {
            var x =
                checked(
                    anchorX +
                    support.X);
            var z =
                checked(
                    anchorZ +
                    support.Z);
            var groundY =
                _terrain.BaseSurfaceHeight(
                    x,
                    z);

            if (structure.Restrictions.RequiresDryGround &&
                HasGeneratedFluidAbove(
                    x,
                    groundY,
                    z))
            {
                originY = 0;
                return false;
            }

            minimumGround =
                Math.Min(
                    minimumGround,
                    groundY);
            maximumGround =
                Math.Max(
                    maximumGround,
                    groundY);
        }

        if (minimumGround ==
            int.MaxValue)
        {
            originY = 0;
            return false;
        }

        var slope =
            maximumGround -
            minimumGround;

        if (slope <
                structure.Restrictions.MinSlope ||
            slope >
                structure.Restrictions.MaxSlope)
        {
            originY = 0;
            return false;
        }

        originY =
            checked(
                minimumGround -
                structure.MinimumYOffset);

        if (checked(
                originY +
                structure.MinimumYOffset) <
                _minimumY ||
            checked(
                originY +
                structure.MaximumYOffset) >
                _maximumY)
        {
            return false;
        }

        if (structure.Restrictions.GroundBlocks.Count >
            0)
        {
            var allowed =
                rule.AllowedGroundBlocks;

            foreach (var support in
                     structure.Supports(
                         rotation))
            {
                var x =
                    checked(
                        anchorX +
                        support.X);
                var z =
                    checked(
                        anchorZ +
                        support.Z);
                var sample =
                    _biomes.Sample(
                        x,
                        z);
                var block =
                    _materials.BlockAt(
                        sample,
                        x,
                        z,
                        0);

                if (!allowed.Contains(
                        block))
                {
                    return false;
                }
            }
        }

        if (structure.Restrictions.RequiredBiomeCoverage >
            0f)
        {
            var footprint =
                structure.Footprint(
                    rotation);
            var matching =
                footprint.Count(offset =>
                    string.Equals(
                        _biomes.Sample(
                                checked(
                                    anchorX +
                                    offset.X),
                                checked(
                                    anchorZ +
                                    offset.Z))
                            .Primary,
                        rule.Biome,
                        StringComparison.Ordinal));
            var coverage =
                matching /
                (float)Math.Max(
                    1,
                    footprint.Count);

            if (coverage +
                float.Epsilon <
                structure.Restrictions.RequiredBiomeCoverage)
            {
                return false;
            }
        }

        if (structure.Generation.FluidPolicy ==
            StructureFluidPolicy.Forbid)
        {
            foreach (var voxel in
                     structure.RotatedVoxels(
                         rotation))
            {
                var x =
                    checked(
                        anchorX +
                        voxel.Offset.X);
                var y =
                    checked(
                        originY +
                        voxel.Offset.Y);
                var z =
                    checked(
                        anchorZ +
                        voxel.Offset.Z);

                if (HasGeneratedFluidAt(
                        x,
                        y,
                        z))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool HasGeneratedFluidAbove(
        int worldX,
        int groundY,
        int worldZ) =>
        HasGeneratedFluidAt(
            worldX,
            checked(
                groundY +
                1),
            worldZ);

    private bool HasGeneratedFluidAt(
        int worldX,
        int worldY,
        int worldZ)
    {
        if (worldY < 0)
        {
            return false;
        }

        var sample =
            _biomes.Sample(
                worldX,
                worldZ);
        var baseY =
            _terrain.BaseSurfaceHeight(
                worldX,
                worldZ);
        var density =
            _terrain.DensityAt(
                worldX,
                worldY,
                worldZ);

        return !_generatedFluids
            .FluidAt(
                sample,
                baseY,
                worldY,
                density)
            .IsEmpty;
    }

    private static bool Outranks(
        Candidate left,
        Candidate right) =>
        Compare(
            left,
            right) <
        0;

    private static int Compare(
        Candidate left,
        Candidate right)
    {
        var priority =
            right.Placement.Structure.Priority
                .CompareTo(
                    left.Placement.Structure.Priority);

        if (priority != 0)
        {
            return priority;
        }

        var reference =
            string.Compare(
                left.Rule.Reference,
                right.Rule.Reference,
                StringComparison.Ordinal);

        if (reference != 0)
        {
            return reference;
        }

        var biome =
            string.Compare(
                left.Rule.Biome,
                right.Rule.Biome,
                StringComparison.Ordinal);

        if (biome != 0)
        {
            return biome;
        }

        var x =
            left.Placement.AnchorX.CompareTo(
                right.Placement.AnchorX);

        if (x != 0)
        {
            return x;
        }

        var z =
            left.Placement.AnchorZ.CompareTo(
                right.Placement.AnchorZ);

        if (z != 0)
        {
            return z;
        }

        return left.Placement.OriginY.CompareTo(
            right.Placement.OriginY);
    }

    private static bool Conflicts(
        Candidate higher,
        Candidate lower)
    {
        if (!RectanglesOverlap(
                higher.MinimumX,
                higher.MinimumZ,
                higher.MaximumX,
                higher.MaximumZ,
                lower.MinimumX,
                lower.MinimumZ,
                lower.MaximumX,
                lower.MaximumZ) ||
            higher.Placement.MaximumY <
                lower.Placement.MinimumY ||
            higher.Placement.MinimumY >
                lower.Placement.MaximumY)
        {
            return false;
        }

        return higher.Placement.Structure.Generation.ReserveSpace ||
               higher.Placement.Structure.ConflictGroups.Any(
                   group =>
                       lower.Placement.Structure.ConflictGroups.Contains(
                           group,
                           StringComparer.Ordinal));
    }

    private static bool Intersects(
        Candidate candidate,
        int minimumX,
        int minimumZ,
        int maximumX,
        int maximumZ) =>
        RectanglesOverlap(
            candidate.MinimumX,
            candidate.MinimumZ,
            candidate.MaximumX,
            candidate.MaximumZ,
            minimumX,
            minimumZ,
            maximumX,
            maximumZ);

    private static bool RectanglesOverlap(
        int leftMinimumX,
        int leftMinimumZ,
        int leftMaximumX,
        int leftMaximumZ,
        int rightMinimumX,
        int rightMinimumZ,
        int rightMaximumX,
        int rightMaximumZ) =>
        leftMinimumX <=
            rightMaximumX &&
        leftMaximumX >=
            rightMinimumX &&
        leftMinimumZ <=
            rightMaximumZ &&
        leftMaximumZ >=
            rightMinimumZ;

    private static double UnitProbability(
        ulong value) =>
        value /
        (double)ulong.MaxValue;

    private static long JitterOffset(
        ulong value,
        uint jitter)
    {
        if (jitter == 0)
        {
            return 0;
        }

        var span =
            (ulong)jitter *
            2UL +
            1UL;

        return (long)(
                   value %
                   span) -
               jitter;
    }

    private sealed record Candidate(
        int RuleIndex,
        int CellX,
        int CellZ,
        ResolvedRule Rule,
        SurfaceStructurePlacement Placement,
        int MinimumX,
        int MinimumZ,
        int MaximumX,
        int MaximumZ);

    private sealed class ResolvedRule
    {
        private ResolvedRule(
            string biome,
            string reference,
            uint spacing,
            uint jitter,
            float chance,
            StructureDefinition[] variants,
            HashSet<BlockRuntimeId> allowedGroundBlocks,
            int maximumHorizontalExtent,
            GenerationDomain presenceDomain,
            GenerationDomain jitterXDomain,
            GenerationDomain jitterZDomain,
            GenerationDomain variantDomain,
            GenerationDomain rotationDomain)
        {
            Biome = biome;
            Reference = reference;
            Spacing = spacing;
            Jitter = jitter;
            Chance = chance;
            Variants = variants;
            AllowedGroundBlocks = allowedGroundBlocks;
            MaximumHorizontalExtent = maximumHorizontalExtent;
            PresenceDomain = presenceDomain;
            JitterXDomain = jitterXDomain;
            JitterZDomain = jitterZDomain;
            VariantDomain = variantDomain;
            RotationDomain = rotationDomain;
        }

        public string Biome { get; }

        public string Reference { get; }

        public uint Spacing { get; }

        public uint Jitter { get; }

        public float Chance { get; }

        public StructureDefinition[] Variants { get; }

        public HashSet<BlockRuntimeId> AllowedGroundBlocks { get; }

        public int MaximumHorizontalExtent { get; }

        public GenerationDomain PresenceDomain { get; }

        public GenerationDomain JitterXDomain { get; }

        public GenerationDomain JitterZDomain { get; }

        public GenerationDomain VariantDomain { get; }

        public GenerationDomain RotationDomain { get; }

        public static ResolvedRule Create(
            DimensionGeneratedSurfaceStructureDefinition definition,
            int _,
            StructureRegistry structures,
            BlockRegistry blocks)
        {
            if (definition.Placement !=
                GeneratedSurfaceStructurePlacement.BiomeInterior)
            {
                throw new ArgumentException(
                    $"Structure rule {definition.Structure} uses unsupported placement {definition.Placement}.");
            }

            var variants =
                structures
                    .ReferenceMembers(
                        definition.Structure)
                    .OrderBy(
                        structure =>
                            structure.Id,
                        StringComparer.Ordinal)
                    .ToArray();

            var allowedGroundBlocks =
                variants
                    .SelectMany(
                        variant =>
                            variant.Restrictions.GroundBlocks)
                    .Select(
                        blocks.GetId)
                    .ToHashSet();

            var suffix =
                $"{definition.Biome}/{definition.Structure}";

            return new ResolvedRule(
                definition.Biome,
                definition.Structure,
                definition.Spacing,
                definition.Jitter,
                definition.Chance,
                variants,
                allowedGroundBlocks,
                variants.Max(
                    variant =>
                        variant.MaximumHorizontalExtent),
                GenerationDomain.Named(
                    $"structure/root/presence/v1/{suffix}"),
                GenerationDomain.Named(
                    $"structure/root/jitter-x/v1/{suffix}"),
                GenerationDomain.Named(
                    $"structure/root/jitter-z/v1/{suffix}"),
                GenerationDomain.Named(
                    $"structure/root/variant/v1/{suffix}"),
                GenerationDomain.Named(
                    $"structure/root/rotation/v1/{suffix}"));
        }
    }
}
