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
    private readonly BoundedMemoCache<CandidateKey, int?> _anchors =
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
    public ChunkCursor CreateChunkCursor(ChunkCoord coord) =>
        new(this, coord);

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
    /// Resumable deterministic scan of authored formations intersecting one
    /// 16³ section. Candidate evaluation and emitted voxels have separate
    /// budgets: a large blueprint cannot monopolize a worker slice, and a
    /// column containing no valid formations still yields after bounded tests.
    /// </summary>
    public sealed class ChunkCursor
    {
        private readonly VolumeStructureField _owner;
        private readonly ChunkCoord _coord;
        private readonly int _originX;
        private readonly int _originY;
        private readonly int _originZ;
        private readonly long _right;
        private readonly long _top;
        private readonly long _back;
        private int _ruleIndex = -1;
        private Rule? _rule;
        private int _firstX;
        private int _lastX;
        private int _firstZ;
        private int _lastZ;
        private int _nextX;
        private int _nextZ;
        private Variant? _activeVariant;
        private int _activeVoxelIndex;
        private int _activeWorldX;
        private int _activeWorldZ;
        private int _activeSupportY;

        internal ChunkCursor(VolumeStructureField owner, ChunkCoord coord)
        {
            _owner = owner;
            _coord = coord;
            var origin = VoxelCoordinates.ChunkOrigin(coord);
            _originX = origin.X;
            _originY = origin.Y;
            _originZ = origin.Z;
            _right = (long)origin.X + Chunk.Size - 1;
            _top = (long)origin.Y + Chunk.Size - 1;
            _back = (long)origin.Z + Chunk.Size - 1;
            IsComplete = owner._rules.Length == 0;
        }

        public bool IsComplete { get; private set; }

        /// <summary>
        /// Writes at most <paramref name="voxels"/>.Length intersecting
        /// voxels and evaluates at most <paramref name="maximumCandidateEvaluations"/>
        /// candidates. The cursor retains both rule/cell position and any
        /// partially emitted blueprint between calls.
        /// </summary>
        public bool Advance(
            Span<VolumeStructureVoxel> voxels,
            int maximumCandidateEvaluations,
            out int written)
        {
            if (voxels.IsEmpty)
                throw new ArgumentException(
                    "A volume-structure output buffer cannot be empty.",
                    nameof(voxels));
            if (maximumCandidateEvaluations <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCandidateEvaluations));

            written = 0;
            if (IsComplete)
                return true;

            var evaluatedCandidates = 0;
            while (!IsComplete &&
                   evaluatedCandidates < maximumCandidateEvaluations &&
                   written < voxels.Length)
            {
                if (_activeVariant is { } activeVariant)
                {
                    while (_activeVoxelIndex < activeVariant.Voxels.Count &&
                           written < voxels.Length)
                    {
                        var relative = activeVariant.Voxels[_activeVoxelIndex++];
                        var worldX = (long)_activeWorldX + relative.X;
                        var worldY = (long)_activeSupportY + relative.Y;
                        var worldZ = (long)_activeWorldZ + relative.Z;
                        if (worldX < _originX || worldX > _right ||
                            worldY < _originY || worldY > _top ||
                            worldZ < _originZ || worldZ > _back)
                            continue;

                        voxels[written++] = new VolumeStructureVoxel(
                            (int)worldX, (int)worldY, (int)worldZ, relative.Block);
                    }

                    if (_activeVoxelIndex >= activeVariant.Voxels.Count)
                    {
                        _activeVariant = null;
                        _activeVoxelIndex = 0;
                    }
                    continue;
                }

                if (!TryNextCandidate(
                        out var rule, out var cellX, out var cellZ))
                {
                    IsComplete = true;
                    break;
                }

                evaluatedCandidates++;
                var hash = WorldGenerationEntropy.Sample2D(
                    _owner._seed, rule.Domain, cellX, cellZ);
                if (WorldGenerationEntropy.Unit(hash) >= rule.Definition.Chance)
                    continue;

                // Separate deterministic hashes give independent axes and
                // family variations without global/random state.
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
                var anchor = _owner._anchors.GetOrAdd(
                    new CandidateKey(rule.Index, cellX, cellZ),
                    () => _owner.TryAnchor(
                        rule, variant, (int)worldX, (int)worldZ,
                        out var located) ? located : null);
                if (anchor is not { } supportY)
                    continue;

                _activeVariant = variant;
                _activeVoxelIndex = 0;
                _activeWorldX = (int)worldX;
                _activeWorldZ = (int)worldZ;
                _activeSupportY = supportY;
            }

            return IsComplete;
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
                if (_top < (long)candidateRule.Definition.MinY +
                        candidateRule.MinOffsetY ||
                    _originY > (long)candidateRule.Definition.MaxY +
                        candidateRule.MaxOffsetY)
                {
                    continue;
                }

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

    private bool TryAnchor(Rule rule, Variant variant, int x, int z,
                           out int supportY)
    {
        supportY = 0;
        var direction = rule.Definition.SupportSurface ==
                        DecorationSupportSurface.Floor ? 1 : -1;
        var cavity = rule.Placement == VolumeBiomePlacement.CarvedVoid
            ? _volumes.SampleCave(x, z)
            : null;

        if (rule.Placement == VolumeBiomePlacement.CarvedVoid &&
            cavity?.Primary != rule.Biome)
            return false;

        for (var y = rule.Definition.MaxY; y >= rule.Definition.MinY; y--)
        {
            if ((long)y + variant.MinY <= 0 ||
                (long)y + variant.MaxY > int.MaxValue - 1)
                continue;

            var volume = cavity ?? _volumes.Sample(x, y, z);
            if (volume?.Primary != rule.Biome ||
                _terrain.DensityAt(x, y, z) < 0d ||
                _terrain.DensityAt(x, y + direction, z) >= 0d)
                continue;

            if (rule.Placement == VolumeBiomePlacement.CarvedVoid &&
                !_terrain.IsCaveVoidAt(x, y + direction, z))
                continue;

            var face = direction > 0
                ? BiomePaletteFace.Floor : BiomePaletteFace.Ceiling;
            var support = rule.Placement == VolumeBiomePlacement.Additive
                ? _materials.SampleColumn(volume, x, z).BlockAt(0)
                : _materials.VolumeBlockAt(volume, x, y, z, face);
            if (!variant.GroundBlocks.Contains(support))
                continue;

            var valid = true;
            foreach (var voxel in variant.Voxels)
            {
                var px = (long)x + voxel.X;
                var py = (long)y + voxel.Y;
                var pz = (long)z + voxel.Z;
                // Every bottom-most stem or arch foot must have real
                // terrain support, not just the central anchor column.
                var touchesGround = voxel.Y == direction;
                if (px is < int.MinValue or > int.MaxValue ||
                    pz is < int.MinValue or > int.MaxValue ||
                    py <= 0 || py > int.MaxValue ||
                    _terrain.DensityAt((int)px, (int)py, (int)pz) >= 0d ||
                    (touchesGround &&
                     _terrain.DensityAt((int)px, (int)py - direction,
                         (int)pz) < 0d) ||
                    (rule.Placement == VolumeBiomePlacement.CarvedVoid &&
                     !_terrain.IsCaveVoidAt((int)px, (int)py, (int)pz)))
                {
                    valid = false;
                    break;
                }
            }
            if (!valid)
                continue;

            supportY = y;
            return true;
        }
        return false;
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

    private readonly record struct CandidateKey(
        int RuleIndex, int CellX, int CellZ);

    private sealed record RelativeVoxel(int X, int Y, int Z,
                                        BlockRuntimeId Block);

    private sealed record Variant(
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
