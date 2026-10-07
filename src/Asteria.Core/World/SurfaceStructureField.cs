namespace Asteria.Core.World;

/// <summary>
/// Deterministic generated surface-structure placement. This owner decides
/// logical placements only; SurfaceChunkMaterializer remains the single
/// procedural voxel writer.
/// </summary>
public sealed class SurfaceStructureField
{
    private const int PlacementCacheCapacity = 128;

    private readonly ulong _seed;
    private readonly BiomeField _biomes;
    private readonly SurfaceTerrainField _terrain;
    private readonly BiomeSurfaceMaterialField _materials;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly RootRule[] _rules;
    private readonly int? _floorY;
    private readonly int? _roofY;
    private readonly BoundedMemoCache<
        StructureChunkKey,
        IReadOnlyList<SurfaceStructurePlacement>>
        _placements =
            new(
                PlacementCacheCapacity);

    public SurfaceStructureField(
        ulong seed,
        DimensionDefinition dimension,
        StructureRegistry structures,
        StructureSetRegistry structureSets,
        BlockRegistry blocks,
        FluidRegistry fluids,
        BiomeField biomes,
        SurfaceTerrainField terrain,
        BiomeSurfaceMaterialField materials,
        GeneratedFluidField generatedFluids)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            structures);
        ArgumentNullException.ThrowIfNull(
            structureSets);
        ArgumentNullException.ThrowIfNull(
            blocks);
        ArgumentNullException.ThrowIfNull(
            fluids);
        _biomes =
            biomes ??
            throw new ArgumentNullException(
                nameof(biomes));
        _terrain =
            terrain ??
            throw new ArgumentNullException(
                nameof(terrain));
        _materials =
            materials ??
            throw new ArgumentNullException(
                nameof(materials));
        _generatedFluids =
            generatedFluids ??
            throw new ArgumentNullException(
                nameof(generatedFluids));

        _seed = seed;
        _floorY =
            dimension.Shell?.FloorY;
        _roofY =
            dimension.Shell?.RoofY;

        _rules =
            dimension
                .GeneratedSurfaceStructures
                .OrderBy(
                    generated =>
                        generated.Biome,
                    StringComparer.Ordinal)
                .ThenBy(
                    generated =>
                        generated.Structure,
                    StringComparer.Ordinal)
                .Select(
                    generated =>
                        RootRule.Create(
                            generated,
                            structures,
                            structureSets,
                            blocks,
                            fluids))
                .ToArray();
    }

    public bool HasRules =>
        _rules.Length > 0;

    internal IReadOnlyList<SurfaceStructurePlacement>
        PlacementsForChunk(
            int chunkX,
            int chunkZ) =>
        _placements.GetOrAdd(
            new StructureChunkKey(
                chunkX,
                chunkZ),
            () =>
                ResolvePlacements(
                    chunkX,
                    chunkZ));

    public ChunkSurfaceRange ExpandSurfaceRange(
        ChunkSurfaceRange terrainRange,
        int chunkX,
        int chunkZ)
    {
        var minimum =
            terrainRange.MinimumWorldY;
        var maximum =
            terrainRange.MaximumWorldY;

        foreach (var placement in
                 PlacementsForChunk(
                     chunkX,
                     chunkZ))
        {
            minimum =
                Math.Min(
                    minimum,
                    placement.MinimumY);
            maximum =
                Math.Max(
                    maximum,
                    placement.MaximumY);
        }

        return new ChunkSurfaceRange(
            Math.Max(
                0,
                minimum),
            maximum);
    }

    private IReadOnlyList<SurfaceStructurePlacement>
        ResolvePlacements(
            int chunkX,
            int chunkZ)
    {
        if (_rules.Length == 0)
        {
            return Array.Empty<
                SurfaceStructurePlacement>();
        }

        var originX =
            checked(
                chunkX *
                Chunk.Size);
        var originZ =
            checked(
                chunkZ *
                Chunk.Size);
        var maximumX =
            checked(
                originX +
                Chunk.Size -
                1);
        var maximumZ =
            checked(
                originZ +
                Chunk.Size -
                1);
        var direct =
            CollectCandidatesIntersectingBounds(
                originX,
                originZ,
                maximumX,
                maximumZ);
        var placements =
            ResolveConflicts(
                    direct)
                .SelectMany(
                    candidate =>
                        candidate.Placements)
                .Where(
                    placement =>
                        placement.IntersectsHorizontal(
                            originX,
                            originZ,
                            maximumX,
                            maximumZ))
                .ToArray();

        Array.Sort(
            placements,
            SurfaceStructurePlacement
                .CompareDeterministically);
        return Array.AsReadOnly(
            placements);
    }

    private List<StructureCandidate>
        CollectCandidatesIntersectingBounds(
            int minimumX,
            int minimumZ,
            int maximumX,
            int maximumZ)
    {
        var candidates =
            new List<StructureCandidate>();

        for (var ruleIndex = 0;
             ruleIndex < _rules.Length;
             ruleIndex++)
        {
            var rule =
                _rules[ruleIndex];
            var padding =
                checked(
                    rule.MaximumHorizontalRadius +
                    rule.Jitter +
                    1);
            var halfSpacing =
                rule.Spacing /
                2;
            var minimumCellX =
                FloorDiv(
                    (long)minimumX -
                    padding -
                    halfSpacing,
                    rule.Spacing) -
                1;
            var maximumCellX =
                FloorDiv(
                    (long)maximumX +
                    padding -
                    halfSpacing,
                    rule.Spacing) +
                1;
            var minimumCellZ =
                FloorDiv(
                    (long)minimumZ -
                    padding -
                    halfSpacing,
                    rule.Spacing) -
                1;
            var maximumCellZ =
                FloorDiv(
                    (long)maximumZ +
                    padding -
                    halfSpacing,
                    rule.Spacing) +
                1;

            for (var cellZ =
                     minimumCellZ;
                 cellZ <=
                     maximumCellZ;
                 cellZ++)
            {
                for (var cellX =
                         minimumCellX;
                     cellX <=
                     maximumCellX;
                     cellX++)
                {
                    if (cellX is <
                            int.MinValue or
                            > int.MaxValue ||
                        cellZ is <
                            int.MinValue or
                            > int.MaxValue)
                    {
                        continue;
                    }

                    var candidate =
                        ResolveCandidate(
                            ruleIndex,
                            rule,
                            (int)cellX,
                            (int)cellZ);

                    if (candidate is null ||
                        !candidate.IntersectsHorizontal(
                            minimumX,
                            minimumZ,
                            maximumX,
                            maximumZ))
                    {
                        continue;
                    }

                    candidates.Add(
                        candidate);
                }
            }
        }

        return candidates;
    }

    private IReadOnlyList<StructureCandidate>
        ResolveConflicts(
            IReadOnlyList<StructureCandidate> direct)
    {
        if (direct.Count == 0)
        {
            return Array.Empty<StructureCandidate>();
        }

        var competitors =
            new List<StructureCandidate>(
                direct);
        var seen =
            direct
                .Select(
                    CandidateIdentity)
                .ToHashSet();

        foreach (var candidate in
                 direct)
        {
            foreach (var other in
                     CollectCandidatesIntersectingBounds(
                         candidate.MinimumX,
                         candidate.MinimumZ,
                         candidate.MaximumX,
                         candidate.MaximumZ))
            {
                if (SameCandidate(
                        other,
                        candidate) ||
                    !CandidateOutranks(
                        other,
                        candidate) ||
                    !CandidatesConflict(
                        other,
                        candidate))
                {
                    continue;
                }

                if (seen.Add(
                        CandidateIdentity(
                            other)))
                {
                    competitors.Add(
                        other);
                }
            }
        }

        return direct
            .Where(candidate =>
                !competitors.Any(other =>
                    !SameCandidate(
                        other,
                        candidate) &&
                    CandidateOutranks(
                        other,
                        candidate) &&
                    CandidatesConflict(
                        other,
                        candidate)))
            .ToArray();
    }

    private StructureCandidate?
        ResolveCandidate(
            int ruleIndex,
            RootRule rule,
            int cellX,
            int cellZ)
    {
        if (rule.Chance <= 0f)
        {
            return null;
        }

        if (rule.Chance < 1f &&
            WorldGenerationEntropy.Unit(
                WorldGenerationEntropy.Sample2D(
                    _seed,
                    rule.PresenceDomain,
                    cellX,
                    cellZ)) >=
            rule.Chance)
        {
            return null;
        }

        var anchorX =
            CandidateAxis(
                cellX,
                rule.Spacing,
                rule.Jitter,
                WorldGenerationEntropy.Sample2D(
                    _seed,
                    rule.JitterXDomain,
                    cellX,
                    cellZ));
        var anchorZ =
            CandidateAxis(
                cellZ,
                rule.Spacing,
                rule.Jitter,
                WorldGenerationEntropy.Sample2D(
                    _seed,
                    rule.JitterZDomain,
                    cellX,
                    cellZ));

        if (anchorX is null ||
            anchorZ is null)
        {
            return null;
        }

        var anchorSurface =
            SurfaceAt(
                anchorX.Value,
                anchorZ.Value);
        if (!string.Equals(
                anchorSurface.Biome.Primary,
                rule.Biome,
                StringComparison.Ordinal))
        {
            return null;
        }

        IReadOnlyList<SurfaceStructurePlacement>
            placements;
        int priority;
        bool reserveSpace;
        IReadOnlyList<string> conflictGroups;

        if (rule.Set is
            { } set)
        {
            var resolved =
                set.Resolve(
                    _seed,
                    cellX,
                    cellZ,
                    anchorX.Value,
                    anchorZ.Value,
                    (member, rotation, pieceX, pieceZ) =>
                        TryPlaceStructure(
                            rule,
                            member,
                            pieceX,
                            pieceZ,
                            rotation));

            if (resolved is null ||
                resolved.Count == 0)
            {
                return null;
            }

            placements = resolved;
            priority =
                set.Definition.Priority;
            reserveSpace =
                set.Definition.ReserveSpace;
            conflictGroups =
                set.Definition.ConflictGroups;
        }
        else
        {
            var memberHash =
                WorldGenerationEntropy.Sample2D(
                    _seed,
                    rule.VariantDomain,
                    cellX,
                    cellZ);
            var member =
                rule.Members[
                    checked((int)(
                        memberHash %
                        (ulong)rule.Members.Length))];
            var rotation =
                member.Definition
                    .RotationForHash(
                        WorldGenerationEntropy.Sample2D(
                            _seed,
                            rule.RotationDomain,
                            cellX,
                            cellZ));
            var placement =
                TryPlaceStructure(
                    rule,
                    member,
                    anchorX.Value,
                    anchorZ.Value,
                    rotation);

            if (placement is null)
            {
                return null;
            }

            placements =
            [
                placement,
            ];
            priority =
                member.Definition.Priority;
            reserveSpace =
                member.Definition.Generation.ReserveSpace;
            conflictGroups =
                member.Definition.ConflictGroups;
        }

        var minimumX =
            placements.Min(
                placement =>
                    placement.MinimumX);
        var maximumX =
            placements.Max(
                placement =>
                    placement.MaximumX);
        var minimumY =
            placements.Min(
                placement =>
                    placement.MinimumY);
        var maximumY =
            placements.Max(
                placement =>
                    placement.MaximumY);
        var minimumZ =
            placements.Min(
                placement =>
                    placement.MinimumZ);
        var maximumZ =
            placements.Max(
                placement =>
                    placement.MaximumZ);

        return new StructureCandidate(
            ruleIndex,
            cellX,
            cellZ,
            priority,
            reserveSpace,
            conflictGroups,
            placements,
            anchorX.Value,
            anchorZ.Value,
            minimumX,
            maximumX,
            minimumY,
            maximumY,
            minimumZ,
            maximumZ);
    }

    private SurfaceStructurePlacement?
        TryPlaceStructure(
            RootRule rule,
            RuntimeStructure member,
            int anchorX,
            int anchorZ,
            StructureRotation rotation)
    {
        var anchorSurface =
            SurfaceAt(
                anchorX,
                anchorZ);
        var footprint =
            member.HorizontalFootprint(
                rotation);
        var matchingBiome =
            0;
        var minimumSurfaceY =
            int.MaxValue;
        var maximumSurfaceY =
            int.MinValue;

        foreach (var offset in
                 footprint)
        {
            var x =
                checked(
                    anchorX +
                    offset.X);
            var z =
                checked(
                    anchorZ +
                    offset.Z);
            var surface =
                SurfaceAt(
                    x,
                    z);

            if (string.Equals(
                    surface.Biome.Primary,
                    rule.Biome,
                    StringComparison.Ordinal))
            {
                matchingBiome++;
            }

            minimumSurfaceY =
                Math.Min(
                    minimumSurfaceY,
                    surface.BaseY);
            maximumSurfaceY =
                Math.Max(
                    maximumSurfaceY,
                    surface.BaseY);

            if (member.Definition
                    .Restrictions
                    .RequiresDryGround &&
                IsGeneratedFluidAbove(
                    surface))
            {
                return null;
            }
        }

        if (member.AllowedGroundBlocks.Count >
            0)
        {
            foreach (var offset in
                     member.SupportOffsets(
                         rotation))
            {
                var x =
                    checked(
                        anchorX +
                        offset.X);
                var z =
                    checked(
                        anchorZ +
                        offset.Z);
                var surface =
                    SurfaceAt(
                        x,
                        z);
                var groundBlock =
                    _materials.BlockAt(
                        surface.Biome,
                        x,
                        z,
                        0);

                if (!member.AllowedGroundBlocks.Contains(
                        groundBlock))
                {
                    return null;
                }
            }
        }

        var coverage =
            matchingBiome /
            (float)footprint.Count;
        if (coverage +
                1e-6f <
            member.Definition
                .Restrictions
                .RequiredBiomeCoverage)
        {
            return null;
        }

        if ((long)maximumSurfaceY -
                minimumSurfaceY >
            member.Definition
                .Restrictions
                .MaxSlope)
        {
            return null;
        }

        if (member.Proximity.Any(proximity =>
                !ProximityRuleSatisfied(
                    anchorX,
                    anchorZ,
                    proximity)))
        {
            return null;
        }

        var placement =
            member.Place(
                rule.Reference,
                anchorX,
                anchorSurface.BaseY,
                anchorZ,
                rotation);

        if (placement.MinimumY < 0 ||
            (_floorY is { } floorY &&
             placement.MinimumY <=
             floorY) ||
            (_roofY is { } roofY &&
             placement.MaximumY >=
             roofY))
        {
            return null;
        }

        if (member.Definition
                .Generation
                .FluidPolicy ==
            StructureFluidPolicy.Forbid &&
            placement.Voxels.Any(voxel =>
                GeneratedFluidExistsAt(
                    voxel.X,
                    voxel.Y,
                    voxel.Z)))
        {
            return null;
        }

        return placement;
    }

    private bool GeneratedFluidExistsAt(
        int worldX,
        int worldY,
        int worldZ)
    {
        var surface =
            SurfaceAt(
                worldX,
                worldZ);

        return !_generatedFluids
            .FluidAtEmptyVoxel(
                surface.Biome,
                surface.BaseY,
                worldY)
            .IsEmpty;
    }

    private bool ProximityRuleSatisfied(
        int anchorX,
        int anchorZ,
        RuntimeProximityRule rule)
    {
        var minimumSquared =
            (long)rule.MinimumDistance *
            rule.MinimumDistance;
        var maximumSquared =
            (long)rule.MaximumDistance *
            rule.MaximumDistance;
        var found =
            false;

        for (var offsetZ =
                 -rule.MaximumDistance;
             offsetZ <=
                 rule.MaximumDistance;
             offsetZ++)
        {
            for (var offsetX =
                     -rule.MaximumDistance;
                 offsetX <=
                     rule.MaximumDistance;
                 offsetX++)
            {
                var squared =
                    (long)offsetX *
                    offsetX +
                    (long)offsetZ *
                    offsetZ;

                if (squared <
                        minimumSquared ||
                    squared >
                        maximumSquared)
                {
                    continue;
                }

                var worldX =
                    (long)anchorX +
                    offsetX;
                var worldZ =
                    (long)anchorZ +
                    offsetZ;

                if (worldX is <
                        int.MinValue or >
                        int.MaxValue ||
                    worldZ is <
                        int.MinValue or >
                        int.MaxValue)
                {
                    continue;
                }

                if (!ProximityTargetMatches(
                        (int)worldX,
                        (int)worldZ,
                        rule))
                {
                    continue;
                }

                found =
                    true;

                if (rule.Mode ==
                    StructureProximityMode.Required)
                {
                    return true;
                }

                return false;
            }
        }

        return rule.Mode switch
        {
            StructureProximityMode.Required =>
                found,
            StructureProximityMode.Forbidden =>
                true,
            _ =>
                throw new InvalidOperationException(
                    $"Unknown structure proximity mode {rule.Mode}."),
        };
    }

    private bool ProximityTargetMatches(
        int worldX,
        int worldZ,
        RuntimeProximityRule rule)
    {
        var surface =
            SurfaceAt(
                worldX,
                worldZ);

        if (!rule.Block.IsAir)
        {
            return _materials.BlockAt(
                       surface.Biome,
                       worldX,
                       worldZ,
                       0) ==
                   rule.Block;
        }

        if (!rule.Fluid.IsNone)
        {
            var aboveY =
                (long)surface.BaseY +
                1L;

            if (aboveY is <
                    int.MinValue or >
                    int.MaxValue)
            {
                return false;
            }

            return _generatedFluids
                       .FluidAtEmptyVoxel(
                           surface.Biome,
                           surface.BaseY,
                           (int)aboveY)
                       .Fluid ==
                   rule.Fluid;
        }

        throw new InvalidOperationException(
            "Validated structure proximity rule has no runtime target.");
    }

    private bool CandidateOutranks(
        StructureCandidate left,
        StructureCandidate right)
    {
        var leftRule =
            _rules[left.RuleIndex];
        var rightRule =
            _rules[right.RuleIndex];

        var priority =
            right.Priority.CompareTo(
                left.Priority);

        if (priority != 0)
        {
            return priority < 0;
        }

        var reference =
            string.Compare(
                leftRule.Reference,
                rightRule.Reference,
                StringComparison.Ordinal);
        if (reference != 0)
        {
            return reference < 0;
        }

        var biome =
            string.Compare(
                leftRule.Biome,
                rightRule.Biome,
                StringComparison.Ordinal);
        if (biome != 0)
        {
            return biome < 0;
        }

        var x =
            left.AnchorX.CompareTo(
                right.AnchorX);
        if (x != 0)
        {
            return x < 0;
        }

        var z =
            left.AnchorZ.CompareTo(
                right.AnchorZ);
        if (z != 0)
        {
            return z < 0;
        }

        return left.MinimumY <
               right.MinimumY;
    }

    private static bool CandidatesConflict(
        StructureCandidate higher,
        StructureCandidate lower)
    {
        if (!higher.IntersectsHorizontal(
                lower.MinimumX,
                lower.MinimumZ,
                lower.MaximumX,
                lower.MaximumZ) ||
            higher.MaximumY <
                lower.MinimumY ||
            higher.MinimumY >
                lower.MaximumY)
        {
            return false;
        }

        return higher.ReserveSpace ||
               higher.ConflictGroups.Any(group =>
                   lower.ConflictGroups.Contains(
                       group,
                       StringComparer.Ordinal));
    }

    private static bool SameCandidate(
        StructureCandidate left,
        StructureCandidate right) =>
        CandidateIdentity(
            left) ==
        CandidateIdentity(
            right);

    private static (
        int RuleIndex,
        int CellX,
        int CellZ)
        CandidateIdentity(
            StructureCandidate candidate) =>
        (
            candidate.RuleIndex,
            candidate.CellX,
            candidate.CellZ);

    private SurfaceSample SurfaceAt(
        int worldX,
        int worldZ)
    {
        var surface =
            _terrain.SampleBaseSurface(
                worldX,
                worldZ);

        return new SurfaceSample(
            surface.Biome,
            surface.BaseY);
    }

    private bool IsGeneratedFluidAbove(
        SurfaceSample surface)
    {
        if (!_generatedFluids
                .TryGetColumnBounds(
                    surface.Biome,
                    surface.BaseY,
                    out var minimumY,
                    out var maximumY))
        {
            return false;
        }

        var above =
            (long)surface.BaseY +
            1L;
        return above >= minimumY &&
               above <= maximumY;
    }

    private static int? CandidateAxis(
        int cell,
        int spacing,
        int jitter,
        ulong hash)
    {
        var offset =
            jitter == 0
                ? 0
                : checked(
                    (int)(
                        hash %
                        (ulong)(
                            jitter *
                            2 +
                            1)) -
                    jitter);
        var value =
            (long)cell *
            spacing +
            spacing /
            2L +
            offset;

        return value is
            < int.MinValue or
            > int.MaxValue
            ? null
            : (int)value;
    }

    private static long FloorDiv(
        long value,
        int divisor)
    {
        var quotient =
            Math.DivRem(
                value,
                divisor,
                out var remainder);
        return remainder < 0
            ? quotient - 1
            : quotient;
    }

    private readonly record struct StructureChunkKey(
        int X,
        int Z);

    private readonly record struct SurfaceSample(
        BiomeSample Biome,
        int BaseY);

    private sealed class RootRule
    {
        private RootRule(
            string biome,
            string reference,
            int spacing,
            float chance,
            int jitter,
            RuntimeStructure[] members,
            RuntimeStructureSet? set,
            int maximumHorizontalRadius)
        {
            Biome = biome;
            Reference = reference;
            Spacing = spacing;
            Chance = chance;
            Jitter = jitter;
            Members = members;
            Set = set;
            MaximumHorizontalRadius =
                maximumHorizontalRadius;
            var prefix =
                $"surface-structure/{biome}/{reference}/v1/";
            PresenceDomain =
                GenerationDomain.Named(
                    prefix +
                    "presence");
            VariantDomain =
                GenerationDomain.Named(
                    prefix +
                    "variant");
            RotationDomain =
                GenerationDomain.Named(
                    prefix +
                    "rotation");
            JitterXDomain =
                GenerationDomain.Named(
                    prefix +
                    "jitter-x");
            JitterZDomain =
                GenerationDomain.Named(
                    prefix +
                    "jitter-z");
        }

        public string Biome { get; }

        public string Reference { get; }

        public int Spacing { get; }

        public float Chance { get; }

        public int Jitter { get; }

        public RuntimeStructure[] Members { get; }

        public RuntimeStructureSet? Set { get; }

        public int MaximumHorizontalRadius { get; }

        public GenerationDomain PresenceDomain { get; }

        public GenerationDomain VariantDomain { get; }

        public GenerationDomain RotationDomain { get; }

        public GenerationDomain JitterXDomain { get; }

        public GenerationDomain JitterZDomain { get; }

        public static RootRule Create(
            DimensionGeneratedSurfaceStructureDefinition generated,
            StructureRegistry structures,
            StructureSetRegistry structureSets,
            BlockRegistry blocks,
            FluidRegistry fluids)
        {
            if (structureSets.TryGet(
                    generated.Structure,
                    out var setDefinition))
            {
                var set =
                    RuntimeStructureSet.Create(
                        setDefinition,
                        structures,
                        blocks,
                        fluids,
                        $"{generated.Biome}/{generated.Structure}");

                return new RootRule(
                    generated.Biome,
                    generated.Structure,
                    generated.Spacing,
                    generated.Chance,
                    generated.Jitter,
                    Array.Empty<RuntimeStructure>(),
                    set,
                    set.MaximumHorizontalRadius);
            }

            var members =
                structures
                    .ResolveReference(
                        generated.Structure)
                    .Select(
                        definition =>
                            new RuntimeStructure(
                                definition,
                                blocks,
                                fluids))
                    .ToArray();

            return new RootRule(
                generated.Biome,
                generated.Structure,
                generated.Spacing,
                generated.Chance,
                generated.Jitter,
                members,
                null,
                members.Max(
                    member =>
                        member.MaximumHorizontalRadius));
        }
    }

    private sealed record StructureCandidate(
        int RuleIndex,
        int CellX,
        int CellZ,
        int Priority,
        bool ReserveSpace,
        IReadOnlyList<string> ConflictGroups,
        IReadOnlyList<SurfaceStructurePlacement> Placements,
        int AnchorX,
        int AnchorZ,
        int MinimumX,
        int MaximumX,
        int MinimumY,
        int MaximumY,
        int MinimumZ,
        int MaximumZ)
    {
        public bool IntersectsHorizontal(
            int minimumX,
            int minimumZ,
            int maximumX,
            int maximumZ) =>
            MinimumX <= maximumX &&
            MaximumX >= minimumX &&
            MinimumZ <= maximumZ &&
            MaximumZ >= minimumZ;
    }

    private delegate SurfaceStructurePlacement?
        SetPieceResolver(
            RuntimeStructure member,
            StructureRotation rotation,
            int anchorX,
            int anchorZ);

    private sealed class RuntimeStructureSet
    {
        private const ulong ElementHashSalt =
            0x9e37_79b1_85eb_ca87UL;
        private const ulong InstanceHashSalt =
            0xc2b2_ae3d_27d4_eb4fUL;
        private const ulong AttemptHashSalt =
            0x1656_67b1_9e37_79f9UL;

        private readonly RuntimeSetElement[] _elements;

        private RuntimeStructureSet(
            StructureSetDefinition definition,
            RuntimeSetElement[] elements,
            int maximumHorizontalRadius)
        {
            Definition = definition;
            _elements = elements;
            MaximumHorizontalRadius =
                maximumHorizontalRadius;
        }

        public StructureSetDefinition Definition { get; }

        public int MaximumHorizontalRadius { get; }

        public IReadOnlyList<SurfaceStructurePlacement>?
            Resolve(
                ulong seed,
                int cellX,
                int cellZ,
                int rootX,
                int rootZ,
                SetPieceResolver resolvePiece)
        {
            var resolved =
                new List<SurfaceStructurePlacement>();
            var anchorsByElement =
                new Dictionary<
                    string,
                    List<(int X, int Z)>>(
                    StringComparer.Ordinal);
            var allAnchors =
                new List<(int X, int Z)>();

            for (var elementIndex = 0;
                 elementIndex < _elements.Length;
                 elementIndex++)
            {
                var element =
                    _elements[elementIndex];
                var elementHash =
                    Avalanche(
                        WorldGenerationEntropy.Sample2D(
                            seed,
                            element.Domain,
                            cellX,
                            cellZ) ^
                        (ulong)elementIndex *
                        ElementHashSalt);

                if (!ChanceSelects(
                        element.Definition.Chance,
                        elementHash))
                {
                    if (element.Definition.Required &&
                        element.Definition.Count.Minimum >
                        0)
                    {
                        return null;
                    }

                    anchorsByElement.Add(
                        element.Definition.Id,
                        []);
                    continue;
                }

                var targetCount =
                    ChooseCount(
                        element.Definition.Count,
                        elementHash);
                var placedForElement =
                    new List<(int X, int Z)>();

                for (var instance = 0;
                     instance < targetCount;
                     instance++)
                {
                    var instanceHash =
                        Avalanche(
                            elementHash ^
                            (ulong)(instance + 1) *
                            InstanceHashSalt);
                    var member =
                        element.Variants[
                            checked((int)(
                                instanceHash %
                                (ulong)element.Variants.Length))];
                    var rotation =
                        member.Definition.RotationForHash(
                            RotateLeft(
                                instanceHash,
                                23));
                    SurfaceStructurePlacement?
                        accepted =
                            null;

                    for (var attempt = 0;
                         attempt <
                             element.Definition
                                 .Placement
                                 .Attempts;
                         attempt++)
                    {
                        var attemptHash =
                            Avalanche(
                                instanceHash ^
                                (ulong)(attempt + 1) *
                                AttemptHashSalt);
                        var reference =
                            ResolveReferenceAnchor(
                                element.Definition
                                    .Placement
                                    .RelativeTo,
                                rootX,
                                rootZ,
                                allAnchors,
                                anchorsByElement,
                                attemptHash);

                        if (reference is null)
                        {
                            continue;
                        }

                        var offset =
                            AnnulusOffset(
                                RotateLeft(
                                    attemptHash,
                                    13),
                                element.Definition
                                    .Placement
                                    .MinimumDistance,
                                element.Definition
                                    .Placement
                                    .MaximumDistance);

                        if (offset is null)
                        {
                            continue;
                        }

                        var anchorX =
                            (long)reference.Value.X +
                            offset.Value.X;
                        var anchorZ =
                            (long)reference.Value.Z +
                            offset.Value.Z;

                        if (anchorX is <
                                int.MinValue or
                                > int.MaxValue ||
                            anchorZ is <
                                int.MinValue or
                                > int.MaxValue)
                        {
                            continue;
                        }

                        var candidateAnchor =
                            (
                                X: (int)anchorX,
                                Z: (int)anchorZ);

                        if (!SeparationSatisfied(
                                candidateAnchor,
                                allAnchors,
                                element.Definition
                                    .Placement
                                    .MinimumSeparation))
                        {
                            continue;
                        }

                        var placement =
                            resolvePiece(
                                member,
                                rotation,
                                candidateAnchor.X,
                                candidateAnchor.Z);

                        if (placement is null)
                        {
                            continue;
                        }

                        if (!element.Definition
                                .Placement
                                .AllowOverlap &&
                            resolved.Any(existing =>
                                RectanglesOverlap(
                                    placement.MinimumX,
                                    placement.MinimumZ,
                                    placement.MaximumX,
                                    placement.MaximumZ,
                                    existing.MinimumX,
                                    existing.MinimumZ,
                                    existing.MaximumX,
                                    existing.MaximumZ)))
                        {
                            continue;
                        }

                        accepted =
                            placement;
                        break;
                    }

                    if (accepted is null)
                    {
                        continue;
                    }

                    placedForElement.Add(
                        (
                            accepted.AnchorX,
                            accepted.AnchorZ));
                    allAnchors.Add(
                        (
                            accepted.AnchorX,
                            accepted.AnchorZ));
                    resolved.Add(
                        accepted);
                }

                if (element.Definition.Required &&
                    placedForElement.Count <
                        element.Definition.Count.Minimum)
                {
                    return null;
                }

                anchorsByElement.Add(
                    element.Definition.Id,
                    placedForElement);
            }

            return resolved.Count > 0
                ? resolved
                : null;
        }

        public static RuntimeStructureSet Create(
            StructureSetDefinition definition,
            StructureRegistry structures,
            BlockRegistry blocks,
            FluidRegistry fluids,
            string domainSuffix)
        {
            var anchorRadii =
                new Dictionary<string, int>(
                    StringComparer.Ordinal);
            var maximumPriorAnchorRadius =
                0;
            var maximumHorizontalRadius =
                0;
            var elements =
                new RuntimeSetElement[
                    definition.Elements.Count];

            for (var index = 0;
                 index < definition.Elements.Count;
                 index++)
            {
                var element =
                    definition.Elements[index];
                var variants =
                    structures
                        .ResolveReference(
                            element.Structure)
                        .Select(
                            structure =>
                                new RuntimeStructure(
                                    structure,
                                    blocks,
                                    fluids))
                        .ToArray();
                var baseRadius =
                    element.Placement.RelativeTo switch
                    {
                        "origin" =>
                            0,
                        "any" =>
                            maximumPriorAnchorRadius,
                        var relativeTo =>
                            anchorRadii[
                                relativeTo],
                    };
                var anchorRadius =
                    checked(
                        baseRadius +
                        element.Placement
                            .MaximumDistance);
                var memberRadius =
                    variants.Max(
                        variant =>
                            variant.MaximumHorizontalRadius);
                maximumHorizontalRadius =
                    Math.Max(
                        maximumHorizontalRadius,
                        checked(
                            anchorRadius +
                            memberRadius));
                anchorRadii.Add(
                    element.Id,
                    anchorRadius);
                maximumPriorAnchorRadius =
                    Math.Max(
                        maximumPriorAnchorRadius,
                        anchorRadius);
                elements[index] =
                    new RuntimeSetElement(
                        element,
                        variants,
                        GenerationDomain.Named(
                            $"structure/set/element/v1/{domainSuffix}/{element.Id}"));
            }

            return new RuntimeStructureSet(
                definition,
                elements,
                maximumHorizontalRadius);
        }

        private static int ChooseCount(
            StructureSetCountDefinition count,
            ulong hash)
        {
            if (count.Minimum ==
                count.Maximum)
            {
                return count.Minimum;
            }

            var span =
                checked(
                    count.Maximum -
                    count.Minimum +
                    1);

            return checked(
                count.Minimum +
                (int)(
                    Avalanche(
                        RotateLeft(
                            hash,
                            31)) %
                    (ulong)span));
        }

        private static (int X, int Z)?
            ResolveReferenceAnchor(
                string relativeTo,
                int rootX,
                int rootZ,
                IReadOnlyList<(int X, int Z)> allAnchors,
                IReadOnlyDictionary<
                    string,
                    List<(int X, int Z)>> anchorsByElement,
                ulong hash)
        {
            if (relativeTo ==
                "origin")
            {
                return (
                    rootX,
                    rootZ);
            }

            if (relativeTo ==
                "any")
            {
                return allAnchors.Count == 0
                    ? (
                        rootX,
                        rootZ)
                    : allAnchors[
                        checked((int)(
                            hash %
                            (ulong)allAnchors.Count))];
            }

            if (!anchorsByElement.TryGetValue(
                    relativeTo,
                    out var anchors) ||
                anchors.Count == 0)
            {
                return null;
            }

            return anchors[
                checked((int)(
                    hash %
                    (ulong)anchors.Count))];
        }

        private static (int X, int Z)?
            AnnulusOffset(
                ulong hash,
                int minimumDistance,
                int maximumDistance)
        {
            if (maximumDistance == 0)
            {
                return minimumDistance == 0
                    ? (
                        0,
                        0)
                    : null;
            }

            var diameter =
                (long)maximumDistance *
                2L +
                1L;
            var x =
                (long)(
                    hash %
                    (ulong)diameter) -
                maximumDistance;
            var zHash =
                Avalanche(
                    RotateLeft(
                        hash,
                        29));
            var z =
                (long)(
                    zHash %
                    (ulong)diameter) -
                maximumDistance;
            var squared =
                x *
                x +
                z *
                z;
            var minimumSquared =
                (long)minimumDistance *
                minimumDistance;
            var maximumSquared =
                (long)maximumDistance *
                maximumDistance;

            if (squared <
                    minimumSquared ||
                squared >
                    maximumSquared)
            {
                return null;
            }

            return (
                checked((int)x),
                checked((int)z));
        }

        private static bool SeparationSatisfied(
            (int X, int Z) anchor,
            IReadOnlyList<(int X, int Z)> existing,
            int minimumSeparation)
        {
            if (minimumSeparation == 0)
            {
                return true;
            }

            var minimumSquared =
                (long)minimumSeparation *
                minimumSeparation;

            return existing.All(other =>
            {
                var dx =
                    (long)anchor.X -
                    other.X;
                var dz =
                    (long)anchor.Z -
                    other.Z;

                return dx *
                       dx +
                       dz *
                       dz >=
                       minimumSquared;
            });
        }

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

        private static bool ChanceSelects(
            float chance,
            ulong value) =>
            chance >= 1f ||
            chance > 0f &&
            WorldGenerationEntropy.Unit(
                value) <
            chance;

        private static ulong RotateLeft(
            ulong value,
            int count) =>
            value <<
                count |
            value >>
                (64 -
                 count);

        private static ulong Avalanche(
            ulong value)
        {
            value ^=
                value >>
                30;
            value *=
                0xbf58_476d_1ce4_e5b9UL;
            value ^=
                value >>
                27;
            value *=
                0x94d0_49bb_1331_11ebUL;
            return value ^
                   value >>
                       31;
        }

        private sealed record RuntimeSetElement(
            StructureSetElementDefinition Definition,
            RuntimeStructure[] Variants,
            GenerationDomain Domain);
    }

    private sealed record RuntimeProximityRule(
        StructureProximityMode Mode,
        int MinimumDistance,
        int MaximumDistance,
        BlockRuntimeId Block,
        FluidRuntimeId Fluid);

    private sealed class RuntimeStructure
    {
        private readonly RuntimeVoxel[] _voxels;
        private readonly (int X, int Z)[] _footprint;
        private readonly (int X, int Z)[] _supports;

        public RuntimeStructure(
            StructureDefinition definition,
            BlockRegistry blocks,
            FluidRegistry fluids)
        {
            Definition = definition;
            _voxels =
                definition.Voxels
                    .Select(
                        voxel =>
                        {
                            var block =
                                blocks.GetId(
                                    voxel.Block);
                            return new RuntimeVoxel(
                                voxel.X,
                                voxel.Y,
                                voxel.Z,
                                new VoxelCell(
                                    block,
                                    orientation:
                                        voxel.Orientation));
                        })
                    .ToArray();
            _footprint =
                _voxels
                    .Select(
                        voxel =>
                            (
                                voxel.X,
                                voxel.Z))
                    .Distinct()
                    .OrderBy(
                        offset =>
                            offset.X)
                    .ThenBy(
                        offset =>
                            offset.Z)
                    .ToArray();
            _supports =
                _voxels
                    .Where(
                        voxel =>
                            voxel.Y ==
                            definition.MinimumY)
                    .Select(
                        voxel =>
                            (
                                voxel.X,
                                voxel.Z))
                    .Distinct()
                    .OrderBy(
                        offset =>
                            offset.X)
                    .ThenBy(
                        offset =>
                            offset.Z)
                    .ToArray();
            AllowedGroundBlocks =
                definition.Restrictions
                    .GroundBlocks
                    .Select(
                        blocks.GetId)
                    .ToHashSet();
            Proximity =
                definition.Restrictions
                    .Proximity
                    .Select(rule =>
                        new RuntimeProximityRule(
                            rule.Mode,
                            rule.MinDistance ??
                            0,
                            rule.MaxDistance,
                            rule.Target.Block is
                                { } block
                                ? blocks.GetId(
                                    block)
                                : BlockRuntimeId.Air,
                            rule.Target.Fluid is
                                { } fluid
                                ? fluids.GetId(
                                    fluid)
                                : FluidRuntimeId.None))
                    .ToArray();
            var maximumRadius =
                _footprint.Max(
                    offset =>
                        Math.Max(
                            Math.Abs(
                                (long)offset.X),
                            Math.Abs(
                                (long)offset.Z)));
            MaximumHorizontalRadius =
                maximumRadius >
                int.MaxValue
                    ? int.MaxValue
                    : checked(
                        (int)maximumRadius);
        }

        public StructureDefinition Definition { get; }

        public HashSet<BlockRuntimeId> AllowedGroundBlocks { get; }

        public RuntimeProximityRule[] Proximity { get; }

        public int MaximumHorizontalRadius { get; }

        public IReadOnlyList<(int X, int Z)>
            HorizontalFootprint(
                StructureRotation rotation) =>
            RotateOffsets(
                _footprint,
                rotation);

        public IReadOnlyList<(int X, int Z)>
            SupportOffsets(
                StructureRotation rotation) =>
            RotateOffsets(
                _supports,
                rotation);

        private static IReadOnlyList<(int X, int Z)>
            RotateOffsets(
                IEnumerable<(int X, int Z)> offsets,
                StructureRotation rotation) =>
            offsets
                .Select(
                    offset =>
                    {
                        var rotated =
                            StructureDefinition.RotateOffset(
                                rotation,
                                offset.X,
                                0,
                                offset.Z);
                        return (
                            rotated.X,
                            rotated.Z);
                    })
                .Distinct()
                .ToArray();

        public SurfaceStructurePlacement Place(
            string reference,
            int anchorX,
            int anchorY,
            int anchorZ,
            StructureRotation rotation)
        {
            var voxels =
                new PlacedStructureVoxel[
                    _voxels.Length];
            var minimumX =
                int.MaxValue;
            var maximumX =
                int.MinValue;
            var minimumY =
                int.MaxValue;
            var maximumY =
                int.MinValue;
            var minimumZ =
                int.MaxValue;
            var maximumZ =
                int.MinValue;

            for (var index = 0;
                 index < _voxels.Length;
                 index++)
            {
                var voxel =
                    _voxels[index];
                var rotated =
                    StructureDefinition.RotateOffset(
                        rotation,
                        voxel.X,
                        voxel.Y,
                        voxel.Z);
                var x =
                    checked(
                        anchorX +
                        rotated.X);
                var y =
                    checked(
                        anchorY +
                        rotated.Y);
                var z =
                    checked(
                        anchorZ +
                        rotated.Z);
                var cell =
                    voxel.Cell.WithOrientation(
                        StructureDefinition
                            .RotateOrientation(
                                rotation,
                                voxel.Cell.Orientation));

                voxels[index] =
                    new PlacedStructureVoxel(
                        x,
                        y,
                        z,
                        cell);
                minimumX =
                    Math.Min(
                        minimumX,
                        x);
                maximumX =
                    Math.Max(
                        maximumX,
                        x);
                minimumY =
                    Math.Min(
                        minimumY,
                        y);
                maximumY =
                    Math.Max(
                        maximumY,
                        y);
                minimumZ =
                    Math.Min(
                        minimumZ,
                        z);
                maximumZ =
                    Math.Max(
                        maximumZ,
                        z);
            }

            return new SurfaceStructurePlacement(
                reference,
                Definition.Id,
                anchorX,
                anchorY,
                anchorZ,
                rotation,
                Definition.Generation,
                voxels,
                minimumX,
                maximumX,
                minimumY,
                maximumY,
                minimumZ,
                maximumZ);
        }

        private readonly record struct RuntimeVoxel(
            int X,
            int Y,
            int Z,
            VoxelCell Cell);
    }
}

internal sealed class SurfaceStructurePlacement
{
    public SurfaceStructurePlacement(
        string reference,
        string structureId,
        int anchorX,
        int anchorY,
        int anchorZ,
        StructureRotation rotation,
        StructureGenerationDefinition generation,
        IReadOnlyList<PlacedStructureVoxel> voxels,
        int minimumX,
        int maximumX,
        int minimumY,
        int maximumY,
        int minimumZ,
        int maximumZ)
    {
        Reference = reference;
        StructureId = structureId;
        AnchorX = anchorX;
        AnchorY = anchorY;
        AnchorZ = anchorZ;
        Rotation = rotation;
        Generation = generation;
        Voxels = voxels;
        MinimumX = minimumX;
        MaximumX = maximumX;
        MinimumY = minimumY;
        MaximumY = maximumY;
        MinimumZ = minimumZ;
        MaximumZ = maximumZ;
    }

    public string Reference { get; }

    public string StructureId { get; }

    public int AnchorX { get; }

    public int AnchorY { get; }

    public int AnchorZ { get; }

    public StructureRotation Rotation { get; }

    public StructureGenerationDefinition Generation { get; }

    public IReadOnlyList<PlacedStructureVoxel> Voxels { get; }

    public int MinimumX { get; }

    public int MaximumX { get; }

    public int MinimumY { get; }

    public int MaximumY { get; }

    public int MinimumZ { get; }

    public int MaximumZ { get; }

    public bool IntersectsHorizontal(
        int minimumX,
        int minimumZ,
        int maximumX,
        int maximumZ) =>
        MinimumX <= maximumX &&
        MaximumX >= minimumX &&
        MinimumZ <= maximumZ &&
        MaximumZ >= minimumZ;

    public static int CompareDeterministically(
        SurfaceStructurePlacement left,
        SurfaceStructurePlacement right)
    {
        var byReference =
            string.CompareOrdinal(
                left.Reference,
                right.Reference);
        if (byReference != 0)
        {
            return byReference;
        }

        var byStructure =
            string.CompareOrdinal(
                left.StructureId,
                right.StructureId);
        if (byStructure != 0)
        {
            return byStructure;
        }

        var byX =
            left.AnchorX.CompareTo(
                right.AnchorX);
        if (byX != 0)
        {
            return byX;
        }

        var byZ =
            left.AnchorZ.CompareTo(
                right.AnchorZ);
        return byZ != 0
            ? byZ
            : left.AnchorY.CompareTo(
                right.AnchorY);
    }
}

internal readonly record struct PlacedStructureVoxel(
    int X,
    int Y,
    int Z,
    VoxelCell Cell);
