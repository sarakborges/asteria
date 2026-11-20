using System.Collections.Immutable;

namespace Asteria.Core.World;

/// <summary>
/// Immutable, bounded candidate resolution for authored formations anchored
/// to carved cave faces or additive island surfaces. Placement never writes
/// chunks or infers ownership from the biome's name.
/// </summary>
public sealed class VolumeStructureField
{
    private readonly ulong _seed;
    private readonly SurfaceTerrainField _terrain;
    private readonly VolumeBiomeField _volumes;
    private readonly BiomeSurfaceMaterialField _materials;
    private readonly Rule[] _rules;
    // Repeated vertical chunks of one X/Z area ask about the same
    // anchors. This is an immutable, thread-safe bounded derivation,
    // never a second authority for terrain or volume occupancy.
    // Candidate anchor searches are shared across vertical section cursors.
    // Each request advances a bounded support-Y slice; no worker performs the
    // full authored vertical scan synchronously.
    private readonly BoundedMemoCache<CandidateKey, AnchorSearch> _anchorSearches =
        new(4096);
    // One resumable horizontal-column product replaces the repeated X/Z candidate
    // scan previously performed independently by every vertical section cursor.
    // It is bounded and non-authoritative: eviction only causes deterministic recomputation.
    private readonly BoundedMemoCache<ColumnKey, ColumnAnchorProduct> _columnAnchorProducts =
        new(4096);

    public VolumeStructureField(
        ulong seed,
        IEnumerable<BiomeDefinition> volumeBiomes,
        StructureRegistry structures,
        BlockRegistry blocks,
        SurfaceTerrainField terrain,
        VolumeBiomeField volumes,
        BiomeSurfaceMaterialField materials,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(volumeBiomes);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(blocks);
        _seed = seed;
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));

        // Disabled generation must not resolve optional authored structure
        // families. Flat/Void and spawnStructures=false also work with
        // StructureRegistry.Empty, just like surface structure generation.
        if (!enabled)
        {
            _rules = [];
            return;
        }

        var rules = new List<Rule>();
        foreach (var biome in volumeBiomes.OrderBy(b => b.Id, StringComparer.Ordinal))
        foreach (var authored in biome.VolumeStructures
                     .OrderBy(rule => rule.Structure, StringComparer.Ordinal))
        {
            var variants = structures.ResolveReference(authored.Structure)
                .Select(definition => CreateVariant(definition, blocks))
                .ToArray();
            if (variants.Length == 0)
                throw new ArgumentException(
                    $"Volume structure group {authored.Structure} has no variants.");

            // An author may not attach a downward-facing blueprint to
            // an upward-facing terrain support (or vice versa).
            var direction = authored.SupportSurface ==
                DecorationSupportSurface.Floor ? 1 : -1;
            if (variants.Any(v => Math.Sign(v.MinY) != direction ||
                                  Math.Sign(v.MaxY) != direction))
                throw new ArgumentException(
                    $"Volume structure {authored.Structure} blueprint direction does not match its supportSurface.");

            var maximumRadius = variants.Max(variant => variant.Radius);
            if (maximumRadius > 6 || maximumRadius * 2 >= authored.Spacing)
                throw new ArgumentException(
                    $"Volume structure {authored.Structure} needs a compact (<7) horizontal footprint.");
            rules.Add(new Rule(
                rules.Count,
                biome.Id,
                biome.VolumeLayout!.Placement,
                authored,
                variants,
                maximumRadius,
                variants.Min(variant => variant.MinY),
                variants.Max(variant => variant.MaxY),
                GenerationDomain.Named(
                    $"worldgen/volume-structure/{biome.Id}/{authored.Structure}/v1")));
        }
        _rules = rules.ToArray();
    }

    public bool HasRules => _rules.Length > 0;

    /// <summary>
    /// Returns world-space voxels only for candidates whose immutable
    /// template fits real density voids. All candidate decisions are made in
    /// world coordinates, including anchors outside the requested chunk.
    /// </summary>
    public ChunkCursor CreateChunkCursor(
        ChunkCoord coord,
        ChunkGenerationAccessRegion? accessRegion = null)
    {
        if (accessRegion is not null &&
            (accessRegion.TargetSection != coord ||
             accessRegion.StepId != ChunkGenerationStepId.VolumeStructures))
        {
            throw new ArgumentException(
                "Volume-structure access region must target this section and the VolumeStructures step.",
                nameof(accessRegion));
        }

        if (coord.Y < 0)
            throw new ArgumentOutOfRangeException(
                nameof(coord), "Volume structures cannot target a section below the world floor.");

        var columnProduct = _columnAnchorProducts.GetOrAdd(
            new ColumnKey(coord.X, coord.Z),
            () => new ColumnAnchorProduct(
                this,
                coord.X,
                coord.Z,
                CreateColumnAccessRegion(coord)));
        return new ChunkCursor(this, coord, columnProduct);
    }

    /// <summary>
    /// Creates the access boundary for resolving the immutable volume-structure
    /// anchor product of one X/Z column. The vertical allowance is derived from
    /// the active authored support range and template reach, not from the
    /// requesting section's local +/- section halo.
    /// </summary>
    public ChunkGenerationAccessRegion CreateColumnAccessRegion(ChunkCoord coord)
    {
        if (coord.Y < 0)
            throw new ArgumentOutOfRangeException(
                nameof(coord), "Volume structures cannot target a section below the world floor.");

        var maximumReadWorldY = 0L;
        foreach (var rule in _rules)
        {
            var templateReach = Math.Max(0L, (long)rule.MaxOffsetY);
            var maximumRuleReadY =
                (long)rule.Definition.MaxY + templateReach + 1L;
            maximumReadWorldY = Math.Max(maximumReadWorldY, maximumRuleReadY);
        }

        var maximumSectionOffset = (int)Math.Min(
            int.MaxValue,
            maximumReadWorldY / Chunk.Size);
        var readEnvelope = new ChunkGenerationDependencyEnvelope(
            horizontalRadius: 1,
            minimumSectionOffset: 0,
            maximumSectionOffset: maximumSectionOffset);

        return new ChunkGenerationAccessRegion(
            AsteriaChunkGenerationPlan.Default,
            new ChunkCoord(coord.X, 0, coord.Z),
            ChunkGenerationStepId.VolumeStructurePlacements,
            readEnvelope);
    }

    /// <summary>
    /// Compatibility/reference query. Production materialization uses
    /// <see cref="CreateChunkCursor"/> so candidate evaluation and voxel
    /// application can be interleaved with other generation work.
    /// </summary>
    public IReadOnlyList<VolumeStructureVoxel> VoxelsForChunk(ChunkCoord coord)
    {
        var results = new List<VolumeStructureVoxel>();
        var cursor = CreateChunkCursor(coord);
        Span<VolumeStructureVoxel> batch = stackalloc VolumeStructureVoxel[64];
        while (!cursor.IsComplete)
        {
            _ = cursor.Advance(
                batch,
                maximumCandidateEvaluations: 8,
                out var written);
            for (var index = 0; index < written; index++)
                results.Add(batch[index]);
        }

        return results;
    }

    /// <summary>
    /// Resumable section rasterizer over one completed column anchor product.
    /// Candidate discovery and support-Y resolution happen once per X/Z column;
    /// this cursor only selects placements intersecting its 16³ section.
    /// </summary>
    public sealed class ChunkCursor
    {
        private readonly ColumnAnchorProduct _columnProduct;
        private readonly int _originX;
        private readonly int _originY;
        private readonly int _originZ;
        private readonly long _right;
        private readonly long _top;
        private readonly long _back;
        private int _nextAnchorIndex;
        private Variant? _activeVariant;
        private int _activeVoxelIndex;
        private int _activeWorldX;
        private int _activeWorldZ;
        private int _activeSupportY;

        internal ChunkCursor(
            VolumeStructureField owner,
            ChunkCoord coord,
            ColumnAnchorProduct columnProduct)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _columnProduct = columnProduct ??
                throw new ArgumentNullException(nameof(columnProduct));
            var origin = VoxelCoordinates.ChunkOrigin(coord);
            _originX = origin.X;
            _originY = origin.Y;
            _originZ = origin.Z;
            _right = (long)origin.X + Chunk.Size - 1;
            _top = (long)origin.Y + Chunk.Size - 1;
            _back = (long)origin.Z + Chunk.Size - 1;
            IsComplete = !owner.HasRules;
        }

        public bool IsComplete { get; private set; }

        /// <summary>
        /// Number of bounded support-search slices advanced by this section
        /// cursor while helping produce its shared column product.
        /// </summary>
        public int AnchorSearchSlices { get; private set; }

        /// <summary>
        /// Writes at most <paramref name="voxels"/>.Length intersecting voxels.
        /// Column product discovery and support-Y scanning are separately
        /// budgeted; a section never repeats the column's candidate scan.
        /// </summary>
        public bool Advance(
            Span<VolumeStructureVoxel> voxels,
            int maximumCandidateEvaluations,
            out int written,
            int maximumAnchorSupportSamples = 16)
        {
            if (voxels.IsEmpty)
                throw new ArgumentException(
                    "A volume-structure output buffer cannot be empty.",
                    nameof(voxels));
            if (maximumCandidateEvaluations <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCandidateEvaluations));
            if (maximumAnchorSupportSamples <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumAnchorSupportSamples));

            written = 0;
            if (IsComplete)
                return true;

            if (!_columnProduct.IsComplete)
            {
                var productCompleted = _columnProduct.Advance(
                    maximumCandidateEvaluations,
                    maximumAnchorSupportSamples,
                    out var anchorSearchSlices);
                AnchorSearchSlices += anchorSearchSlices;
                if (!productCompleted)
                    return false;
            }

            var anchors = _columnProduct.Anchors;
            while (!IsComplete && written < voxels.Length)
            {
                if (_activeVariant is { } activeVariant)
                {
                    while (_activeVoxelIndex < activeVariant.Voxels.Count &&
                           written < voxels.Length)
                    {
                        var relative = activeVariant.Voxels[_activeVoxelIndex++];
                        var voxelWorldX = (long)_activeWorldX + relative.X;
                        var voxelWorldY = (long)_activeSupportY + relative.Y;
                        var voxelWorldZ = (long)_activeWorldZ + relative.Z;
                        if (voxelWorldX < _originX || voxelWorldX > _right ||
                            voxelWorldY < _originY || voxelWorldY > _top ||
                            voxelWorldZ < _originZ || voxelWorldZ > _back)
                            continue;

                        voxels[written++] = new VolumeStructureVoxel(
                            (int)voxelWorldX,
                            (int)voxelWorldY,
                            (int)voxelWorldZ,
                            relative.Block);
                    }

                    if (_activeVoxelIndex >= activeVariant.Voxels.Count)
                    {
                        _activeVariant = null;
                        _activeVoxelIndex = 0;
                    }
                    continue;
                }

                if (_nextAnchorIndex >= anchors.Length)
                {
                    IsComplete = true;
                    break;
                }

                var anchor = anchors[_nextAnchorIndex++];
                if ((long)anchor.SupportY + anchor.Variant.MinY > _top ||
                    (long)anchor.SupportY + anchor.Variant.MaxY < _originY)
                    continue;

                _activeVariant = anchor.Variant;
                _activeVoxelIndex = 0;
                _activeWorldX = anchor.WorldX;
                _activeWorldZ = anchor.WorldZ;
                _activeSupportY = anchor.SupportY;
            }

            return IsComplete;
        }
    }

    /// <summary>
    /// Mutable only while incrementally resolving one X/Z column. Once complete,
    /// its placements are frozen in deterministic order and shared read-only by
    /// all vertical section cursors in that column.
    /// </summary>
    internal sealed class ColumnAnchorProduct
    {
        private readonly object _gate = new();
        private readonly VolumeStructureField _owner;
        private readonly ChunkGenerationAccessRegion _accessRegion;
        private readonly int _originX;
        private readonly int _originZ;
        private readonly long _right;
        private readonly long _back;
        private readonly List<ResolvedAnchor> _buildingAnchors = [];
        private ImmutableArray<ResolvedAnchor> _anchors =
            ImmutableArray<ResolvedAnchor>.Empty;
        private int _ruleIndex = -1;
        private Rule? _rule;
        private int _firstX;
        private int _lastX;
        private int _firstZ;
        private int _lastZ;
        private int _nextX;
        private int _nextZ;
        private AnchorSearch? _pendingAnchorSearch;
        private Variant? _pendingAnchorVariant;
        private int _pendingAnchorWorldX;
        private int _pendingAnchorWorldZ;
        private bool _completed;

        public ColumnAnchorProduct(
            VolumeStructureField owner,
            int columnX,
            int columnZ,
            ChunkGenerationAccessRegion accessRegion)
        {
            _owner = owner;
            _accessRegion = accessRegion ??
                throw new ArgumentNullException(nameof(accessRegion));
            var origin = VoxelCoordinates.ChunkOrigin(
                new ChunkCoord(columnX, 0, columnZ));
            _originX = origin.X;
            _originZ = origin.Z;
            _right = (long)origin.X + Chunk.Size - 1;
            _back = (long)origin.Z + Chunk.Size - 1;
            _completed = !owner.HasRules;
        }

        public bool IsComplete
        {
            get
            {
                lock (_gate)
                    return _completed;
            }
        }

        public ImmutableArray<ResolvedAnchor> Anchors
        {
            get
            {
                lock (_gate)
                {
                    if (!_completed)
                        throw new InvalidOperationException(
                            "A volume-structure column product cannot be consumed before completion.");
                    return _anchors;
                }
            }
        }

        public bool Advance(
            int maximumCandidateEvaluations,
            int maximumAnchorSupportSamples,
            out int anchorSearchSlices)
        {
            if (maximumCandidateEvaluations <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCandidateEvaluations));
            if (maximumAnchorSupportSamples <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumAnchorSupportSamples));

            lock (_gate)
            {
                anchorSearchSlices = 0;
                if (_completed)
                    return true;

                var evaluatedCandidates = 0;
                while (true)
                {
                    if (_pendingAnchorSearch is { } pendingSearch)
                    {
                        anchorSearchSlices++;
                        if (!pendingSearch.Advance(
                                _owner,
                                _accessRegion,
                                maximumAnchorSupportSamples))
                            return false;

                        var pendingVariant = _pendingAnchorVariant ??
                            throw new InvalidOperationException(
                                "A pending volume-structure anchor has no selected variant.");
                        if (pendingSearch.SupportY is { } supportY)
                        {
                            _buildingAnchors.Add(new ResolvedAnchor(
                                pendingVariant,
                                _pendingAnchorWorldX,
                                _pendingAnchorWorldZ,
                                supportY));
                        }

                        _pendingAnchorSearch = null;
                        _pendingAnchorVariant = null;
                        continue;
                    }

                    if (evaluatedCandidates >= maximumCandidateEvaluations)
                        return false;

                    if (!TryNextCandidate(
                            out var rule,
                            out var cellX,
                            out var cellZ))
                    {
                        _anchors = _buildingAnchors.ToImmutableArray();
                        _buildingAnchors.Clear();
                        _completed = true;
                        return true;
                    }

                    evaluatedCandidates++;
                    var hash = WorldGenerationEntropy.Sample2D(
                        _owner._seed, rule.Domain, cellX, cellZ);
                    if (WorldGenerationEntropy.Unit(hash) >= rule.Definition.Chance)
                        continue;

                    // Independent deterministic hashes avoid global/random state.
                    var sampleX = WorldGenerationEntropy.Sample2D(
                        _owner._seed, rule.Domain, cellX + 113, cellZ);
                    var sampleZ = WorldGenerationEntropy.Sample2D(
                        _owner._seed, rule.Domain, cellX, cellZ + 317);
                    var spacing = rule.Definition.Spacing;
                    var jitter = spacing / 4;
                    var worldX = (long)cellX * spacing + spacing / 2 +
                                 (long)(sampleX % (uint)(2 * jitter + 1)) - jitter;
                    var worldZ = (long)cellZ * spacing + spacing / 2 +
                                 (long)(sampleZ % (uint)(2 * jitter + 1)) - jitter;
                    if (worldX is < int.MinValue or > int.MaxValue ||
                        worldZ is < int.MinValue or > int.MaxValue)
                        continue;
                    if (worldX + rule.Radius < _originX ||
                        worldX - rule.Radius > _right ||
                        worldZ + rule.Radius < _originZ ||
                        worldZ - rule.Radius > _back)
                        continue;

                    var variant = rule.Variants[
                        (int)(WorldGenerationEntropy.Sample2D(
                            _owner._seed, rule.Domain, cellX + 509, cellZ + 1021) %
                            (uint)rule.Variants.Count)];
                    _pendingAnchorSearch = _owner._anchorSearches.GetOrAdd(
                        new CandidateKey(rule.Index, cellX, cellZ),
                        () => new AnchorSearch(
                            rule, variant, (int)worldX, (int)worldZ));
                    _pendingAnchorVariant = variant;
                    _pendingAnchorWorldX = (int)worldX;
                    _pendingAnchorWorldZ = (int)worldZ;
                }
            }
        }

        private bool TryNextCandidate(
            out Rule rule,
            out int cellX,
            out int cellZ)
        {
            while (true)
            {
                if (_rule is { } currentRule && _nextZ <= _lastZ)
                {
                    rule = currentRule;
                    cellX = _nextX;
                    cellZ = _nextZ;
                    _nextX++;
                    if (_nextX > _lastX)
                    {
                        _nextX = _firstX;
                        _nextZ++;
                    }
                    return true;
                }

                _rule = null;
                _ruleIndex++;
                if (_ruleIndex >= _owner._rules.Length)
                {
                    rule = null!;
                    cellX = 0;
                    cellZ = 0;
                    return false;
                }

                var candidateRule = _owner._rules[_ruleIndex];
                var spacing = candidateRule.Definition.Spacing;
                var radius = candidateRule.Radius + spacing / 4;
                _firstX = Cell((long)_originX - radius, spacing);
                _lastX = Cell(_right + radius, spacing);
                _firstZ = Cell((long)_originZ - radius, spacing);
                _lastZ = Cell(_back + radius, spacing);
                _nextX = _firstX;
                _nextZ = _firstZ;
                _rule = candidateRule;
            }
        }
    }

    internal readonly record struct ResolvedAnchor(
        Variant Variant,
        int WorldX,
        int WorldZ,
        int SupportY);
    private bool IsValidAnchorAtSupportY(
        Rule rule,
        Variant variant,
        int x,
        int z,
        BiomeSample? cavity,
        int y,
        ChunkGenerationAccessRegion? accessRegion)
    {
        var direction = rule.Definition.SupportSurface ==
                        DecorationSupportSurface.Floor ? 1 : -1;
        if ((long)y + variant.MinY <= 0 ||
            (long)y + variant.MaxY > int.MaxValue - 1)
            return false;

        var supportNeighborY = checked(y + direction);
        accessRegion?.EnsureCanReadWorldPosition(x, y, z);
        accessRegion?.EnsureCanReadWorldPosition(
            x, supportNeighborY, z);
        var volume = cavity ?? _volumes.Sample(x, y, z);
        if (volume?.Primary != rule.Biome ||
            _terrain.DensityAt(x, y, z) < 0d ||
            _terrain.DensityAt(x, supportNeighborY, z) >= 0d)
            return false;

        if (rule.Placement == VolumeBiomePlacement.CarvedVoid &&
            !_terrain.IsCaveVoidAt(x, supportNeighborY, z))
            return false;

        var face = direction > 0
            ? BiomePaletteFace.Floor : BiomePaletteFace.Ceiling;
        var support = rule.Placement == VolumeBiomePlacement.Additive
            ? _materials.SampleColumn(volume, x, z).BlockAt(0)
            : _materials.VolumeBlockAt(volume, x, y, z, face);
        if (!variant.GroundBlocks.Contains(support))
            return false;

        foreach (var voxel in variant.Voxels)
        {
            var px = (long)x + voxel.X;
            var py = (long)y + voxel.Y;
            var pz = (long)z + voxel.Z;
            // Every bottom-most stem or arch foot must have real terrain
            // support, not just the central anchor column.
            var touchesGround = voxel.Y == direction;
            if (px is < int.MinValue or > int.MaxValue ||
                pz is < int.MinValue or > int.MaxValue ||
                py <= 0 || py > int.MaxValue)
                return false;

            var voxelX = (int)px;
            var voxelY = (int)py;
            var voxelZ = (int)pz;
            accessRegion?.EnsureCanReadWorldPosition(
                voxelX, voxelY, voxelZ);
            var hasUnsupportedFoot = false;
            if (touchesGround)
            {
                var supportYBelow = checked(voxelY - direction);
                accessRegion?.EnsureCanReadWorldPosition(
                    voxelX, supportYBelow, voxelZ);
                hasUnsupportedFoot =
                    _terrain.DensityAt(
                        voxelX, supportYBelow, voxelZ) < 0d;
            }

            if (_terrain.DensityAt(voxelX, voxelY, voxelZ) >= 0d ||
                hasUnsupportedFoot ||
                (rule.Placement == VolumeBiomePlacement.CarvedVoid &&
                 !_terrain.IsCaveVoidAt(voxelX, voxelY, voxelZ)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Shared deterministic support-Y search for one rule/candidate cell.
    /// Each Advance performs at most the requested number of Y samples; the
    /// lock makes concurrent vertical section cursors cooperate on one result.
    /// </summary>
    private sealed class AnchorSearch(
        Rule rule,
        Variant variant,
        int worldX,
        int worldZ)
    {
        private readonly object _gate = new();
        private bool _initialized;
        private bool _completed;
        private int _nextSupportY;
        private BiomeSample? _cavity;

        public int? SupportY { get; private set; }

        public bool Advance(
            VolumeStructureField owner,
            ChunkGenerationAccessRegion? accessRegion,
            int maximumSupportSamples)
        {
            lock (_gate)
            {
                if (_completed)
                    return true;

                if (!_initialized)
                {
                    accessRegion?.EnsureCanReadColumnData(worldX, worldZ);
                    if (rule.Placement == VolumeBiomePlacement.CarvedVoid)
                    {
                        _cavity = owner._volumes.SampleCave(worldX, worldZ);
                        if (_cavity?.Primary != rule.Biome)
                        {
                            _completed = true;
                            return true;
                        }
                    }

                    _nextSupportY = rule.Definition.MaxY;
                    _initialized = true;
                }

                var samples = 0;
                while (_nextSupportY >= rule.Definition.MinY &&
                       samples < maximumSupportSamples)
                {
                    var supportY = _nextSupportY--;
                    samples++;
                    if (owner.IsValidAnchorAtSupportY(
                            rule,
                            variant,
                            worldX,
                            worldZ,
                            _cavity,
                            supportY,
                            accessRegion))
                    {
                        SupportY = supportY;
                        _completed = true;
                        return true;
                    }
                }

                if (_nextSupportY < rule.Definition.MinY)
                    _completed = true;

                return _completed;
            }
        }
    }

    private static Variant CreateVariant(
        StructureDefinition definition, BlockRegistry blocks)
    {
        if (definition.Rotation || definition.GroundAnchorY != 0 ||
            definition.FluidVoxels.Count > 0 ||
            definition.ClearVoxels.Count > 0 ||
            definition.Connectors.Count > 0 ||
            definition.Voxels.Count == 0 ||
            definition.Voxels.Any(v => v.Detail is not null ||
                v.Orientation != BlockOrientation.Y))
            throw new ArgumentException(
                $"Volume template {definition.Id} needs unrotated block-only voxels, groundAnchorY=0, and no connectors/details.");

        var radius = definition.Voxels.Max(v =>
            Math.Max(Math.Abs(v.X), Math.Abs(v.Z)));
        var minimumY = definition.Voxels.Min(v => v.Y);
        var maximumY = definition.Voxels.Max(v => v.Y);
        if (minimumY < -16 || maximumY > 16 || minimumY == 0 ||
            maximumY == 0 || minimumY * maximumY < 0)
            throw new ArgumentException(
                $"Volume template {definition.Id} must fit entirely on one side of its support face within 16 blocks.");

        var support = definition.Restrictions.GroundBlocks
            .Select(blocks.GetId).ToHashSet();
        if (support.Count == 0)
            throw new ArgumentException(
                $"Volume template {definition.Id} requires support materials.");

        return new Variant(
            definition.Voxels.Select(v =>
                new RelativeVoxel(v.X, v.Y, v.Z, blocks.GetId(v.Block)))
                .ToArray(),
            support,
            radius,
            minimumY,
            maximumY);
    }

    private static int Cell(long coordinate, int spacing) =>
        checked((int)Math.Floor(coordinate / (double)spacing));

    private readonly record struct ColumnKey(int X, int Z);

    private readonly record struct CandidateKey(
        int RuleIndex, int CellX, int CellZ);

    internal sealed record RelativeVoxel(int X, int Y, int Z,
                                        BlockRuntimeId Block);

    internal sealed record Variant(
        IReadOnlyList<RelativeVoxel> Voxels,
        IReadOnlySet<BlockRuntimeId> GroundBlocks,
        int Radius, int MinY, int MaxY);

    private sealed record Rule(
        int Index,
        string Biome,
        VolumeBiomePlacement Placement,
        BiomeVolumeStructureDefinition Definition,
        IReadOnlyList<Variant> Variants,
        int Radius, int MinOffsetY, int MaxOffsetY,
        GenerationDomain Domain);
}

public readonly record struct VolumeStructureVoxel(
    int X, int Y, int Z, BlockRuntimeId Block);
