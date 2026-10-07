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
    private readonly SurfaceTerrainColumnCache _columns;
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
        BlockRegistry blocks,
        FluidRegistry fluids,
        BiomeField biomes,
        SurfaceTerrainColumnCache columns,
        BiomeSurfaceMaterialField materials,
        GeneratedFluidField generatedFluids)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            structures);
        ArgumentNullException.ThrowIfNull(
            blocks);
        ArgumentNullException.ThrowIfNull(
            fluids);
        _biomes =
            biomes ??
            throw new ArgumentNullException(
                nameof(biomes));
        _columns =
            columns ??
            throw new ArgumentNullException(
                nameof(columns));
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
                .Select(
                    candidate =>
                        candidate.Placement)
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
                        !candidate.Placement
                            .IntersectsHorizontal(
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
                         candidate.Placement.MinimumX,
                         candidate.Placement.MinimumZ,
                         candidate.Placement.MaximumX,
                         candidate.Placement.MaximumZ))
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
                    anchorX.Value +
                    offset.X);
            var z =
                checked(
                    anchorZ.Value +
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
                        anchorX.Value +
                        offset.X);
                var z =
                    checked(
                        anchorZ.Value +
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

        if (member.Proximity.Any(rule =>
                !ProximityRuleSatisfied(
                    anchorX.Value,
                    anchorZ.Value,
                    rule)))
        {
            return null;
        }

        var placement =
            member.Place(
                rule.Reference,
                anchorX.Value,
                anchorSurface.BaseY,
                anchorZ.Value,
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

        return new StructureCandidate(
            ruleIndex,
            cellX,
            cellZ,
            member.Definition.Priority,
            member.Definition.Generation.ReserveSpace,
            member.Definition.ConflictGroups,
            placement);
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
            left.Placement.AnchorX.CompareTo(
                right.Placement.AnchorX);
        if (x != 0)
        {
            return x < 0;
        }

        var z =
            left.Placement.AnchorZ.CompareTo(
                right.Placement.AnchorZ);
        if (z != 0)
        {
            return z < 0;
        }

        return left.Placement.MinimumY <
               right.Placement.MinimumY;
    }

    private static bool CandidatesConflict(
        StructureCandidate higher,
        StructureCandidate lower)
    {
        var left =
            higher.Placement;
        var right =
            lower.Placement;

        if (!left.IntersectsHorizontal(
                right.MinimumX,
                right.MinimumZ,
                right.MaximumX,
                right.MaximumZ) ||
            left.MaximumY <
                right.MinimumY ||
            left.MinimumY >
                right.MaximumY)
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
        var address =
            VoxelCoordinates.FromWorld(
                worldX,
                0,
                worldZ);
        var column =
            _columns.Get(
                address.Chunk.X,
                address.Chunk.Z);
        var local =
            address.Local;

        return new SurfaceSample(
            column.BiomeAt(
                local.X,
                local.Z),
            column.BaseHeightAt(
                local.X,
                local.Z));
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
            RuntimeStructure[] members)
        {
            Biome = biome;
            Reference = reference;
            Spacing = spacing;
            Chance = chance;
            Jitter = jitter;
            Members = members;
            MaximumHorizontalRadius =
                members.Max(
                    member =>
                        member
                            .MaximumHorizontalRadius);
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

        public int MaximumHorizontalRadius { get; }

        public GenerationDomain PresenceDomain { get; }

        public GenerationDomain VariantDomain { get; }

        public GenerationDomain RotationDomain { get; }

        public GenerationDomain JitterXDomain { get; }

        public GenerationDomain JitterZDomain { get; }

        public static RootRule Create(
            DimensionGeneratedSurfaceStructureDefinition generated,
            StructureRegistry structures,
            BlockRegistry blocks,
            FluidRegistry fluids)
        {
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
                members);
        }
    }

    private sealed record StructureCandidate(
        int RuleIndex,
        int CellX,
        int CellZ,
        int Priority,
        bool ReserveSpace,
        IReadOnlyList<string> ConflictGroups,
        SurfaceStructurePlacement Placement);

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
