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
        BiomeField biomes,
        SurfaceTerrainColumnCache columns,
        GeneratedFluidField generatedFluids)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            structures);
        ArgumentNullException.ThrowIfNull(
            blocks);
        _biomes =
            biomes ??
            throw new ArgumentNullException(
                nameof(biomes));
        _columns =
            columns ??
            throw new ArgumentNullException(
                nameof(columns));
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
                            blocks))
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
        var placements =
            new List<
                SurfaceStructurePlacement>();

        foreach (var rule in
                 _rules)
        {
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
                    (long)originX -
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
                    (long)originZ -
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
                            rule,
                            (int)cellX,
                            (int)cellZ);

                    if (candidate is null ||
                        !candidate
                            .IntersectsHorizontal(
                                originX,
                                originZ,
                                maximumX,
                                maximumZ))
                    {
                        continue;
                    }

                    placements.Add(
                        candidate);
                }
            }
        }

        placements.Sort(
            SurfaceStructurePlacement
                .CompareDeterministically);
        return Array.AsReadOnly(
            placements.ToArray());
    }

    private SurfaceStructurePlacement?
        ResolveCandidate(
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

        return placement;
    }

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
            BlockRegistry blocks)
        {
            var members =
                structures
                    .ResolveReference(
                        generated.Structure)
                    .Select(
                        definition =>
                            new RuntimeStructure(
                                definition,
                                blocks))
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

    private sealed class RuntimeStructure
    {
        private readonly RuntimeVoxel[] _voxels;
        private readonly (int X, int Z)[] _footprint;

        public RuntimeStructure(
            StructureDefinition definition,
            BlockRegistry blocks)
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
            MaximumHorizontalRadius =
                _footprint.Max(
                    offset =>
                        Math.Max(
                            Math.Abs(
                                (long)offset.X),
                            Math.Abs(
                                (long)offset.Z))) >
                int.MaxValue
                    ? int.MaxValue
                    : checked((int)_footprint.Max(
                        offset =>
                            Math.Max(
                                Math.Abs(
                                    (long)offset.X),
                                Math.Abs(
                                    (long)offset.Z))));
        }

        public StructureDefinition Definition { get; }

        public int MaximumHorizontalRadius { get; }

        public IReadOnlyList<(int X, int Z)>
            HorizontalFootprint(
                StructureRotation rotation)
        {
            if (rotation is
                StructureRotation.Degrees0 or
                StructureRotation.Degrees180)
            {
                return _footprint
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
            }

            return _footprint
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
        }

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
