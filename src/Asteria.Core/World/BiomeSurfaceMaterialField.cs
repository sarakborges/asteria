using System.Runtime.CompilerServices;

namespace Asteria.Core.World;

[Flags]
internal enum BiomePaletteFaceMask : byte
{
    None = 0,
    Floor = 1 << 0,
    Walls = 1 << 1,
    Ceiling = 1 << 2,
}

internal readonly record struct BiomeDirectionalPalette(
    BiomePaletteFaceMask Faces,
    uint MaxDepth)
{
    public bool Has(BiomePaletteFace face) =>
        (Faces & (face switch
        {
            BiomePaletteFace.Floor => BiomePaletteFaceMask.Floor,
            BiomePaletteFace.Walls => BiomePaletteFaceMask.Walls,
            BiomePaletteFace.Ceiling => BiomePaletteFaceMask.Ceiling,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        })) != 0;
}

/// <summary>
/// Resolves authored surface coatings and three-dimensional solid geology.
/// Terrain height may blend biome influences; material ownership stays with
/// the authoritative primary surface biome.
/// </summary>
public sealed class BiomeSurfaceMaterialField
{
    /// <summary>
    /// Resolved per-biome geology rule for hot voxel loops. A chunk column
    /// keeps this handle so each voxel does not repeat a string-keyed lookup.
    /// </summary>
    internal readonly struct GeologySampler
    {
        // One reference per column instead of duplicating the immutable seed
        // and rule reference in every cache entry.
        private readonly ResolvedGeology? _geology;

        private GeologySampler(ResolvedGeology? geology)
        {
            _geology = geology;
        }

        internal static GeologySampler Resolve(
            BiomeSurfaceMaterialField field,
            BiomeSample sample) =>
            field._geologyRules.TryGetValue(sample.Primary, out var geology)
                ? new GeologySampler(geology)
                : default;

        internal uint? SurfaceCoverDepth =>
            _geology?.SurfaceCoverDepth;

        internal bool RequiresSubmergedSurface =>
            _geology is { SurfaceCoverage: > 0f } &&
            _geology.SurfaceSubmergedOnly;

        internal bool TryBlockAtCached(
            int worldX,
            int worldY,
            int worldZ,
            WorldGenerationEntropy.ValueNoise3DLatticeCache latticeCache,
            out BlockRuntimeId block)
        {
            if (_geology is null)
            {
                block = BlockRuntimeId.Air;
                return false;
            }

            block = _geology.BlockAtCached(
                worldX, worldY, worldZ, latticeCache);
            return true;
        }

        internal bool TrySurfaceBlockAtCached(
            int worldX,
            int worldY,
            int worldZ,
            WorldGenerationEntropy.ValueNoise3DLatticeCache latticeCache,
            out BlockRuntimeId block)
        {
            if (_geology is null ||
                !_geology.TrySurfaceBlockAtCached(
                    worldX, worldY, worldZ,
                    out block, latticeCache))
            {
                block = BlockRuntimeId.Air;
                return false;
            }

            return true;
        }
    }

    private const int SampledColumnCacheCapacity = 8192;

    private readonly record struct MaterialColumnKey(
        BiomeSample Sample,
        int WorldX,
        int WorldZ,
        SurfacePlacementContext? Placement);

    private readonly BoundedMemoCache<
        MaterialColumnKey,
        BiomeSurfaceMaterialColumn> _sampledColumns =
            new(SampledColumnCacheCapacity);

    private readonly ulong _seed;
    private readonly SurfaceTerrainField? _terrain;
    private readonly SurfaceMosaicField? _mosaic;
    private readonly Dictionary<string, MaterialRule> _rules;
    private readonly Dictionary<string, FaceRules> _faceRules;
    private readonly Dictionary<string, ResolvedGeology> _geologyRules;

    public BiomeSurfaceMaterialField(
        ulong seed,
        IEnumerable<BiomeDefinition> biomes,
        BlockRegistry blocks,
        SurfaceTerrainField? terrain = null,
        SurfaceMosaicField? mosaic = null)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(blocks);

        _seed = seed;
        _terrain = terrain;
        _mosaic = mosaic;
        var biomeDefinitions = biomes.ToArray();
        _faceRules = biomeDefinitions
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(
                biome => biome.Id,
                biome => FaceRules.Create(biome, blocks),
                StringComparer.Ordinal);
        _rules = _faceRules.ToDictionary(
            pair => pair.Key, pair => pair.Value.Floor,
            StringComparer.Ordinal);
        _geologyRules = biomeDefinitions
            .Where(biome => biome.Geology is not null)
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(
                biome => biome.Id,
                biome => ResolvedGeology.Create(seed, biome.Id, biome.Geology!, blocks),
                StringComparer.Ordinal);

        var volumeBiomeIds = biomeDefinitions
            .Where(biome => biome.VolumeLayout is not null)
            .Select(biome => biome.Id)
            .ToHashSet(StringComparer.Ordinal);
        HasDirectionalFacePalettes = _faceRules.Any(pair =>
            pair.Value.OverrideWalls ||
            pair.Value.OverrideCeiling ||
            (volumeBiomeIds.Contains(pair.Key) &&
             (!_geologyRules.ContainsKey(pair.Key) ||
              pair.Value.OverrideFloor)));
        MaximumDirectionalPaintDepth = _faceRules.Values
            .Select(rule => rule.MaxDepth)
            .DefaultIfEmpty(0u)
            .Max();
        FacePalettesPreserveOccupancy =
            _faceRules.Values.All(profile => profile.PreservesOccupancy) &&
            _geologyRules.Values.All(rule => !rule.MayResolveAir);

        if (_rules.Count == 0)
        {
            throw new ArgumentException(
                "Surface material field requires at least one biome.",
                nameof(biomes));
        }
    }

    /// <summary>
    /// True when at least one selected volume biome or authored surface
    /// wall/ceiling override can contribute a directional face material.
    /// </summary>
    public bool HasDirectionalFacePalettes { get; }

    /// <summary>
    /// Global upper bound for exposed-face search. The selected owner's
    /// individual palette depth is checked before painting.
    /// </summary>
    public uint MaximumDirectionalPaintDepth { get; }

    /// <summary>
    /// True when directional face palettes can only replace an existing
    /// solid block with another solid block. Support-face queries may omit
    /// these presentation-material rules without changing occupancy.
    /// </summary>
    public bool FacePalettesPreserveOccupancy { get; }

    public BlockRuntimeId BlockAt(
        BiomeSample sample,
        int worldX,
        int worldZ,
        uint depth,
        SurfacePlacementContext? placement = null)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (!_rules.TryGetValue(
                sample.Primary,
                out var rule))
        {
            throw new KeyNotFoundException(
                $"Surface biome {sample.Primary} has no material rule.");
        }

        if (_geologyRules.TryGetValue(sample.Primary, out var geology) &&
            _terrain is not null &&
            depth >= geology.SurfaceCoverDepth)
        {
            var surfaceY = _terrain.SurfaceHeight(worldX, worldZ);
            var geologicalY = (int)Math.Max(
                int.MinValue, (long)surfaceY - depth);
            return geology.BlockAt(_seed, worldX, geologicalY, worldZ);
        }

        if (depth == 0 &&
            _mosaic?.Sample(sample, worldX, worldZ) is
                { Block.IsAir: false } choice)
            return choice.Block;

        if (depth >= rule.CoreStartDepth)
        {
            return rule.CoreBlock;
        }

        return rule.BlockAtDepth(
            _seed,
            worldX,
            worldZ,
            depth,
            PlacementFor(rule, worldX, worldZ, placement));
    }

    /// <summary>
    /// Resolves the hot-loop handle for one biome's authored geology.
    /// </summary>
    internal GeologySampler ResolveGeology(BiomeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return GeologySampler.Resolve(this, sample);
    }

    /// <summary>
    /// Returns the authored surface-cover depth, or null for biomes using only
    /// the legacy surface profile. Beyond this depth, the 3D geology owns solids.
    /// </summary>
    public uint? GeologySurfaceCoverDepth(BiomeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return _geologyRules.TryGetValue(sample.Primary, out var geology)
            ? geology.SurfaceCoverDepth
            : null;
    }

    /// <summary>
    /// Resolves the volumetric geology at a solid world-space voxel. Material
    /// selection uses a continuous 3D field, not altitude-specific layer rules.
    /// </summary>
    public bool TryGeologyBlockAt(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        out BlockRuntimeId block) =>
        TryGeologyBlockAtCore(
            sample, worldX, worldY, worldZ, null, out block);

    internal bool TryGeologyBlockAtCached(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        WorldGenerationEntropy.ValueNoise3DLatticeCache latticeCache,
        out BlockRuntimeId block)
    {
        ArgumentNullException.ThrowIfNull(latticeCache);
        return TryGeologyBlockAtCore(
            sample, worldX, worldY, worldZ, latticeCache, out block);
    }

    private bool TryGeologyBlockAtCore(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache,
        out BlockRuntimeId block)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_geologyRules.TryGetValue(sample.Primary, out var geology))
        {
            block = BlockRuntimeId.Air;
            return false;
        }

        block = geology.BlockAt(_seed, worldX, worldY, worldZ, latticeCache);
        return true;
    }

    /// <summary>
    /// True when this biome's exposed geological deposits are restricted to
    /// terrain with generated fluid immediately above its surface.
    /// </summary>
    public bool SurfaceGeologyRequiresSubmerged(BiomeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return _geologyRules.TryGetValue(sample.Primary, out var geology) &&
            geology.SurfaceCoverage > 0f &&
            geology.SurfaceSubmergedOnly;
    }

    /// <summary>
    /// Resolves a deterministic world-space deposit for an exposed top voxel.
    /// The caller applies the authored biome cover when this returns false.
    /// </summary>
    public bool TrySurfaceGeologyBlockAt(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        out BlockRuntimeId block) =>
        TrySurfaceGeologyBlockAtCore(
            sample, worldX, worldY, worldZ, null, out block);

    internal bool TrySurfaceGeologyBlockAtCached(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        WorldGenerationEntropy.ValueNoise3DLatticeCache latticeCache,
        out BlockRuntimeId block)
    {
        ArgumentNullException.ThrowIfNull(latticeCache);
        return TrySurfaceGeologyBlockAtCore(
            sample, worldX, worldY, worldZ, latticeCache, out block);
    }

    private bool TrySurfaceGeologyBlockAtCore(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache,
        out BlockRuntimeId block)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_geologyRules.TryGetValue(sample.Primary, out var geology) ||
            !geology.TrySurfaceBlockAt(
                _seed, worldX, worldY, worldZ, out block, latticeCache))
        {
            block = BlockRuntimeId.Air;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Samples the same authored palette across 3D exposed faces, where
    /// depth increases into the solid along the face normal.
    /// </summary>
    public BlockRuntimeId VolumeBlockAt(
        BiomeSample sample, int worldX, int worldY, int worldZ,
        BiomePaletteFace face = BiomePaletteFace.Floor, uint depth = 0)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_faceRules.TryGetValue(sample.Primary, out var rules))
            throw new KeyNotFoundException(
                $"Biome {sample.Primary} has no material palette.");
        if (!rules.HasOverride(face) &&
            _geologyRules.TryGetValue(sample.Primary, out var geology))
            return geology.BlockAt(_seed, worldX, worldY, worldZ);

        var rule = rules.For(face);
        SurfacePlacementContext? placement = null;
        if (rule.HasConditions)
        {
            if (_terrain is null)
            {
                throw new InvalidOperationException(
                    "Conditional volume material patches require a terrain field.");
            }

            var sampled = SurfacePlacementContext.Sample(
                _terrain, worldX, worldZ, rule.RequiresSlope);
            placement = new SurfacePlacementContext(worldY, sampled.Slope);
        }

        return rule.VolumeBlockAt(
            _seed, worldX, worldY, worldZ, depth, placement);
    }

    /// <summary>Maximum relevant inward distance (at least one voxel).</summary>
    public uint MaxVolumePaintDepth(BiomeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_faceRules.TryGetValue(sample.Primary, out var rules))
            throw new KeyNotFoundException(
                $"Biome {sample.Primary} has no material palette.");
        return rules.MaxDepth;
    }

    public bool HasDirectionalOverride(
        BiomeSample sample, BiomePaletteFace face) =>
        _faceRules.TryGetValue(sample.Primary, out var rules) &&
        rules.HasOverride(face);

    internal BiomeDirectionalPalette DirectionalPaletteFor(
        BiomeRegionOwner owner)
    {
        if (!_faceRules.TryGetValue(owner.Biome.Primary, out var rules))
            return default;

        var faces = BiomePaletteFaceMask.None;
        if (owner.Kind == BiomeRegionOwnerKind.Surface)
        {
            if (rules.OverrideWalls)
                faces |= BiomePaletteFaceMask.Walls;
            if (rules.OverrideCeiling)
                faces |= BiomePaletteFaceMask.Ceiling;
        }
        else
        {
            var hasGeology = _geologyRules.ContainsKey(owner.Biome.Primary);
            if (rules.OverrideFloor || !hasGeology)
                faces |= BiomePaletteFaceMask.Floor;
            if (rules.OverrideWalls || !hasGeology)
                faces |= BiomePaletteFaceMask.Walls;
            if (rules.OverrideCeiling || !hasGeology)
                faces |= BiomePaletteFaceMask.Ceiling;
        }

        return faces == BiomePaletteFaceMask.None
            ? default
            : new BiomeDirectionalPalette(faces, rules.MaxDepth);
    }

    internal bool ShouldApplyDirectionalFacePalette(
        BiomeRegionOwner owner,
        BiomePaletteFace face) =>
        DirectionalPaletteFor(owner).Has(face);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public BiomeSurfaceMaterialColumn SampleColumn(
        BiomeSample sample,
        int worldX,
        int worldZ,
        SurfacePlacementContext? placement = null)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (!_rules.TryGetValue(sample.Primary, out var rule))
        {
            throw new KeyNotFoundException(
                $"Surface biome {sample.Primary} has no material rule.");
        }

        // The same immutable X/Z biome is sampled again for each vertical
        // chunk band. Cache the resolved layer-depth and block arrays so
        // vertical materialization does not repeatedly allocate them.
        var key = new MaterialColumnKey(
            sample, worldX, worldZ, placement);
        if (_sampledColumns.TryGet(key, out var cached))
            return cached;

        return _sampledColumns.GetOrAdd(
            key,
            () =>
            {
                var mosaic = _mosaic?.Sample(
                    sample, worldX, worldZ) ?? default;
                return rule.SampleColumn(
                    _seed,
                    worldX,
                    worldZ,
                    PlacementFor(rule, worldX, worldZ, placement),
                    mosaic.Block);
            });
    }

    /// <summary>
    /// Immutable deepest layer; queries below its start depth cannot be
    /// affected by shallow patches or conditional terrain sampling.
    /// </summary>
    public (BlockRuntimeId Block, uint StartDepth) CoreLayer(
        BiomeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_rules.TryGetValue(sample.Primary, out var rule))
        {
            throw new KeyNotFoundException(
                $"Surface biome {sample.Primary} has no material rule.");
        }

        return (rule.CoreBlock, rule.CoreStartDepth);
    }

    private SurfacePlacementContext? PlacementFor(
        MaterialRule rule,
        int worldX,
        int worldZ,
        SurfacePlacementContext? supplied)
    {
        if (supplied.HasValue)
        {
            return supplied;
        }

        if (!rule.HasConditions)
        {
            return null;
        }

        if (_terrain is null)
        {
            throw new InvalidOperationException(
                "Conditional material patches require a terrain field.");
        }

        return SurfacePlacementContext.Sample(
            _terrain, worldX, worldZ,
            rule.RequiresSlope, rule.UsesBaseSurface);
    }

    private sealed record FaceRules(
        MaterialRule Floor,
        MaterialRule Walls,
        MaterialRule Ceiling,
        bool OverrideFloor,
        bool OverrideWalls,
        bool OverrideCeiling)
    {
        public uint MaxDepth =>
            Math.Max(1u, Math.Max(Floor.CoreStartDepth,
                Math.Max(Walls.CoreStartDepth, Ceiling.CoreStartDepth)));

        public MaterialRule For(BiomePaletteFace face) => face switch
        {
            BiomePaletteFace.Floor => Floor,
            BiomePaletteFace.Walls => Walls,
            BiomePaletteFace.Ceiling => Ceiling,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };

        public bool PreservesOccupancy =>
            !Floor.MayResolveAir && !Walls.MayResolveAir &&
            !Ceiling.MayResolveAir;

        public bool HasOverride(BiomePaletteFace face) => face switch
        {
            BiomePaletteFace.Floor => OverrideFloor,
            BiomePaletteFace.Walls => OverrideWalls,
            BiomePaletteFace.Ceiling => OverrideCeiling,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };

        public static FaceRules Create(BiomeDefinition biome, BlockRegistry blocks)
        {
            var palette = biome.Palette;
            var defaults = MaterialRule.Create(biome, blocks, palette.Default, null);
            var floor = palette.Floor is null
                ? defaults : MaterialRule.Create(biome, blocks, palette.Floor, "floor");
            var walls = palette.Walls is null
                ? defaults : MaterialRule.Create(biome, blocks, palette.Walls, "walls");
            var ceiling = palette.Ceiling is null
                ? defaults : MaterialRule.Create(biome, blocks, palette.Ceiling, "ceiling");
            return new FaceRules(
                floor, walls, ceiling,
                palette.Floor is not null,
                palette.Walls is not null,
                palette.Ceiling is not null);
        }
    }

    private sealed class ResolvedGeology
    {
        // Each blended field maps to a unit value clamped to
        // [1e-6, 1 - 1e-6] before its Gumbel score. This is an exact upper
        // bound for any candidate, enabling safe score pruning when boundary
        // perturbation (which needs every candidate score) is disabled.
        private static readonly double MaximumGumbelScore =
            -Math.Log(-Math.Log(1d - 1e-6d));

        private readonly record struct SpatialPosition(double X, double Y, double Z);

        public bool MayResolveAir => _baseBlock.IsAir ||
            _blocks.Any(block => block.IsAir) ||
            _volumeBlocks.Any(block => block.IsAir);

        private readonly ulong _seed;
        private readonly BiomeGeologyDefinition _definition;
        private readonly BlockRuntimeId _baseBlock;
        private readonly BlockRuntimeId[] _blocks;
        private readonly BlockRuntimeId[] _volumeBlocks;
        private readonly GenerationDomain _surfaceExposureDomain;
        private readonly GenerationDomain[] _warpDomains;
        private readonly GenerationDomain[] _warpDetailDomains;
        private readonly GenerationDomain[] _volumeMaterialDomains;
        private readonly GenerationDomain[] _volumeDetailDomains;
        private readonly GenerationDomain[] _volumeMicroDomains;
        private readonly GenerationDomain[] _volumeClusterDomains;
        private readonly GenerationDomain[] _volumeGrainDomains;
        private readonly GenerationDomain[] _surfaceMaterialDomains;
        private readonly GenerationDomain[] _surfaceDetailDomains;
        private readonly GenerationDomain[] _surfaceMicroDomains;
        private readonly GenerationDomain[] _surfaceClusterDomains;
        private readonly GenerationDomain[] _surfaceGrainDomains;
        private readonly double[] _volumeBiases;
        private readonly double[] _surfaceBiases;
        private readonly double[] _volumeRegionThresholds;
        private readonly double[] _surfaceRegionThresholds;
        private readonly GenerationDomain[] _regionWarpDomains;
        private readonly GenerationDomain[] _regionalEdgeDomains;
        private readonly GenerationDomain _volumeRegionMaterialDomain;
        private readonly GenerationDomain _surfaceRegionMaterialDomain;
        private readonly GenerationDomain _volumeRegionalDetailNoiseDomain;
        private readonly GenerationDomain _surfaceRegionalDetailNoiseDomain;
        private readonly GenerationDomain _volumeRegionalDetailMaterialDomain;
        private readonly GenerationDomain _surfaceRegionalDetailMaterialDomain;
        private readonly BiomeGeologySamplingMode _samplingMode;
        private readonly uint _regionScale;
        private readonly uint _regionVerticalScale;
        private readonly uint _regionalDetailScale;
        private readonly uint _regionalDetailVerticalScale;
        private readonly uint _regionalEdgeScale;
        private readonly uint _regionalEdgeVerticalScale;
        private readonly double _regionWarpStrength;
        private readonly uint _surfaceExposureScale;
        private readonly uint _warpHorizontalScale;
        private readonly uint _warpVerticalScale;
        private readonly double _warpHorizontalStrength;
        private readonly double _warpVerticalStrength;
        private readonly uint _detailWarpScale;
        private readonly uint _detailWarpVerticalScale;
        private readonly double _detailWarpStrength;
        private readonly double _detailWarpVerticalStrength;
        private readonly uint _selectionScale;
        private readonly uint _selectionVerticalScale;
        private readonly uint _detailScale;
        private readonly uint _detailVerticalScale;
        private readonly uint _microScale;
        private readonly uint _microVerticalScale;
        private readonly uint _clusterScale;
        private readonly uint _clusterVerticalScale;
        private readonly uint _grainScale;
        private readonly uint _grainVerticalScale;
        private readonly double _grainInfluence;
        private readonly double _clusterInfluence;
        private readonly double _remainingInfluence;
        private readonly double _boundaryInfluence;

        private ResolvedGeology(
            ulong seed,
            BiomeGeologyDefinition definition,
            BlockRuntimeId baseBlock,
            BlockRuntimeId[] blocks,
            string biomeId)
        {
            _seed = seed;
            _definition = definition;
            _surfaceExposureScale = Math.Max(6u, definition.DetailScale / 2u);
            _warpHorizontalScale = Math.Max(12u, definition.Scale / 3u);
            _warpVerticalScale = Math.Max(10u, definition.VerticalScale / 3u);
            _warpHorizontalStrength = Math.Max(6d, definition.DetailScale * 0.9d);
            _warpVerticalStrength = Math.Max(5d, definition.DetailScale * 0.7d);
            _detailWarpScale = Math.Max(6u, definition.DetailScale / 2u);
            _detailWarpVerticalScale = Math.Max(
                5u, Math.Min(definition.VerticalScale / 4u, definition.DetailScale / 2u));
            _detailWarpStrength = Math.Max(2d, definition.DetailScale * 0.35d);
            _detailWarpVerticalStrength = Math.Max(2d, definition.DetailScale * 0.28d);
            _selectionScale = Math.Max(12u, definition.SelectionScale);
            _selectionVerticalScale = Math.Max(10u, definition.VerticalScale);
            _detailScale = Math.Max(6u, definition.DetailScale);
            _detailVerticalScale = Math.Max(
                6u, Math.Min(definition.VerticalScale / 3u, definition.DetailScale));
            _microScale = Math.Max(4u, definition.DetailScale / 2u);
            _microVerticalScale = Math.Max(4u, _detailVerticalScale / 2u);
            _grainScale = definition.GrainScale ?? Math.Max(2u, _detailScale / 8u);
            _grainVerticalScale = definition.GrainVerticalScale ??
                Math.Max(2u, _detailVerticalScale / 8u);
            _clusterScale = definition.ClusterScale ?? Math.Max(3u, _detailScale / 4u);
            _clusterVerticalScale = definition.ClusterVerticalScale ??
                Math.Max(3u, _detailVerticalScale / 4u);
            _grainInfluence = definition.GrainInfluence;
            _clusterInfluence = definition.ClusterInfluence;
            _remainingInfluence = 1d - _grainInfluence - _clusterInfluence;
            _boundaryInfluence = definition.BoundaryGrainInfluence;
            _samplingMode = definition.SamplingMode;
            _regionScale = definition.RegionScale;
            _regionVerticalScale = definition.RegionVerticalScale;
            _regionalDetailScale = Math.Max(
                4u, Math.Min(_detailScale, Math.Max(4u, _regionScale / 3u)));
            _regionalDetailVerticalScale = Math.Max(
                4u, Math.Min(
                    _detailVerticalScale,
                    Math.Max(4u, _regionVerticalScale / 3u)));
            _regionalEdgeScale = Math.Max(3u, Math.Min(
                _clusterScale, _regionScale / 6u));
            _regionalEdgeVerticalScale = Math.Max(3u, Math.Min(
                _clusterVerticalScale, _regionVerticalScale / 6u));
            _regionWarpStrength = definition.RegionWarpStrength;
            _baseBlock = baseBlock;
            _blocks = blocks;
            _volumeBlocks = [baseBlock, .. blocks];
            _surfaceExposureDomain = GenerationDomain.Named(
                $"material/geology/surface-exposure/v2/{biomeId}");
            _warpDomains = new[] { "x", "y", "z" }
                .Select(axis => GenerationDomain.Named(
                    $"material/geology/domain-warp/v4/{biomeId}/{axis}"))
                .ToArray();
            _warpDetailDomains = new[] { "x", "y", "z" }
                .Select(axis => GenerationDomain.Named(
                    $"material/geology/domain-warp-detail/v1/{biomeId}/{axis}"))
                .ToArray();
            _regionWarpDomains = new[] { "x", "y", "z" }
                .Select(axis => GenerationDomain.Named(
                    $"material/geology/regional-warp/v1/{biomeId}/{axis}"))
                .ToArray();
            _regionalEdgeDomains = new[] { "x", "y", "z" }
                .Select(axis => GenerationDomain.Named(
                    $"material/geology/regional-edge/v1/{biomeId}/{axis}"))
                .ToArray();
            _volumeRegionMaterialDomain = GenerationDomain.Named(
                $"material/geology/regional-volume-material/v1/{biomeId}");
            _surfaceRegionMaterialDomain = GenerationDomain.Named(
                $"material/geology/regional-surface-material/v1/{biomeId}");
            _volumeRegionalDetailNoiseDomain = GenerationDomain.Named(
                $"material/geology/regional-volume-detail-noise/v1/{biomeId}");
            _surfaceRegionalDetailNoiseDomain = GenerationDomain.Named(
                $"material/geology/regional-surface-detail-noise/v1/{biomeId}");
            _volumeRegionalDetailMaterialDomain = GenerationDomain.Named(
                $"material/geology/regional-volume-detail-material/v1/{biomeId}");
            _surfaceRegionalDetailMaterialDomain = GenerationDomain.Named(
                $"material/geology/regional-surface-detail-material/v1/{biomeId}");

            var volumeNames = new[] { definition.BaseBlock }
                .Concat(definition.Blocks).ToArray();
            _volumeMaterialDomains = volumeNames
                .Select(block => GenerationDomain.Named(
                    $"material/geology/volume-field/v1/{biomeId}/{block}"))
                .ToArray();
            _volumeDetailDomains = volumeNames
                .Select(block => GenerationDomain.Named(
                    $"material/geology/volume-detail/v1/{biomeId}/{block}"))
                .ToArray();
            _volumeMicroDomains = volumeNames
                .Select(block => GenerationDomain.Named(
                    $"material/geology/volume-micro/v1/{biomeId}/{block}"))
                .ToArray();
            _volumeClusterDomains = volumeNames
                .Select(block => GenerationDomain.Named(
                    $"material/geology/volume-cluster/v1/{biomeId}/{block}"))
                .ToArray();
            _volumeGrainDomains = volumeNames
                .Select(block => GenerationDomain.Named(
                    $"material/geology/volume-grain/v1/{biomeId}/{block}"))
                .ToArray();

            _surfaceMaterialDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/surface-field/v1/{biomeId}/{block}"))
                .ToArray();
            _surfaceDetailDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/surface-detail/v2/{biomeId}/{block}"))
                .ToArray();
            _surfaceMicroDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/surface-micro/v1/{biomeId}/{block}"))
                .ToArray();
            _surfaceClusterDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/surface-cluster/v1/{biomeId}/{block}"))
                .ToArray();
            _surfaceGrainDomains = definition.Blocks
                .Select(block => GenerationDomain.Named(
                    $"material/geology/surface-grain/v1/{biomeId}/{block}"))
                .ToArray();

            var alternativeWeightTotal = definition.BlockWeights.Sum();
            var baseProbability = 1d - definition.Coverage;
            // Spatial fields are correlated value-noise samples rather than
            // independent ideal Gumbel variates. Compress log-biases so rare
            // authored materials remain present in every broad region while
            // weights still influence their relative frequency.
            const double biasStrength = 0.35d;
            _volumeBiases = new double[_volumeBlocks.Length];
            _volumeBiases[0] = baseProbability > 0d
                ? biasStrength * Math.Log(baseProbability)
                : double.NegativeInfinity;
            for (var index = 0; index < definition.BlockWeights.Count; index++)
            {
                var probability = definition.Coverage *
                    definition.BlockWeights[index] / alternativeWeightTotal;
                _volumeBiases[index + 1] = biasStrength * Math.Log(probability);
            }

            _surfaceBiases = definition.BlockWeights
                .Select(weight => biasStrength * Math.Log(weight / alternativeWeightTotal))
                .ToArray();

            var volumeProbabilities = new double[_volumeBlocks.Length];
            volumeProbabilities[0] = baseProbability;
            for (var index = 0; index < definition.BlockWeights.Count; index++)
            {
                volumeProbabilities[index + 1] =
                    definition.Coverage *
                    definition.BlockWeights[index] /
                    alternativeWeightTotal;
            }

            _volumeRegionThresholds = CumulativeThresholds(volumeProbabilities);
            _surfaceRegionThresholds = CumulativeThresholds(
                definition.BlockWeights
                    .Select(weight => weight / alternativeWeightTotal)
                    .ToArray());
        }

        public uint SurfaceCoverDepth => _definition.SurfaceCoverDepth;
        public float SurfaceCoverage => _definition.SurfaceCoverage;
        public bool SurfaceSubmergedOnly => _definition.SurfaceSubmergedOnly;

        internal BlockRuntimeId BlockAtCached(
            int x,
            int y,
            int z,
            WorldGenerationEntropy.ValueNoise3DLatticeCache latticeCache) =>
            BlockAt(_seed, x, y, z, latticeCache);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public BlockRuntimeId BlockAt(
            ulong seed, int x, int y, int z,
            WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache = null)
        {
            if (_samplingMode != BiomeGeologySamplingMode.PerVoxel)
            {
                return SelectRegionalMaterial(
                    seed, x, y, z,
                    _volumeBlocks, _volumeRegionThresholds,
                    _volumeRegionMaterialDomain,
                    _volumeRegionalDetailNoiseDomain,
                    _volumeRegionalDetailMaterialDomain,
                    latticeCache);
            }

            // Base material and deposits now compete in one spatial selector.
            // The previous separate formation mask created a second set of
            // broad, continuous boundaries that looked like columns/sheets.
            var position = WarpCoordinates(seed, x, y, z, latticeCache);
            return SelectMaterial(
                seed, position, _volumeBlocks,
                _volumeMaterialDomains, _volumeDetailDomains, _volumeMicroDomains,
                _volumeClusterDomains, _volumeGrainDomains, _volumeBiases,
                latticeCache);
        }

        internal bool TrySurfaceBlockAtCached(
            int x,
            int y,
            int z,
            out BlockRuntimeId block,
            WorldGenerationEntropy.ValueNoise3DLatticeCache latticeCache) =>
            TrySurfaceBlockAt(_seed, x, y, z, out block, latticeCache);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public bool TrySurfaceBlockAt(
            ulong seed, int x, int y, int z, out BlockRuntimeId block,
            WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache = null)
        {
            if (_definition.SurfaceCoverage <= 0f)
            {
                block = BlockRuntimeId.Air;
                return false;
            }

            // Surface coverage is a dedicated shallow X/Z exposure field. It
            // must not also pass the volumetric occupancy threshold; that
            // double gate made many flat surfaces remain entirely grass/sand.
            var exposure = WorldGenerationEntropy.SmoothNoise2D(
                seed, _surfaceExposureDomain, x, z, _surfaceExposureScale);
            if (exposure < 1d - _definition.SurfaceCoverage * 2d)
            {
                block = BlockRuntimeId.Air;
                return false;
            }

            // With one authored surface material, exposure is the only
            // spatial decision; material selection itself is constant.
            if (_blocks.Length == 1)
            {
                block = _blocks[0];
                return true;
            }

            if (_samplingMode != BiomeGeologySamplingMode.PerVoxel)
            {
                block = SelectRegionalMaterial(
                    seed, x, y, z,
                    _blocks, _surfaceRegionThresholds,
                    _surfaceRegionMaterialDomain,
                    _surfaceRegionalDetailNoiseDomain,
                    _surfaceRegionalDetailMaterialDomain,
                    latticeCache);
                return true;
            }

            var position = WarpCoordinates(seed, x, y, z, latticeCache);
            block = SelectMaterial(
                seed, position, _blocks,
                _surfaceMaterialDomains, _surfaceDetailDomains, _surfaceMicroDomains,
                _surfaceClusterDomains, _surfaceGrainDomains, _surfaceBiases,
                latticeCache);
            return true;
        }

        private static SpatialPosition RotateCoordinates(int x, int y, int z)
        {
            // Rotate the continuous domain so no noise axis follows a world
            // axis. This matters for vertical walls as well as horizontal cuts.
            const double cosineY = 0.8660254037844386d;
            const double sineY = 0.5d;
            const double cosineX = 0.9396926207859084d;
            const double sineX = 0.3420201433256687d;

            return new SpatialPosition(
                cosineY * x + sineY * sineX * y + sineY * cosineX * z,
                cosineX * y - sineX * z,
                -sineY * x + cosineY * sineX * y + cosineY * cosineX * z);
        }

        private static SpatialPosition RotateFieldPosition(
            SpatialPosition position, int orientation)
        {
            // Each noise scale uses a different orthogonal basis. Reusing one
            // basis for every octave left coincident boundary directions even
            // after adding finer noise, which appeared as aligned mineral bands.
            switch (orientation)
            {
                case 0:
                    return position;
                case 1:
                {
                    // Rotate around Y by 37°, then X by 29°.
                    const double cy = 0.7986355100472928d;
                    const double sy = 0.6018150231520483d;
                    const double cx = 0.8746197071393957d;
                    const double sx = 0.4848096202463371d;
                    var x1 = cy * position.X + sy * position.Z;
                    var z1 = -sy * position.X + cy * position.Z;
                    return new SpatialPosition(
                        x1,
                        cx * position.Y - sx * z1,
                        sx * position.Y + cx * z1);
                }
                case 2:
                {
                    // Rotate around Z by 53°, then Y by -29°.
                    const double cz = 0.6018150231520483d;
                    const double sz = 0.7986355100472928d;
                    const double cy = 0.8746197071393957d;
                    const double sy = -0.4848096202463371d;
                    var x1 = cz * position.X - sz * position.Y;
                    var y1 = sz * position.X + cz * position.Y;
                    return new SpatialPosition(
                        cy * x1 + sy * position.Z,
                        y1,
                        -sy * x1 + cy * position.Z);
                }
                case 3:
                {
                    // Intermediate cluster field uses the former fine-grain basis.
                    const double cz = 0.9063077870366499d;
                    const double sz = 0.4226182617406994d;
                    const double cy = 0.6427876096865394d;
                    const double sy = 0.766044443118978d;
                    const double cx = 0.7313537016191705d;
                    const double sx = 0.6819983600624985d;
                    var x1 = cz * position.X - sz * position.Y;
                    var y1 = sz * position.X + cz * position.Y;
                    var x2 = cy * x1 + sy * position.Z;
                    var z2 = -sy * x1 + cy * position.Z;
                    return new SpatialPosition(
                        x2,
                        cx * y1 - sx * z2,
                        sx * y1 + cx * z2);
                }
                default:
                {
                    // The finest grain uses a fourth independent basis.
                    const double cz = 0.8191520442889918d;
                    const double sz = 0.573576436351046d;
                    const double cy = 0.7313537016191705d;
                    const double sy = 0.6819983600624985d;
                    const double cx = 0.7986355100472928d;
                    const double sx = 0.6018150231520483d;
                    var x1 = cz * position.X - sz * position.Y;
                    var y1 = sz * position.X + cz * position.Y;
                    var x2 = cy * x1 + sy * position.Z;
                    var z2 = -sy * x1 + cy * position.Z;
                    return new SpatialPosition(
                        x2,
                        cx * y1 - sx * z2,
                        sx * y1 + cx * z2);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static double SampleNoise(
            WorldGenerationEntropy.ValueNoise3DCoordinates coordinates,
            ulong seed,
            GenerationDomain domain,
            WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache) =>
            latticeCache is null
                ? coordinates.Sample(seed, domain)
                : coordinates.Sample(seed, domain, latticeCache);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private SpatialPosition WarpCoordinates(
            ulong seed, int x, int y, int z,
            WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache)
        {
            var rotated = RotateCoordinates(x, y, z);
            var warpCoordinates =
                WorldGenerationEntropy.PrepareValueNoise3D(
                    rotated.X, rotated.Y, rotated.Z,
                    _warpHorizontalScale, _warpVerticalScale);
            var warpX = SampleNoise(warpCoordinates, seed, _warpDomains[0], latticeCache);
            var warpY = SampleNoise(warpCoordinates, seed, _warpDomains[1], latticeCache);
            var warpZ = SampleNoise(warpCoordinates, seed, _warpDomains[2], latticeCache);

            // A second, smaller-scale warp breaks the broad domain warp's
            // smooth directional bends. It is sampled in another basis and is
            // strong enough to bend material boundaries, not to randomize voxels.
            var detailPosition = RotateFieldPosition(rotated, 2);
            var detailWarpCoordinates =
                WorldGenerationEntropy.PrepareValueNoise3D(
                    detailPosition.X, detailPosition.Y, detailPosition.Z,
                    _detailWarpScale, _detailWarpVerticalScale);
            var detailWarpX =
                SampleNoise(detailWarpCoordinates, seed, _warpDetailDomains[0], latticeCache);
            var detailWarpY =
                SampleNoise(detailWarpCoordinates, seed, _warpDetailDomains[1], latticeCache);
            var detailWarpZ =
                SampleNoise(detailWarpCoordinates, seed, _warpDetailDomains[2], latticeCache);

            return new SpatialPosition(
                rotated.X + warpX * _warpHorizontalStrength + detailWarpX * _detailWarpStrength,
                rotated.Y + warpY * _warpVerticalStrength + detailWarpY * _detailWarpVerticalStrength,
                rotated.Z + warpZ * _warpHorizontalStrength + detailWarpZ * _detailWarpStrength);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private SpatialPosition WarpRegionalCoordinates(
            ulong seed,
            int x,
            int y,
            int z,
            WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache)
        {
            var rotated = RotateCoordinates(x, y, z);
            if (_regionWarpStrength == 0d)
                return rotated;

            var coordinates = WorldGenerationEntropy.PrepareValueNoise3D(
                rotated.X,
                rotated.Y,
                rotated.Z,
                _detailScale,
                _detailVerticalScale);
            var warpX = SampleNoise(
                coordinates, seed, _regionWarpDomains[0], latticeCache);
            var warpY = SampleNoise(
                coordinates, seed, _regionWarpDomains[1], latticeCache);
            var warpZ = SampleNoise(
                coordinates, seed, _regionWarpDomains[2], latticeCache);

            // One smooth 3D vector field bends the material-cell grid. Cell
            // identity and material choice remain anchored to world space, so
            // neighboring chunks independently resolve identical regions.
            return new SpatialPosition(
                rotated.X + warpX * _regionScale * _regionWarpStrength,
                rotated.Y + warpY * _regionVerticalScale * _regionWarpStrength,
                rotated.Z + warpZ * _regionScale * _regionWarpStrength);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private SpatialPosition WarpRegionalEdges(
            ulong seed,
            SpatialPosition position,
            WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache)
        {
            // The macro region remains the material owner. Only voxels close
            // to a cell face evaluate this smaller-scale vector field, so the
            // expensive detail work is concentrated on boundaries rather than
            // repeated throughout every solid voxel.
            const double edgeBand = 0.12d;
            const double maximumDisplacement = 0.10d;

            var scaledX = position.X / _regionScale;
            var scaledY = position.Y / _regionVerticalScale;
            var scaledZ = position.Z / _regionScale;
            var fractionX = scaledX - Math.Floor(scaledX);
            var fractionY = scaledY - Math.Floor(scaledY);
            var fractionZ = scaledZ - Math.Floor(scaledZ);
            var distanceX = Math.Min(fractionX, 1d - fractionX);
            var distanceY = Math.Min(fractionY, 1d - fractionY);
            var distanceZ = Math.Min(fractionZ, 1d - fractionZ);
            var weightX = Math.Clamp(1d - distanceX / edgeBand, 0d, 1d);
            var weightY = Math.Clamp(1d - distanceY / edgeBand, 0d, 1d);
            var weightZ = Math.Clamp(1d - distanceZ / edgeBand, 0d, 1d);
            weightX *= weightX * (3d - 2d * weightX);
            weightY *= weightY * (3d - 2d * weightY);
            weightZ *= weightZ * (3d - 2d * weightZ);
            var nearX = weightX > 0d;
            var nearY = weightY > 0d;
            var nearZ = weightZ > 0d;

            if (!nearX && !nearY && !nearZ)
                return position;

            var coordinates = WorldGenerationEntropy.PrepareValueNoise3D(
                position.X,
                position.Y,
                position.Z,
                _regionalEdgeScale,
                _regionalEdgeVerticalScale);
            var x = position.X;
            var y = position.Y;
            var z = position.Z;
            if (nearX)
            {
                x += SampleNoise(
                    coordinates, seed, _regionalEdgeDomains[0], latticeCache) *
                    (_regionScale * maximumDisplacement * weightX);
            }
            if (nearY)
            {
                y += SampleNoise(
                    coordinates, seed, _regionalEdgeDomains[1], latticeCache) *
                    (_regionVerticalScale * maximumDisplacement * weightY);
            }
            if (nearZ)
            {
                z += SampleNoise(
                    coordinates, seed, _regionalEdgeDomains[2], latticeCache) *
                    (_regionScale * maximumDisplacement * weightZ);
            }

            return new SpatialPosition(x, y, z);
        }

        private BlockRuntimeId SelectRegionalMaterial(
            ulong seed,
            int x,
            int y,
            int z,
            BlockRuntimeId[] candidateBlocks,
            double[] cumulativeThresholds,
            GenerationDomain materialDomain,
            GenerationDomain detailNoiseDomain,
            GenerationDomain detailMaterialDomain,
            WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache)
        {
            var position = WarpRegionalCoordinates(
                seed, x, y, z, latticeCache);
            if (_samplingMode == BiomeGeologySamplingMode.RegionalHybrid)
            {
                position = WarpRegionalEdges(seed, position, latticeCache);
            }

            var cellX = FloorToInt(position.X / _regionScale);
            var cellY = FloorToInt(position.Y / _regionVerticalScale);
            var cellZ = FloorToInt(position.Z / _regionScale);
            var selectionHash = WorldGenerationEntropy.Sample3D(
                seed, materialDomain, cellX, cellY, cellZ);
            var unit = (selectionHash >> 11) *
                (1d / 9007199254740992d);

            var selectedIndex = cumulativeThresholds.Length - 1;
            for (var index = 0; index < cumulativeThresholds.Length; index++)
            {
                if (unit < cumulativeThresholds[index])
                {
                    selectedIndex = index;
                    break;
                }
            }

            var selected = candidateBlocks[selectedIndex];
            if (_samplingMode != BiomeGeologySamplingMode.RegionalHybrid ||
                candidateBlocks.Length < 2)
                return selected;

            // The large regional choice owns most of the formation. A second,
            // much smaller 3D field introduces sparse mineral inclusions
            // inside it; the mask is continuous, while the inclusion's
            // material is stable per detail cell and independent of chunk
            // traversal order.
            var detailCoordinates = WorldGenerationEntropy.PrepareValueNoise3D(
                position.X, position.Y, position.Z,
                _regionalDetailScale, _regionalDetailVerticalScale);
            const double inclusionThreshold = 0.60d;
            if (SampleNoise(
                    detailCoordinates, seed, detailNoiseDomain, latticeCache) <=
                inclusionThreshold)
                return selected;

            var detailCellScale = _regionalDetailScale * 2u;
            var detailCellVerticalScale = _regionalDetailVerticalScale * 2u;
            var detailCellX = FloorToInt(position.X / detailCellScale);
            var detailCellY = FloorToInt(position.Y / detailCellVerticalScale);
            var detailCellZ = FloorToInt(position.Z / detailCellScale);
            var detailHash = WorldGenerationEntropy.Sample3D(
                seed, detailMaterialDomain,
                detailCellX, detailCellY, detailCellZ);
            var detailUnit = (detailHash >> 11) *
                (1d / 9007199254740992d);
            return SelectRegionalDetailMaterial(
                detailUnit, candidateBlocks, cumulativeThresholds,
                selectedIndex);
        }

        private static BlockRuntimeId SelectRegionalDetailMaterial(
            double sample,
            BlockRuntimeId[] candidateBlocks,
            double[] cumulativeThresholds,
            int excludedIndex)
        {
            // Preserve authored mineral weights when selecting an inclusion.
            // Re-normalize only the alternatives, excluding the macro cell's
            // selected material so the finer layer always adds contrast.
            var previousThreshold = 0d;
            var excludedProbability = 0d;
            for (var index = 0; index < cumulativeThresholds.Length; index++)
            {
                var threshold = cumulativeThresholds[index];
                if (index == excludedIndex)
                    excludedProbability = threshold - previousThreshold;
                previousThreshold = threshold;
            }

            var remainingProbability = Math.Max(0d, 1d - excludedProbability);
            if (remainingProbability <= 1e-12d)
                return candidateBlocks[excludedIndex];

            var target = sample * remainingProbability;
            var cumulative = 0d;
            previousThreshold = 0d;
            var fallbackIndex = -1;
            for (var index = 0; index < cumulativeThresholds.Length; index++)
            {
                var threshold = cumulativeThresholds[index];
                var probability = threshold - previousThreshold;
                previousThreshold = threshold;
                if (index == excludedIndex)
                    continue;

                fallbackIndex = index;
                cumulative += probability;
                if (target < cumulative)
                    return candidateBlocks[index];
            }

            return candidateBlocks[fallbackIndex >= 0
                ? fallbackIndex
                : excludedIndex];
        }

        private static double[] CumulativeThresholds(
            IReadOnlyList<double> probabilities)
        {
            var thresholds = new double[probabilities.Count];
            var cumulative = 0d;
            for (var index = 0; index < probabilities.Count; index++)
            {
                cumulative += probabilities[index];
                thresholds[index] = cumulative;
            }

            // Authored weights are normalized; pin the last threshold to one
            // to absorb floating-point summation error.
            thresholds[^1] = 1d;
            return thresholds;
        }

        private static int FloorToInt(double value)
        {
            var floor = Math.Floor(value);
            if (floor <= int.MinValue)
                return int.MinValue;
            if (floor >= int.MaxValue)
                return int.MaxValue;
            return (int)floor;
        }

        private BlockRuntimeId SelectMaterial(
            ulong seed,
            SpatialPosition position,
            BlockRuntimeId[] candidateBlocks,
            GenerationDomain[] broadDomains,
            GenerationDomain[] detailDomains,
            GenerationDomain[] microDomains,
            GenerationDomain[] clusterDomains,
            GenerationDomain[] grainDomains,
            double[] biases,
            WorldGenerationEntropy.ValueNoise3DLatticeCache? latticeCache)
        {
            // Fine-grain field introduces small three-dimensional pockets so
            // large formations cannot keep a single material through long
            // vertical columns merely because the broad fields change slowly.
            var broadPosition = RotateFieldPosition(position, 0);
            var detailPosition = RotateFieldPosition(position, 1);
            var microPosition = RotateFieldPosition(position, 2);
            // The cluster field is optional. Avoid rotating its coordinate
            // basis for biomes that do not use it; their grain basis remains
            // the established orientation 3.
            var clusterPosition = _clusterInfluence > 0d
                ? RotateFieldPosition(position, 3)
                : default;
            var grainPosition = RotateFieldPosition(
                position, _clusterInfluence > 0d ? 4 : 3);
            var broadCoordinates =
                WorldGenerationEntropy.PrepareValueNoise3D(
                    broadPosition.X, broadPosition.Y, broadPosition.Z,
                    _selectionScale,
                    _selectionVerticalScale);
            var detailCoordinates =
                WorldGenerationEntropy.PrepareValueNoise3D(
                    detailPosition.X, detailPosition.Y, detailPosition.Z,
                    _detailScale, _detailVerticalScale);
            var microCoordinates =
                WorldGenerationEntropy.PrepareValueNoise3D(
                    microPosition.X, microPosition.Y, microPosition.Z,
                    _microScale, _microVerticalScale);
            var clusterCoordinates = _clusterInfluence > 0d
                ? WorldGenerationEntropy.PrepareValueNoise3D(
                    clusterPosition.X, clusterPosition.Y, clusterPosition.Z,
                    _clusterScale, _clusterVerticalScale)
                : default;
            var grainCoordinates =
                WorldGenerationEntropy.PrepareValueNoise3D(
                    grainPosition.X, grainPosition.Y, grainPosition.Z,
                    _grainScale, _grainVerticalScale);
            var needsBoundaryScores =
                _boundaryInfluence > 0d && candidateBlocks.Length > 1;
            // Most geology selections need only the winning score. Allocate
            // score vectors only for definitions that explicitly enable
            // boundary roughening; ordinary geological voxels allocate none.
            // Boundary roughening needs both vectors, but allocating two
            // arrays for every solid voxel creates heavy GC pressure during
            // chunk synthesis. Candidate sets are normally small, so keep the
            // scratch data on this call's stack and retain a heap fallback for
            // unusually large authored material sets.
            const int StackScoreCapacity = 16;
            // Do not reserve per-call stack scratch for the overwhelmingly
            // common path that only needs the winning material score.
            Span<double> scores =
                needsBoundaryScores && candidateBlocks.Length <= StackScoreCapacity
                    ? stackalloc double[candidateBlocks.Length]
                    : Span<double>.Empty;
            Span<double> grainValues =
                needsBoundaryScores && candidateBlocks.Length <= StackScoreCapacity
                    ? stackalloc double[candidateBlocks.Length]
                    : Span<double>.Empty;
            if (needsBoundaryScores && candidateBlocks.Length > StackScoreCapacity)
            {
                scores = new double[candidateBlocks.Length];
                grainValues = new double[candidateBlocks.Length];
            }

            var bestScore = double.NegativeInfinity;
            var bestIndex = 0;
            for (var index = 0; index < candidateBlocks.Length; index++)
            {
                // Every weighted noise component lies in [-1, 1], their
                // coefficients sum to one, and the resulting Gumbel term is
                // upper-bounded by MaximumGumbelScore. If the best score
                // already reaches this candidate's bound, sampling its five
                // fields cannot change the winner. Boundary roughening keeps
                // the full candidate vector and deliberately bypasses pruning.
                if (!needsBoundaryScores &&
                    bestScore >= biases[index] + MaximumGumbelScore)
                    continue;

                var broad =
                    SampleNoise(broadCoordinates, seed, broadDomains[index], latticeCache);
                var detail =
                    SampleNoise(detailCoordinates, seed, detailDomains[index], latticeCache);
                var micro =
                    SampleNoise(microCoordinates, seed, microDomains[index], latticeCache);
                var cluster = _clusterInfluence > 0d
                    ? SampleNoise(clusterCoordinates, seed, clusterDomains[index], latticeCache)
                    : 0d;
                var grain =
                    SampleNoise(grainCoordinates, seed, grainDomains[index], latticeCache);

                // Split finer detail across intermediate clusters and very
                // fine grain. The former creates coherent mineral pockets;
                // the latter still nicks their boundaries to avoid long bands.
                var field =
                    broad * (_remainingInfluence * 0.04d / 0.30d) +
                    detail * (_remainingInfluence * 0.10d / 0.30d) +
                    micro * (_remainingInfluence * 0.16d / 0.30d) +
                    cluster * _clusterInfluence +
                    grain * _grainInfluence;
                var unit = Math.Clamp(
                    (field + 1d) * 0.5d, 1e-6d, 1d - 1e-6d);
                var gumbel = -Math.Log(-Math.Log(unit));
                var score = gumbel + biases[index];
                if (needsBoundaryScores)
                {
                    scores[index] = score;
                    grainValues[index] = grain;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            // Ocean-like biomes may use the finest field only as an extra
            // perturbation near material boundaries. That keeps deposit
            // interiors coherent while letting high-frequency grain roughen
            // edges where the leading candidates are already competitive.
            if (_boundaryInfluence > 0d && candidateBlocks.Length > 1)
            {
                var secondBestScore = double.NegativeInfinity;
                for (var index = 0; index < candidateBlocks.Length; index++)
                {
                    if (index != bestIndex)
                        secondBestScore = Math.Max(secondBestScore, scores[index]);
                }

                var margin = bestScore - secondBestScore;
                // Let edge roughening affect a slightly wider transition
                // zone so large deposits do not leave occasional straight runs,
                // while keeping the perturbation absent in confident interiors.
                var boundaryFactor = Math.Clamp(
                    1d - margin / (3d * _boundaryInfluence),
                    0d,
                    1d);
                if (boundaryFactor > 0d)
                {
                    bestScore = double.NegativeInfinity;
                    bestIndex = 0;
                    for (var index = 0; index < candidateBlocks.Length; index++)
                    {
                        var score = scores[index] +
                            grainValues[index] * _boundaryInfluence * boundaryFactor;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestIndex = index;
                        }
                    }
                }
            }

            return candidateBlocks[bestIndex];
        }

        public static ResolvedGeology Create(
            ulong seed,
            string biomeId,
            BiomeGeologyDefinition definition,
            BlockRegistry blocks) =>
            new ResolvedGeology(
                seed,
                definition,
                blocks.GetId(definition.BaseBlock),
                definition.Blocks.Select(blocks.GetId).ToArray(),
                biomeId);
    }

    private sealed class MaterialRule
    {
        // Broad X/Z variation keeps stratum boundaries irregular without
        // introducing a second, high-frequency signal that reads as banding.
        private const uint BoundaryNoiseScale = 48;
        private MaterialRule(
            ResolvedLayer[] layers,
            GenerationDomain[] boundaryDomains,
            bool usesBaseSurface)
        {
            Layers = layers;
            BoundaryDomains = boundaryDomains;
            UsesBaseSurface = usesBaseSurface;
            HasConditions = layers.Any(layer => layer.HasConditions);
            RequiresSlope = layers.Any(layer => layer.RequiresSlope);
            CoreBlock = layers[^1].Block;
            var nominalCoreDepth = layers.Length > 1
                ? layers[^2].EndDepthExclusive!.Value
                : 0u;
            CoreStartDepth = checked(
                nominalCoreDepth +
                (layers.Length > 1 ? layers[^2].BoundaryNoise : 0u));
        }

        private ResolvedLayer[] Layers { get; }
        private GenerationDomain[] BoundaryDomains { get; }

        public bool HasConditions { get; }

        public bool RequiresSlope { get; }

        public bool UsesBaseSurface { get; }
        public BlockRuntimeId CoreBlock { get; }
        public uint CoreStartDepth { get; }
        public bool MayResolveAir => Layers.Any(layer => layer.MayResolveAir);

        public BlockRuntimeId BlockAtDepth(
            ulong seed,
            int worldX,
            int worldZ,
            uint depth,
            SurfacePlacementContext? placement)
        {
            var previousEndDepth = 0u;
            for (var index = 0; index < Layers.Length; index++)
            {
                var layer = Layers[index];
                if (layer.EndDepthExclusive is { } nominalEndDepth)
                {
                    var endDepth = AdjustedEndDepth(
                        seed, worldX, worldZ, index,
                        nominalEndDepth, previousEndDepth);
                    previousEndDepth = endDepth;
                    if (depth >= endDepth)
                        continue;
                }

                return layer.Resolve(
                    seed, worldX, worldZ, placement);
            }

            throw new InvalidOperationException(
                "Validated surface material profile must end with a core layer.");
        }

        public BlockRuntimeId VolumeBlockAt(
            ulong seed, int x, int y, int z, uint depth,
            SurfacePlacementContext? placement)
        {
            if (depth >= CoreStartDepth)
                return CoreBlock;

            var previousEndDepth = 0u;
            for (var index = 0; index < Layers.Length; index++)
            {
                var layer = Layers[index];
                if (layer.EndDepthExclusive is { } nominalEndDepth)
                {
                    var endDepth = AdjustedEndDepth(
                        seed, x, z, index, nominalEndDepth, previousEndDepth);
                    previousEndDepth = endDepth;
                    if (depth >= endDepth)
                        continue;
                }
                return layer.ResolveVolume(seed, x, y, z, placement);
            }
            throw new InvalidOperationException(
                "Validated palette must end with a core layer.");
        }

        public BiomeSurfaceMaterialColumn SampleColumn(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement,
            BlockRuntimeId surfaceOverride)
        {
            var resolvedLayers =
                new BiomeSurfaceMaterialColumn.Layer[Layers.Length];

            var previousEndDepth = 0u;
            for (var index = 0; index < Layers.Length; index++)
            {
                var layer = Layers[index];
                var endDepth = layer.EndDepthExclusive is { } nominalEndDepth
                    ? AdjustedEndDepth(
                        seed, worldX, worldZ, index,
                        nominalEndDepth, previousEndDepth)
                    : uint.MaxValue;
                if (endDepth != uint.MaxValue)
                    previousEndDepth = endDepth;
                resolvedLayers[index] =
                    new BiomeSurfaceMaterialColumn.Layer(
                        endDepth,
                        layer.Resolve(seed, worldX, worldZ, placement));
            }

            return new BiomeSurfaceMaterialColumn(
                resolvedLayers, surfaceOverride);
        }

        private uint AdjustedEndDepth(
            ulong seed,
            int worldX,
            int worldZ,
            int layerIndex,
            uint nominalEndDepth,
            uint previousEndDepth)
        {
            var amplitude = Layers[layerIndex].BoundaryNoise;
            var displacement = 0;
            if (amplitude > 0)
            {
                var broadNoise = WorldGenerationEntropy.ValueNoise2D(
                    seed, BoundaryDomains[layerIndex], worldX, worldZ,
                    BoundaryNoiseScale);
                displacement = (int)Math.Round(
                    broadNoise * amplitude,
                    MidpointRounding.AwayFromZero);
            }
            var displaced = Math.Max(
                (long)nominalEndDepth + displacement,
                (long)previousEndDepth + 1L);
            return checked((uint)displaced);
        }

        public static MaterialRule Create(
            BiomeDefinition biome,
            BlockRegistry blocks,
            IReadOnlyList<BiomeSurfaceLayerDefinition> profile,
            string? face)
        {
            var finiteDepth = 0u;
            var layers =
                new ResolvedLayer[
                    profile.Count];

            for (var index = 0;
                 index < layers.Length;
                 index++)
            {
                var definition =
                    profile[index];
                uint? endDepth = null;

                if (definition.Depth is
                    { } depth)
                {
                    finiteDepth =
                        checked(
                            finiteDepth +
                            depth);
                    endDepth =
                        finiteDepth;
                }

                layers[index] =
                    new ResolvedLayer(
                        blocks.GetId(
                            definition.Block),
                        endDepth,
                        definition.BoundaryNoise,
                        definition.Patch is
                            { } patch
                            ? ResolvedPatch.Create(
                                face is null ? biome.Id : $"{biome.Id}/{face}",
                                index,
                                patch,
                                blocks)
                            : null);
            }

            var boundaryDomains = new GenerationDomain[layers.Length];
            var profileName = face is null
                ? biome.Id
                : $"{biome.Id}/{face}";
            for (var index = 0; index < layers.Length - 1; index++)
            {
                boundaryDomains[index] = GenerationDomain.Named(
                    $"material/surface-strata-boundary/v1/{profileName}/{index}");
            }

            return new MaterialRule(
                layers,
                boundaryDomains,
                biome.SurfaceLayout is not null);
        }
    }

    private sealed class ResolvedLayer
    {
        public ResolvedLayer(
            BlockRuntimeId block,
            uint? endDepthExclusive,
            uint boundaryNoise,
            ResolvedPatch? patch)
        {
            Block = block;
            EndDepthExclusive =
                endDepthExclusive;
            BoundaryNoise = boundaryNoise;
            Patch = patch;
        }

        public BlockRuntimeId Block { get; }

        public uint? EndDepthExclusive { get; }

        public uint BoundaryNoise { get; }

        private ResolvedPatch? Patch { get; }

        public bool HasConditions => Patch?.HasConditions == true;
        public bool MayResolveAir => Block.IsAir || Patch?.MayResolveAir == true;

        public bool RequiresSlope => Patch?.RequiresSlope == true;

        public BlockRuntimeId ResolveVolume(
            ulong seed, int x, int y, int z,
            SurfacePlacementContext? placement) =>
            Patch?.ResolveVolume(seed, x, y, z, placement) ?? Block;

        public BlockRuntimeId Resolve(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement) =>
            Patch?.Resolve(
                seed,
                worldX,
                worldZ,
                placement) ??
            Block;
    }

    private sealed class ResolvedPatch
    {
        private readonly BiomeSurfacePatchDefinition _definition;
        private readonly BlockRuntimeId[] _blocks;
        private readonly double[] _cumulativeWeights;
        private readonly double _totalWeight;
        private readonly GenerationDomain _shapeDomain;
        private readonly GenerationDomain _detailDomain;
        private readonly GenerationDomain _blockDomain;
        private readonly GenerationDomain _warpXDomain;
        private readonly GenerationDomain _warpZDomain;
        private readonly GenerationDomain _volumeMaskDomain;
        private readonly GenerationDomain _volumeSelectionDomain;

        private ResolvedPatch(
            BiomeSurfacePatchDefinition definition,
            BlockRuntimeId[] blocks,
            string suffix)
        {
            _definition = definition;
            _blocks = blocks;
            _cumulativeWeights = new double[blocks.Length];
            var total = 0d;
            for (var index = 0; index < blocks.Length; index++)
            {
                total += definition.BlockWeights[index];
                _cumulativeWeights[index] = total;
            }

            _totalWeight = total;
            _shapeDomain = GenerationDomain.Named(
                $"material/surface-patch/shape/v2/{suffix}");
            _detailDomain = GenerationDomain.Named(
                $"material/surface-patch/detail/v2/{suffix}");
            _blockDomain = GenerationDomain.Named(
                $"material/surface-patch/block/v2/{suffix}");
            _warpXDomain = GenerationDomain.Named(
                $"material/surface-patch/warp-x/v1/{suffix}");
            _warpZDomain = GenerationDomain.Named(
                $"material/surface-patch/warp-z/v1/{suffix}");
            _volumeMaskDomain = GenerationDomain.Named(
                $"material/volume-patch/mask/v1/{suffix}");
            _volumeSelectionDomain = GenerationDomain.Named(
                $"material/volume-patch/selection/v1/{suffix}");
        }

        public BlockRuntimeId? ResolveVolume(
            ulong seed, int x, int y, int z,
            SurfacePlacementContext? placement)
        {
            if (_definition.Conditions is { } conditions &&
                (placement is not { } context ||
                 !conditions.Allows(context)))
                return null;

            var verticalScale = Math.Max(2u, _definition.DetailScale);
            var noise = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeMaskDomain, x, y, z,
                _definition.Scale, verticalScale);
            if (noise < 1d - _definition.Coverage * 2d)
                return null;

            if (_blocks.Length == 1)
                return _blocks[0];

            var selection = WorldGenerationEntropy.ValueNoise3D(
                seed, _volumeSelectionDomain, x, y, z,
                _definition.SelectionScale, verticalScale);
            var unit = Math.Clamp((selection + 1d) * 0.5d,
                0d, 0.999999999999d);
            var target = unit * _totalWeight;
            for (var i = 0; i < _blocks.Length; i++)
            {
                if (target < _cumulativeWeights[i])
                    return _blocks[i];
            }
            return _blocks[^1];
        }

        public bool HasConditions => _definition.Conditions is not null;
        public bool RequiresSlope => _definition.Conditions?.RequiresSlope == true;

        public BlockRuntimeId? Resolve(
            ulong seed,
            int worldX,
            int worldZ,
            SurfacePlacementContext? placement)
        {
            if (_definition.Conditions is { } conditions &&
                (placement is not { } context ||
                 !conditions.Allows(context)))
            {
                return null;
            }

            var x = (double)worldX;
            var z = (double)worldZ;
            if (_definition.WarpStrength > 0d)
            {
                var dx = WorldGenerationEntropy.SmoothNoise2D(
                    seed, _warpXDomain, x, z, _definition.WarpScale);
                var dz = WorldGenerationEntropy.SmoothNoise2D(
                    seed, _warpZDomain, x + 19.7d, z - 11.3d,
                    _definition.WarpScale);
                x += dx * _definition.WarpStrength;
                z += dz * _definition.WarpStrength;
            }

            z /= _definition.StretchZ;
            var morphed = _definition.WarpStrength > 0d ||
                _definition.StretchZ != 1d;
            var macro = Noise(
                seed, _shapeDomain, worldX, worldZ, x, z,
                _definition.Scale, morphed);
            var detail = Noise(
                seed, _detailDomain, worldX, worldZ, x, z,
                _definition.DetailScale, morphed);
            var field = (macro + detail * _definition.Roughness) /
                (1d + _definition.Roughness);
            if (field < 1d - _definition.Coverage * 2d)
            {
                return null;
            }

            if (_blocks.Length == 1)
            {
                return _blocks[0];
            }

            var selection = Noise(
                seed, _blockDomain, worldX, worldZ, x, z,
                _definition.SelectionScale, morphed);
            var unit = Math.Clamp(
                (selection + 1d) * 0.5d,
                0d,
                0.999999999999d);
            var target = unit * _totalWeight;
            for (var index = 0; index < _blocks.Length; index++)
            {
                if (target < _cumulativeWeights[index])
                {
                    return _blocks[index];
                }
            }

            return _blocks[^1];
        }

        public bool MayResolveAir => _blocks.Any(block => block.IsAir);

        private static double Noise(
            ulong seed,
            GenerationDomain domain,
            int worldX,
            int worldZ,
            double x,
            double z,
            uint scale,
            bool morphed) =>
            morphed
                ? WorldGenerationEntropy.SmoothNoise2D(
                    seed, domain, x, z, scale)
                : WorldGenerationEntropy.ValueNoise2D(
                    seed, domain, worldX, worldZ, scale);

        public static ResolvedPatch Create(
            string biomeId,
            int layerIndex,
            BiomeSurfacePatchDefinition definition,
            BlockRegistry blocks) =>
            new(
                definition,
                definition.Blocks.Select(blocks.GetId).ToArray(),
                $"{biomeId}/{layerIndex}");
    }
}


/// <summary>
/// A resolved X/Z material profile; patch decisions are constant for an
/// authored layer and are evaluated once, not for every voxel depth.
/// </summary>
public sealed class BiomeSurfaceMaterialColumn
{
    internal readonly record struct Layer(
        uint EndDepthExclusive,
        BlockRuntimeId Block);

    private readonly Layer[] _layers;
    private readonly uint _finiteDepth;
    private readonly BlockRuntimeId _surfaceOverride;

    internal BiomeSurfaceMaterialColumn(
        Layer[] layers,
        BlockRuntimeId surfaceOverride = default)
    {
        _layers = layers;
        _finiteDepth = layers.Length > 1
            ? layers[^2].EndDepthExclusive
            : 0u;
        _surfaceOverride = surfaceOverride;
    }

    public uint FiniteDepth => _finiteDepth;

    public BlockRuntimeId BlockAt(uint depth)
    {
        if (depth == 0 && !_surfaceOverride.IsAir)
            return _surfaceOverride;

        // Most terrain voxels are below the finite authored cover. Resolve
        // those directly to the core layer instead of scanning every layer.
        if (depth >= _finiteDepth)
            return _layers[^1].Block;

        for (var i = 0; i < _layers.Length - 1; i++)
        {
            if (depth < _layers[i].EndDepthExclusive)
                return _layers[i].Block;
        }

        throw new InvalidOperationException(
            "Validated surface material profile must end with a core layer.");
    }
}
