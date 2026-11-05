#if DEBUG
using System.Diagnostics;
#endif
using System.Threading;

namespace Asteria.Core.World;

internal enum BiomeRegionOwnerKind : byte
{
    Surface,
    Volume,
}

internal readonly record struct BiomeRegionOwner(
    BiomeSample Biome,
    BiomeRegionOwnerKind Kind);

/// <summary>
/// Single authoritative terrain field: a surface crossing plus optional
/// subtractive cave layers and additive biome-authored density formations.
/// Biomes and height are sampled once per X/Z column, never per Y voxel.
/// </summary>
public sealed class SurfaceTerrainField
{
    // A self-tapering formation can still meet another biome before its
    // radial strength reaches zero. Blend over a gradual ownership shoulder
    // rather than leaving a near-vertical wall at its edge.

    private readonly ulong _seed;
    private readonly int _seaLevel;
    private readonly int? _floorY;
    private readonly int? _roofY;
    private readonly BiomeField _surfaceBiomes;
    private readonly VolumeBiomeField _volumeBiomes;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly IReadOnlyDictionary<string, SurfaceTerrainRule> _surfaceRules;
    private readonly IReadOnlyDictionary<string, AdditiveRuleSet> _additiveRules;
    private readonly CoastProfileRule? _coastProfile;
    private readonly CaveRule? _caves;
    private readonly WorldGenerationMode _mode;
    private readonly int _planarSurfaceY;

    public SurfaceTerrainField(
        ulong seed,
        DimensionDefinition dimension,
        BiomeField surfaceBiomes,
        VolumeBiomeField volumeBiomes,
        GeneratedFluidField generatedFluids,
        IEnumerable<BiomeDefinition> surfaceDefinitions,
        IEnumerable<BiomeDefinition> volumeDefinitions,
        bool spawnCaves = true,
        bool spawnOceans = true,
        WorldGenerationMode mode = WorldGenerationMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            surfaceDefinitions);
        ArgumentNullException.ThrowIfNull(
            volumeDefinitions);
        _surfaceBiomes =
            surfaceBiomes ??
            throw new ArgumentNullException(
                nameof(surfaceBiomes));
        _volumeBiomes =
            volumeBiomes ??
            throw new ArgumentNullException(
                nameof(volumeBiomes));
        _generatedFluids =
            generatedFluids ??
            throw new ArgumentNullException(
                nameof(generatedFluids));

        _seed = seed;
        _mode = mode;
        _seaLevel = dimension.SeaLevel;
        var planarMinimum = Math.Max(1, (dimension.Shell?.FloorY ?? 0) + 2);
        var planarMaximum = dimension.Shell?.RoofY is { } roof
            ? Math.Max(planarMinimum, roof - 2)
            : int.MaxValue;
        _planarSurfaceY = Math.Clamp(
            dimension.SeaLevel, planarMinimum, planarMaximum);
        // Void is genuinely empty, including the usual shell lining.
        _floorY = mode == WorldGenerationMode.Void ? null : dimension.Shell?.FloorY;
        _roofY = mode == WorldGenerationMode.Void ? null : dimension.Shell?.RoofY;
        _surfaceRules =
            surfaceDefinitions
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    definition =>
                        definition.Id,
                    definition =>
                        new SurfaceTerrainRule(
                            definition),
                    StringComparer.Ordinal);
        _additiveRules =
            volumeDefinitions
                .Where(
                    definition =>
                        mode == WorldGenerationMode.Normal &&
                        definition.Terrain3d?.Additive.Count > 0)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    definition =>
                        definition.Id,
                    definition =>
                        new AdditiveRuleSet(
                            definition.Id,
                            definition.Terrain3d!.Additive),
                    StringComparer.Ordinal);
        _coastProfile =
            spawnOceans && dimension.GeneratedOcean is
                { } ocean
                ? new CoastProfileRule(
                    ocean.Biome,
                    ocean.Shore)
                : null;
        _caves =
            mode != WorldGenerationMode.Void &&
            spawnCaves && dimension.Caves is
                { } definition
                ? new CaveRule(
                    definition)
                : null;

        if (_surfaceRules.Count == 0)
        {
            throw new ArgumentException(
                "Surface terrain requires at least one surface biome.",
                nameof(surfaceDefinitions));
        }
    }

    // Reuse the calling materialization's biome assignments for adjacent
    // columns. Each caller owns and discards its sampler with the chunk.
    internal int SampleBaseHeight(
        BiomeField.Sampler sampler,
        int worldX,
        int worldZ) =>
        BaseHeightAt(sampler.Sample(worldX, worldZ), worldX, worldZ);

    internal (BiomeSample Biome, int BaseY) SampleBaseSurface(
        int worldX,
        int worldZ)
    {
        var biome =
            _surfaceBiomes.Sample(
                worldX,
                worldZ);

        return (
            biome,
            BaseHeightAt(
                biome,
                worldX,
                worldZ));
    }

    // Surface placement conditions sample several adjacent heights. Keep the
    // two biome assignment caches local to that one query so neighboring height
    // probes reuse seed assignments instead of allocating a sampler per point.
    internal readonly record struct TerrainSampleContext(
        BiomeField.Sampler SurfaceBiomes,
        BiomeField.Sampler? VolumePlacement);

    internal TerrainSampleContext CreateTerrainSampleContext(
        int biomeSampleCacheCapacity = 0) =>
        new(
            _surfaceBiomes.CreateSampler(biomeSampleCacheCapacity),
            _volumeBiomes.CreatePlacementSampler(biomeSampleCacheCapacity));

    internal TerrainSampleContext CreateTerrainSampleContext(
        SurfaceTerrainColumn column,
        int originX,
        int originZ,
        int biomeSampleCacheCapacity)
    {
        ArgumentNullException.ThrowIfNull(column);
        var context = CreateTerrainSampleContext(biomeSampleCacheCapacity);
        column.PrimeBiomeSamplers(
            context.SurfaceBiomes,
            context.VolumePlacement,
            originX,
            originZ);
        return context;
    }

    internal (BiomeSample Biome, int BaseY) SampleBaseSurface(
        TerrainSampleContext context,
        int worldX,
        int worldZ)
    {
        var biome = context.SurfaceBiomes.Sample(worldX, worldZ);
        return (biome, BaseHeightAt(biome, worldX, worldZ));
    }

    internal (
        BiomeSample Biome,
        int BaseY,
        int SurfaceFluidCutDepth)
        SampleBaseSurfaceWithGeneratedFluid(
            int worldX,
            int worldZ) =>
        SampleBaseSurfaceWithGeneratedFluid(
            _surfaceBiomes.CreateSampler(),
            worldX,
            worldZ);

    // Structure-placement queries often sample many nearby surface points.
    // Reuse the query-owned biome sampler instead of allocating a fresh
    // sampler and seed-assignment dictionary for every point.
    internal (
        BiomeSample Biome,
        int BaseY,
        int SurfaceFluidCutDepth)
        SampleBaseSurfaceWithGeneratedFluid(
            BiomeSample biome,
            int worldX,
            int worldZ)
    {
        ArgumentNullException.ThrowIfNull(biome);
        var resolved =
            BaseHeightAndSurfaceFluidCutAt(
                biome,
                worldX,
                worldZ);

        return (
            biome,
            resolved.BaseY,
            resolved.SurfaceFluidCutDepth);
    }

    internal (
        BiomeSample Biome,
        int BaseY,
        int SurfaceFluidCutDepth)
        SampleBaseSurfaceWithGeneratedFluid(
            BiomeField.Sampler sampler,
            int worldX,
            int worldZ)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        var biome =
            sampler.Sample(
                worldX,
                worldZ);
        var resolved =
            BaseHeightAndSurfaceFluidCutAt(
                biome,
                worldX,
                worldZ);

        return (
            biome,
            resolved.BaseY,
            resolved.SurfaceFluidCutDepth);
    }

    internal BiomeField.Sampler CreateBiomeSampler() =>
        _surfaceBiomes.CreateSampler();

    internal (
        BiomeSample Biome,
        int BaseY,
        int SurfaceFluidCutDepth,
        int SurfaceY,
        BiomeSample? Volume)
        SampleDestinationColumn(
            BiomeField.Sampler sampler,
            int worldX,
            int worldZ,
            bool resolveSurfaceHeight)
    {
        var biome = sampler.Sample(worldX, worldZ);
        var (baseY, cutDepth) =
            BaseHeightAndSurfaceFluidCutAt(biome, worldX, worldZ);
        var volume = _volumeBiomes.SamplePlacement(worldX, worldZ);
        var surfaceY = resolveSurfaceHeight
            ? FinalSurfaceHeight(biome, baseY, worldX, worldZ, volume)
            : baseY;
        return (biome, baseY, cutDepth, surfaceY, volume);
    }

    public int SurfaceHeight(int worldX, int worldZ)
    {
        var biome = _surfaceBiomes.Sample(worldX, worldZ);
        var baseY = BaseHeightAt(biome, worldX, worldZ);
        return FinalSurfaceHeight(
            biome,
            baseY,
            worldX,
            worldZ,
            _volumeBiomes.SamplePlacement(
                worldX,
                worldZ));
    }

    internal int SurfaceHeight(
        TerrainSampleContext context,
        int worldX,
        int worldZ)
    {
        var biome = context.SurfaceBiomes.Sample(worldX, worldZ);
        var baseY = BaseHeightAt(biome, worldX, worldZ);
        return FinalSurfaceHeight(
            biome,
            baseY,
            worldX,
            worldZ,
            _volumeBiomes.SamplePlacement(
                context.VolumePlacement,
                worldX,
                worldZ));
    }

    public double DensityAt(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldY));
        }

        var biome = _surfaceBiomes.Sample(worldX, worldZ);
        var baseY = BaseHeightAt(biome, worldX, worldZ);
        return DensityAt(biome, baseY, worldX, worldY, worldZ);
    }

    public BiomeSample? VolumeBiomeAt(
        int worldX,
        int worldY,
        int worldZ) =>
        _volumeBiomes.Sample(
            worldX,
            worldY,
            worldZ);

    public bool IsCaveVoidAt(
        int worldX,
        int worldY,
        int worldZ)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldY));
        }

        if (_caves is null ||
            BordersSphereShell(worldY))
        {
            return false;
        }

        var biome =
            _surfaceBiomes.Sample(
                worldX,
                worldZ);
        var baseY =
            BaseHeightAt(
                biome,
                worldX,
                worldZ);

        return IsCaveVoidAt(
            baseY,
            new WorldVoxelCoord(worldX, worldY, worldZ));
    }

    // Caller-sampled base height avoids resampling the 2D biome field at
    // every boundary voxel of a vertical chunk.
    internal bool IsCaveVoidAt(
        int baseY,
        WorldVoxelCoord position) =>
        _caves is not null &&
        position.Y > 0 &&
        !BordersSphereShell(position.Y) &&
        position.Y <= baseY &&
        CaveVoidDensityAt(
            baseY,
            position.X,
            position.Y,
            position.Z) > 0d;

    /// <summary>
    /// Resolves the biome that owns a sampled world region. Volume identity
    /// owns its authored region; otherwise the surface biome is the fallback.
    /// </summary>
    internal BiomeRegionOwner ResolveRegionOwnerAt(
        SurfaceTerrainColumn column,
        int localX,
        int localZ,
        int worldY,
        BiomeSample? volumeBiome,
        bool isEmpty,
        bool isSphereShellBoundaryOrLining)
    {
        ArgumentNullException.ThrowIfNull(column);
        return ResolveRegionOwner(
            column.BiomeAt(localX, localZ),
            volumeBiome,
            isEmpty ? column.CaveBiomeAt(localX, localZ) : null,
            column.BaseHeightAt(localX, localZ),
            column.SurfaceFluidCutDepthAt(localX, localZ),
            worldY,
            isEmpty,
            isSphereShellBoundaryOrLining);
    }

    internal static BiomeRegionOwner ResolveRegionOwner(
        BiomeSample surfaceBiome,
        BiomeSample? additiveVolumeBiome,
        BiomeSample? emptyRegionVolumeBiome,
        int baseY,
        int surfaceFluidCutDepth,
        int worldY,
        bool isEmpty,
        bool isSphereShellBoundaryOrLining)
    {
        ArgumentNullException.ThrowIfNull(surfaceBiome);

        // Empty-region ownership is selected by the volume biome field;
        // the local density result establishes that the voxel is actually
        // empty. Surface-fluid cuts and Sphere shell lining are excluded.
        if (isEmpty &&
            emptyRegionVolumeBiome is not null &&
            worldY > 0 &&
            (long)worldY <= (long)baseY - surfaceFluidCutDepth &&
            !isSphereShellBoundaryOrLining)
        {
            return new BiomeRegionOwner(
                emptyRegionVolumeBiome, BiomeRegionOwnerKind.Volume);
        }

        // An additive biome owns only cells occupied by its formation.
        if (!isEmpty && additiveVolumeBiome is not null)
        {
            return new BiomeRegionOwner(
                additiveVolumeBiome, BiomeRegionOwnerKind.Volume);
        }

        return new BiomeRegionOwner(
            surfaceBiome, BiomeRegionOwnerKind.Surface);
    }

    /// <summary>
    /// Pure density query with already-sampled X/Z biome and base height.
    /// Used by the chunk materializer across all Y voxels in a column.
    /// </summary>
    internal bool HasAdditiveDensityRule(BiomeSample? volumeBiome) =>
        volumeBiome is not null &&
        _additiveRules.ContainsKey(volumeBiome.Primary);

    internal bool RequiresVolumeBiomeForDensityBand(int originY)
    {
        if (originY < 0 || originY % Chunk.Size != 0)
            throw new ArgumentOutOfRangeException(nameof(originY));

        var maximumY = checked(originY + Chunk.Size - 1);
        foreach (var ruleSet in _additiveRules.Values)
        {
            if (ruleSet.IntersectsYRange(originY, maximumY))
                return true;
        }

        return false;
    }

    public double DensityAt(
        BiomeSample biome,
        int baseY,
        int worldX,
        int worldY,
        int worldZ) =>
        DensityAt(
            biome,
            baseY,
            worldX,
            worldY,
            worldZ,
            _volumeBiomes.SamplePlacement(
                worldX,
                worldZ));

    internal double DensityAt(
        BiomeSample biome,
        int baseY,
        int worldX,
        int worldY,
        int worldZ,
        BiomeSample? volumeBiome) =>
        DensityAt(
            biome,
            baseY,
            worldX,
            worldY,
            worldZ,
            volumeBiome,
            caveSampler: null,
            localX: 0,
            localY: 0,
            localZ: 0);

    private double DensityAt(
        BiomeSample biome,
        int baseY,
        int worldX,
        int worldY,
        int worldZ,
        BiomeSample? volumeBiome,
        CaveNoiseVolumeSampler? caveSampler,
        int localX,
        int localY,
        int localZ) =>
        DensityAt(
            biome, baseY, worldX, worldY, worldZ, volumeBiome,
            caveSampler, localX, localY, localZ,
            preResolvedAdditiveRule: null,
            additiveHorizontalSupports: default,
            additiveRuleResolved: false);

    private double DensityAt(
        BiomeSample biome,
        int baseY,
        int worldX,
        int worldY,
        int worldZ,
        BiomeSample? volumeBiome,
        CaveNoiseVolumeSampler? caveSampler,
        int localX,
        int localY,
        int localZ,
        AdditiveRuleSet? preResolvedAdditiveRule,
        ReadOnlySpan<double> additiveHorizontalSupports,
        bool additiveRuleResolved)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldY));
        }

        // The playable side of a Sphere Shell must have a solid lining.
        // This is part of terrain density, not a post-generation cave patch.
        if (IsShellLining(worldY))
        {
            return Math.Max(0d, baseY - (double)worldY);
        }

        double density =
            baseY -
            (double)worldY;

        AdditiveRuleSet? floating = preResolvedAdditiveRule;
        if (!additiveRuleResolved &&
            volumeBiome is not null &&
            _additiveRules.TryGetValue(
                volumeBiome.Primary,
                out var resolvedFloating))
        {
            floating = resolvedFloating;
        }

        if (floating is not null &&
            volumeBiome is not null &&
            worldY >= floating.MinimumY &&
            worldY <= floating.MaximumY)
        {
            var additiveDensity = additiveHorizontalSupports.IsEmpty
                ? floating.DensityAt(
                    _seed, worldX, worldY, worldZ,
                    volumeBiome.PrimaryWeight)
                : floating.DensityAt(
                    _seed, worldX, worldY, worldZ,
                    volumeBiome.PrimaryWeight,
                    additiveHorizontalSupports);
            density = Math.Max(density, additiveDensity);
        }

        if (density >= 0d &&
            worldY <= baseY)
        {
            var voidDensity =
                CaveVoidDensityAt(
                    baseY,
                    worldX,
                    worldY,
                    worldZ,
                    caveSampler,
                    localX,
                    localY,
                    localZ);

            if (voidDensity > 0d)
            {
                density =
                    Math.Min(
                        density,
                        -voidDensity);
            }
        }

        return density;
    }

    private bool IsShellLining(int worldY) =>
        (_floorY is { } floor && (long)worldY == (long)floor + 1L) ||
        (_roofY is { } roof && (long)worldY == (long)roof - 1L);

    private bool BordersSphereShell(int worldY) =>
        IsShellLining(worldY) ||
        worldY == _floorY ||
        worldY == _roofY;

    private double CaveVoidDensityAt(
        int baseY,
        int worldX,
        int worldY,
        int worldZ,
        CaveNoiseVolumeSampler? caveSampler = null,
        int localX = 0,
        int localY = 0,
        int localZ = 0)
    {
        if (_caves is null ||
            BordersSphereShell(worldY))
        {
            return 0d;
        }

        var depth =
            (long)baseY -
            worldY;

        if (depth < _caves.MinimumDepth ||
            depth > _caves.MaximumDepth)
        {
            return 0d;
        }

        return _caves.VoidDensityAt(
            _seed,
            worldX,
            worldY,
            worldZ,
            depth,
            caveSampler,
            localX,
            localY,
            localZ);
    }

    /// <summary>
    /// Number of contiguous additive solid voxels immediately above y,
    /// capped at finite authored material depth. A gap restarts surface
    /// material layering even when a higher floating mass exists.
    /// </summary>
    public uint AdditiveDepthAt(
        BiomeSample biome,
        int baseY,
        int worldX,
        int worldY,
        int worldZ,
        uint finiteDepth)
    {
        var volumeBiome =
            _volumeBiomes.SamplePlacement(
                worldX,
                worldZ);
        uint depth = 0;

        while (depth < finiteDepth)
        {
            var above = (long)worldY + depth + 1L;
            if (above > int.MaxValue ||
                DensityAt(
                    biome,
                    baseY,
                    worldX,
                    (int)above,
                    worldZ,
                    volumeBiome) < 0d)
            {
                break;
            }

            depth++;
        }

        return depth;
    }

    public TerrainDensityVolume SampleDensityVolume(
        int originX,
        int originY,
        int originZ,
        int width,
        int height,
        int depth)
    {
        if (originY < 0 ||
            width is < 1 or > Chunk.Size ||
            height is < 1 or > Chunk.Size ||
            depth is < 1 or > Chunk.Size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Density samples must be non-negative-Y and bounded to one chunk.");
        }

        _ = checked(originX + width - 1);
        _ = checked(originY + height - 1);
        _ = checked(originZ + depth - 1);

        var biomes =
            _surfaceBiomes.SampleGrid(
                originX,
                originZ,
                width,
                depth);
        var baseHeights =
            new int[
                checked(width * depth)];

        for (var z = 0; z < depth; z++)
        {
            var worldZ = originZ + z;
            for (var x = 0; x < width; x++)
            {
                var worldX = originX + x;
                baseHeights[
                    z * width +
                    x] =
                    BaseHeightAt(
                        biomes[x, z],
                        worldX,
                        worldZ);
            }
        }

        var volumeBiomes =
            _volumeBiomes.SamplePlacementGrid(
                originX,
                originZ,
                width,
                depth);
        var values =
            new double[
                checked(width * height * depth)];

        for (var z = 0; z < depth; z++)
        {
            var worldZ = originZ + z;
            for (var y = 0; y < height; y++)
            {
                var worldY = originY + y;
                for (var x = 0; x < width; x++)
                {
                    var worldX = originX + x;
                    values[
                        (z * height + y) *
                        width +
                        x] =
                        DensityAt(
                            biomes[x, z],
                            baseHeights[
                                z * width +
                                x],
                            worldX,
                            worldY,
                            worldZ,
                            volumeBiomes[x, z]);
                }
            }
        }

        return new TerrainDensityVolume(
            width,
            height,
            depth,
            values,
            volumeBiomes,
            _volumeBiomes);
    }

    internal TerrainDensityVolume SampleDensityVolume(
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ,
        int height,
        ushort[]? cachedColumnMasks = null,
        ushort[]? cachedColumnMaskAvailability = null)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (height is < 1 or > Chunk.Size)
            throw new ArgumentOutOfRangeException(nameof(height));
        if ((cachedColumnMasks is null) != (cachedColumnMaskAvailability is null) ||
            (cachedColumnMasks is not null &&
             (cachedColumnMasks.Length != Chunk.Area ||
              cachedColumnMaskAvailability!.Length != Chunk.Area)))
        {
            throw new ArgumentException(
                "Cached density masks and sampled-bit masks must contain one entry per X/Z column.",
                nameof(cachedColumnMasks));
        }
        if (originY < 0)
            throw new ArgumentOutOfRangeException(nameof(originY));

        _ = checked(originX + Chunk.Size - 1);
        _ = checked(originY + height - 1);
        _ = checked(originZ + Chunk.Size - 1);

        var volumeBiomes = column.VolumeBiomes;
        var solidMask = new ushort[Chunk.Area];
        var requestedBits = (ushort)((1u << height) - 1u);
        var maximumY = checked(originY + height - 1);
        var minimumBaseY = int.MaxValue;
        var maximumBaseY = int.MinValue;
        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        for (var localX = 0; localX < Chunk.Size; localX++)
        {
            var baseY = column.BaseHeightAt(localX, localZ);
            minimumBaseY = Math.Min(minimumBaseY, baseY);
            maximumBaseY = Math.Max(maximumBaseY, baseY);
        }

        var caveSamplerAttempted = false;
        CaveNoiseVolumeSampler? caveSampler = null;
        var maximumAdditiveRuleCount = 0;
        foreach (var ruleSet in _additiveRules.Values)
            maximumAdditiveRuleCount = Math.Max(
                maximumAdditiveRuleCount, ruleSet.RuleCount);
        // One scratch buffer is reused for every X/Z column in this snapshot.
        // Horizontal support is independent of Y, so evaluate it once and
        // share it across all vertical density probes for the column.
        var additiveSupportScratch =
            maximumAdditiveRuleCount == 0
                ? Array.Empty<double>()
                : new double[maximumAdditiveRuleCount];
        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            var worldZ = checked(originZ + localZ);
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var index = localZ * Chunk.Size + localX;
                var cachedBits = cachedColumnMaskAvailability is null
                    ? (ushort)0
                    : (ushort)(cachedColumnMaskAvailability[index] & requestedBits);
                var cachedMask = cachedColumnMasks is null
                    ? (ushort)0
                    : (ushort)(cachedColumnMasks[index] & cachedBits);
                solidMask[index] = cachedMask;
                if (cachedBits == requestedBits)
                    continue;

                var worldX = checked(originX + localX);
                var baseY = column.BaseHeightAt(localX, localZ);
                var biome = column.BiomeAt(localX, localZ);
                var volumeBiome = volumeBiomes[localX, localZ];
                var mayContainCaves =
                    _caves is not null &&
                    (long)originY <= (long)baseY - _caves.MinimumDepth &&
                    (long)maximumY >= (long)baseY - _caves.MaximumDepth;
                if (mayContainCaves && !caveSamplerAttempted)
                {
                    caveSampler = _caves!.CreateSampler(
                        _seed, originX, originY, originZ,
                        Chunk.Size, height, Chunk.Size,
                        minimumBaseY, maximumBaseY);
                    caveSamplerAttempted = true;
                }

                var additiveRule =
                    volumeBiome is not null &&
                    _additiveRules.TryGetValue(volumeBiome.Primary, out var resolvedAdditive)
                        ? resolvedAdditive
                        : null;
                var mayContainAdditive =
                    additiveRule is not null &&
                    originY <= additiveRule.MaximumY &&
                    maximumY >= additiveRule.MinimumY;
                var additiveSupportPrepared = false;
                ReadOnlySpan<double> additiveHorizontalSupports = default;

                if (!mayContainCaves && !mayContainAdditive)
                {
                    var solidDepth = (long)baseY - originY + 1L;
                    var solidCount = (int)Math.Clamp(solidDepth, 0L, height);
                    var baselineMask = solidCount switch
                    {
                        0 => (ushort)0,
                        _ when solidCount == height => requestedBits,
                        _ => (ushort)((1u << solidCount) - 1u),
                    };
                    if (_floorY is { } floorY)
                    {
                        var liningY = (long)floorY + 1L - originY;
                        if (liningY >= 0L && liningY < height)
                            baselineMask |= (ushort)(1u << (int)liningY);
                    }
                    if (_roofY is { } roofY)
                    {
                        var liningY = (long)roofY - 1L - originY;
                        if (liningY >= 0L && liningY < height)
                            baselineMask |= (ushort)(1u << (int)liningY);
                    }
                    solidMask[index] |= (ushort)(baselineMask & (ushort)~cachedBits);
                    continue;
                }

                for (var localY = 0; localY < height; localY++)
                {
                    var bit = (ushort)(1u << localY);
                    if ((cachedBits & bit) != 0)
                        continue;
                    var worldY = checked(originY + localY);
                    var caveDepth = (long)baseY - worldY;
                    var caveDensityApplies =
                        mayContainCaves &&
                        caveDepth >= _caves!.MinimumDepth &&
                        caveDepth <= _caves.MaximumDepth;
                    var additiveDensityApplies =
                        mayContainAdditive &&
                        additiveRule is { } activeAdditive &&
                        worldY >= activeAdditive.MinimumY &&
                        worldY <= activeAdditive.MaximumY;
                    if (additiveDensityApplies && !additiveSupportPrepared)
                    {
                        var count = additiveRule!.SampleHorizontalSupports(
                            _seed, worldX, worldZ, additiveSupportScratch);
                        additiveHorizontalSupports =
                            additiveSupportScratch.AsSpan(0, count);
                        additiveSupportPrepared = true;
                    }

                    var density =
                        caveDensityApplies || additiveDensityApplies || IsShellLining(worldY)
                            ? DensityAt(
                                biome, baseY, worldX, worldY, worldZ, volumeBiome,
                                caveSampler, localX, localY, localZ,
                                additiveRule, additiveHorizontalSupports,
                                additiveRuleResolved: true)
                            : baseY - (double)worldY;
                    if (density >= 0d)
                        solidMask[index] |= bit;
                }
            }
        }

        double DensityAtLocal(int localX, int localY, int localZ)
        {
            var worldX = checked(originX + localX);
            var worldY = checked(originY + localY);
            var worldZ = checked(originZ + localZ);
            return DensityAt(
                column.BiomeAt(localX, localZ),
                column.BaseHeightAt(localX, localZ),
                worldX, worldY, worldZ,
                volumeBiomes[localX, localZ]);
        }

        return new TerrainDensityVolume(
            Chunk.Size, height, Chunk.Size,
            solidMask, DensityAtLocal,
            volumeBiomes, _volumeBiomes);
    }

    /// <summary>
    /// Completes missing occupancy columns from one vertical chunk band.
    /// The cave-noise lattice is prepared once for the batch and shared by
    /// every requested column, matching the chunk-volume sampling path.
    /// </summary>
    internal void SampleDensityColumnMasks(
        SurfaceTerrainDensityColumn column,
        int originX,
        int originY,
        int originZ,
        ReadOnlySpan<int> columnIndices,
        Span<ushort> solidMasks,
        Span<ushort> sampledMasks,
        out int densityProbes,
        out double columnBoundsMilliseconds,
        out double caveSamplerBuildMilliseconds,
        out double columnEvaluationMilliseconds)
    {
        if (originY < 0 || originY % Chunk.Size != 0)
            throw new ArgumentOutOfRangeException(nameof(originY));
        if (columnIndices.Length != solidMasks.Length ||
            columnIndices.Length != sampledMasks.Length ||
            columnIndices.Length > Chunk.Area)
            throw new ArgumentException(
                "Density column batch must contain matching buffers of at most 256 columns.");

        densityProbes = 0;
        columnBoundsMilliseconds = 0d;
        caveSamplerBuildMilliseconds = 0d;
        columnEvaluationMilliseconds = 0d;
        if (columnIndices.IsEmpty)
            return;

        var maximumY = checked(originY + Chunk.Size - 1);
        var minimumCaveBaseY = int.MaxValue;
        var maximumCaveBaseY = int.MinValue;
        var minimumCaveLocalX = Chunk.Size;
        var maximumCaveLocalX = -1;
        var minimumCaveLocalZ = Chunk.Size;
        var maximumCaveLocalZ = -1;
        var hasMissingSamples = false;
        var mayContainCaves = false;
#if DEBUG
        var columnBoundsStarted = Stopwatch.GetTimestamp();
#endif

        for (var index = 0; index < columnIndices.Length; index++)
        {
            var columnIndex = columnIndices[index];
            if ((uint)columnIndex >= Chunk.Area)
                throw new ArgumentOutOfRangeException(nameof(columnIndices));
            if (sampledMasks[index] == ushort.MaxValue)
                continue;

            hasMissingSamples = true;
            var localX = columnIndex % Chunk.Size;
            var localZ = columnIndex / Chunk.Size;
            var baseY = column.BaseHeightAt(localX, localZ);
            if (_caves is { } caves &&
                (long)originY <= (long)baseY - caves.MinimumDepth &&
                (long)maximumY >= (long)baseY - caves.MaximumDepth)
            {
                // Only cave-eligible columns contribute to the sampler bounds.
                // Including unrelated high/low columns can unnecessarily
                // expand its X/Z rectangle and vertical noise lattice.
                mayContainCaves = true;
                minimumCaveLocalX = Math.Min(minimumCaveLocalX, localX);
                maximumCaveLocalX = Math.Max(maximumCaveLocalX, localX);
                minimumCaveLocalZ = Math.Min(minimumCaveLocalZ, localZ);
                maximumCaveLocalZ = Math.Max(maximumCaveLocalZ, localZ);
                minimumCaveBaseY = Math.Min(minimumCaveBaseY, baseY);
                maximumCaveBaseY = Math.Max(maximumCaveBaseY, baseY);
            }
        }
#if DEBUG
        columnBoundsMilliseconds =
            Stopwatch.GetElapsedTime(columnBoundsStarted).TotalMilliseconds;
#endif

        if (!hasMissingSamples)
            return;

        CaveNoiseVolumeSampler? caveSampler = null;
#if DEBUG
        var caveSamplerBuildStarted = Stopwatch.GetTimestamp();
#endif
        if (mayContainCaves)
        {
            // Directional-face batches usually describe one narrow X or Z
            // strip. Build noise lattices only for the requested rectangle,
            // not for the entire 16x16 chunk footprint.
            caveSampler = _caves!.CreateSampler(
                _seed,
                checked(originX + minimumCaveLocalX),
                originY,
                checked(originZ + minimumCaveLocalZ),
                maximumCaveLocalX - minimumCaveLocalX + 1,
                Chunk.Size,
                maximumCaveLocalZ - minimumCaveLocalZ + 1,
                minimumCaveBaseY,
                maximumCaveBaseY);
        }
#if DEBUG
        caveSamplerBuildMilliseconds =
            Stopwatch.GetElapsedTime(caveSamplerBuildStarted).TotalMilliseconds;
        var columnEvaluationStarted = Stopwatch.GetTimestamp();
#endif

        for (var index = 0; index < columnIndices.Length; index++)
        {
            if (sampledMasks[index] == ushort.MaxValue)
                continue;

            var columnIndex = columnIndices[index];
            var localX = columnIndex % Chunk.Size;
            var localZ = columnIndex / Chunk.Size;
            solidMasks[index] = SampleDensityColumnMask(
                column,
                localX,
                originY,
                localZ,
                checked(originX + localX),
                checked(originZ + localZ),
                solidMasks[index],
                sampledMasks[index],
                caveSampler,
                minimumCaveLocalX,
                minimumCaveLocalZ,
                out var probes);
            sampledMasks[index] = ushort.MaxValue;
            densityProbes = checked(densityProbes + probes);
        }
#if DEBUG
        columnEvaluationMilliseconds =
            Stopwatch.GetElapsedTime(columnEvaluationStarted).TotalMilliseconds;
#endif
    }

    /// <summary>
    /// Completes only the unsampled cells in one cached vertical occupancy
    /// column. Validity bits distinguish known-empty cells from unknown cells.
    /// </summary>
    internal ushort SampleDensityColumnMask(
        SurfaceTerrainDensityColumn column,
        int localX,
        int originY,
        int localZ,
        int worldX,
        int worldZ,
        ushort cachedSolidMask,
        ushort sampledMask,
        out int densityProbes) =>
        SampleDensityColumnMask(
            column,
            localX,
            originY,
            localZ,
            worldX,
            worldZ,
            cachedSolidMask,
            sampledMask,
            caveSampler: null,
            caveSamplerOriginLocalX: 0,
            caveSamplerOriginLocalZ: 0,
            out densityProbes);

    private ushort SampleDensityColumnMask(
        SurfaceTerrainDensityColumn column,
        int localX,
        int originY,
        int localZ,
        int worldX,
        int worldZ,
        ushort cachedSolidMask,
        ushort sampledMask,
        CaveNoiseVolumeSampler? caveSampler,
        int caveSamplerOriginLocalX,
        int caveSamplerOriginLocalZ,
        out int densityProbes)
    {
        if ((uint)localX >= Chunk.Size ||
            (uint)localZ >= Chunk.Size ||
            originY < 0 ||
            originY % Chunk.Size != 0)
            throw new ArgumentOutOfRangeException(nameof(originY));

        var biome = column.BiomeAt(localX, localZ);
        var baseY = column.BaseHeightAt(localX, localZ);
        var volumeBiome = column.VolumeBiomeAt(localX, localZ);
        var solidMask = (ushort)(cachedSolidMask & sampledMask);
        var additiveRule =
            volumeBiome is not null &&
            _additiveRules.TryGetValue(volumeBiome.Primary, out var resolvedAdditive)
                ? resolvedAdditive
                : null;
        var maximumY = checked(originY + Chunk.Size - 1);
        var mayContainCaves =
            _caves is not null &&
            (long)originY <= (long)baseY - _caves.MinimumDepth &&
            (long)maximumY >= (long)baseY - _caves.MaximumDepth;
        var mayContainAdditive =
            additiveRule is not null &&
            originY <= additiveRule.MaximumY &&
            maximumY >= additiveRule.MinimumY;

        // Most columns do not intersect any carved or additive density range.
        // Their occupancy is the analytic surface half-space plus optional
        // shell lining; avoid 16 per-voxel DensityAt calls for that case.
        if (!mayContainCaves && !mayContainAdditive)
        {
            var solidDepth = (long)baseY - originY + 1L;
            var solidCount = (int)Math.Clamp(
                solidDepth, 0L, Chunk.Size);
            var baselineMask = solidCount switch
            {
                0 => (ushort)0,
                _ when solidCount == Chunk.Size => ushort.MaxValue,
                _ => (ushort)((1u << solidCount) - 1u),
            };
            if (_floorY is { } floorY)
            {
                var liningY = (long)floorY + 1L - originY;
                if (liningY >= 0L && liningY < Chunk.Size)
                    baselineMask |= (ushort)(1u << (int)liningY);
            }
            if (_roofY is { } roofY)
            {
                var liningY = (long)roofY - 1L - originY;
                if (liningY >= 0L && liningY < Chunk.Size)
                    baselineMask |= (ushort)(1u << (int)liningY);
            }

            densityProbes = System.Numerics.BitOperations.PopCount(
                (uint)(ushort)~sampledMask);
            return (ushort)(
                solidMask |
                (baselineMask & (ushort)~sampledMask));
        }

        static ushort VerticalRangeMask(
            int worldOriginY,
            long minimumWorldY,
            long maximumWorldY)
        {
            var firstLocalY = Math.Max(0L, minimumWorldY - worldOriginY);
            var lastLocalY = Math.Min(
                (long)Chunk.Size - 1L,
                maximumWorldY - worldOriginY);
            if (firstLocalY > lastLocalY)
                return 0;

            var count = checked((int)(lastLocalY - firstLocalY + 1L));
            return (ushort)(((1u << count) - 1u) << (int)firstLocalY);
        }

        static ushort WorldYBit(int worldOriginY, long worldY)
        {
            var localY = worldY - worldOriginY;
            return (ulong)localY < Chunk.Size
                ? (ushort)(1u << (int)localY)
                : (ushort)0;
        }

        var unknownMask = (ushort)~sampledMask;
        densityProbes = System.Numerics.BitOperations.PopCount(
            (uint)unknownMask);

        // Begin with the analytic surface half-space. Only cave, additive and
        // shell positions can differ from it, so the per-voxel path below
        // iterates those bits instead of all 16 Y positions in the column.
        var complexSolidDepth = (long)baseY - originY + 1L;
        var complexSolidCount = (int)Math.Clamp(
            complexSolidDepth, 0L, Chunk.Size);
        var complexBaselineMask = complexSolidCount switch
        {
            0 => (ushort)0,
            _ when complexSolidCount == Chunk.Size => ushort.MaxValue,
            _ => (ushort)((1u << complexSolidCount) - 1u),
        };

        var floorLiningMask = _floorY is { } floorLiningY
            ? WorldYBit(originY, (long)floorLiningY + 1L)
            : (ushort)0;
        var roofLiningMask = _roofY is { } roofLiningY
            ? WorldYBit(originY, (long)roofLiningY - 1L)
            : (ushort)0;
        var shellLiningMask = (ushort)(floorLiningMask | roofLiningMask);
        complexBaselineMask |= shellLiningMask;
        solidMask |= (ushort)(complexBaselineMask & unknownMask);

        var floorBoundaryMask = _floorY is { } floorBoundaryY
            ? WorldYBit(originY, floorBoundaryY)
            : (ushort)0;
        var roofBoundaryMask = _roofY is { } roofBoundaryY
            ? WorldYBit(originY, roofBoundaryY)
            : (ushort)0;
        var caveShellBoundaryMask = (ushort)(
            shellLiningMask | floorBoundaryMask | roofBoundaryMask);

        var caveRangeMask = mayContainCaves
            ? VerticalRangeMask(
                originY,
                (long)baseY - _caves!.MaximumDepth,
                (long)baseY - _caves.MinimumDepth)
            : (ushort)0;
        var additiveRangeMask = mayContainAdditive
            ? VerticalRangeMask(
                originY,
                additiveRule!.MinimumY,
                additiveRule.MaximumY)
            : (ushort)0;

        var caveEvaluationMask = (ushort)(
            caveRangeMask & (ushort)~caveShellBoundaryMask);
        var additiveEvaluationMask = (ushort)(
            additiveRangeMask & (ushort)~shellLiningMask);
        var complexMask = (ushort)(
            unknownMask & (ushort)(caveEvaluationMask | additiveEvaluationMask));
        if (complexMask == 0)
            return solidMask;

        var additiveNeededMask = (ushort)(
            complexMask & additiveEvaluationMask);
        Span<double> additiveHorizontalSupports =
            additiveRule is null || additiveNeededMask == 0
                ? Span<double>.Empty
                : additiveRule.RuleCount <= 16
                    ? stackalloc double[additiveRule.RuleCount]
                    : new double[additiveRule.RuleCount];
        if (additiveNeededMask != 0)
        {
            additiveRule!.SampleHorizontalSupports(
                _seed, worldX, worldZ, additiveHorizontalSupports);
        }

        var caveLocalX = caveSampler is null
            ? localX
            : localX - caveSamplerOriginLocalX;
        var caveLocalZ = caveSampler is null
            ? localZ
            : localZ - caveSamplerOriginLocalZ;

        while (complexMask != 0)
        {
            var localY = System.Numerics.BitOperations.TrailingZeroCount(
                (uint)complexMask);
            var bit = (ushort)(1u << localY);
            complexMask = (ushort)(complexMask & (complexMask - 1));
            var worldY = checked(originY + localY);
            var density = baseY - (double)worldY;

            if ((additiveEvaluationMask & bit) != 0)
            {
                var additiveDensity = additiveHorizontalSupports.IsEmpty
                    ? additiveRule!.DensityAt(
                        _seed, worldX, worldY, worldZ,
                        volumeBiome!.PrimaryWeight)
                    : additiveRule!.DensityAt(
                        _seed, worldX, worldY, worldZ,
                        volumeBiome!.PrimaryWeight,
                        additiveHorizontalSupports);
                density = Math.Max(density, additiveDensity);
            }

            if ((caveEvaluationMask & bit) != 0 &&
                density >= 0d &&
                worldY <= baseY)
            {
                var caveDepth = (long)baseY - worldY;
                var voidDensity = _caves!.VoidDensityAt(
                    _seed, worldX, worldY, worldZ, caveDepth,
                    caveSampler, caveLocalX, localY, caveLocalZ);
                if (voidDensity > 0d)
                    density = Math.Min(density, -voidDensity);
            }

            if (density < 0d)
                solidMask &= (ushort)~bit;
            else
                solidMask |= bit;
        }
        return solidMask;
    }

    /// <summary>
    /// Creates a deterministic, on-demand density view for support queries.
    /// The caller may inspect a small set of voxels without sampling all 4096
    /// cells in the owning chunk.
    /// </summary>
    internal TerrainDensityVolume CreateLazyDensityVolume(
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (originY < 0)
            throw new ArgumentOutOfRangeException(nameof(originY));

        _ = checked(originX + Chunk.Size - 1);
        _ = checked(originY + Chunk.Size - 1);
        _ = checked(originZ + Chunk.Size - 1);

        double DensityAtLocal(int localX, int localY, int localZ)
        {
            var worldX = checked(originX + localX);
            var worldY = checked(originY + localY);
            var worldZ = checked(originZ + localZ);
            return DensityAt(
                column.BiomeAt(localX, localZ),
                column.BaseHeightAt(localX, localZ),
                worldX,
                worldY,
                worldZ,
                column.VolumeBiomes[localX, localZ]);
        }

        return new TerrainDensityVolume(
            Chunk.Size,
            Chunk.Size,
            Chunk.Size,
            DensityAtLocal,
            column.VolumeBiomes,
            _volumeBiomes);
    }

    /// <summary>
    /// Samples only the immutable data required to evaluate voxel density.
    /// Horizontal halo reads do not need cave-biome ownership, final surface
    /// heights or fluid-cut metadata, so avoid building those full-column grids.
    /// </summary>
    /// <summary>
    /// Samples only the requested density columns. Horizontal boundary reads
    /// normally need a narrow strip, not a complete 16x16 terrain footprint.
    /// Samplers share deterministic seed assignments within each batch.
    /// </summary>
    internal BiomeField.Sampler CreateDensitySurfaceSampler() =>
        _surfaceBiomes.CreateSampler();

    internal BiomeField.Sampler? CreateDensityVolumeSampler() =>
        _volumeBiomes.CreatePlacementSampler();

    internal void SampleDensitySurfaceColumns(
        int chunkX,
        int chunkZ,
        ReadOnlySpan<int> columnIndices,
        BiomeField.Sampler surfaceSampler,
        BiomeSample?[] biomes,
        int[] baseHeights)
    {
        ArgumentNullException.ThrowIfNull(surfaceSampler);
        if (columnIndices.Length > Chunk.Area ||
            biomes.Length != Chunk.Area ||
            baseHeights.Length != Chunk.Area)
        {
            throw new ArgumentException(
                "Density surface snapshots must use chunk-sized buffers.");
        }

        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(
                new ChunkCoord(chunkX, 0, chunkZ));

        for (var offset = 0; offset < columnIndices.Length; offset++)
        {
            var columnIndex = columnIndices[offset];
            if ((uint)columnIndex >= Chunk.Area)
                throw new ArgumentOutOfRangeException(nameof(columnIndices));

            var localX = columnIndex % Chunk.Size;
            var localZ = columnIndex / Chunk.Size;
            var worldX = checked(originX + localX);
            var worldZ = checked(originZ + localZ);
            var biome = surfaceSampler.Sample(worldX, worldZ);

            biomes[columnIndex] = biome;
            baseHeights[columnIndex] =
                BaseHeightAt(biome, worldX, worldZ);
        }
    }

    internal void SampleDensityVolumeColumns(
        int chunkX,
        int chunkZ,
        ReadOnlySpan<int> columnIndices,
        BiomeField.Sampler? volumeSampler,
        BiomeSample?[] volumeBiomes)
    {
        if (columnIndices.Length > Chunk.Area ||
            volumeBiomes.Length != Chunk.Area)
        {
            throw new ArgumentException(
                "Density volume snapshots must use chunk-sized buffers.");
        }

        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(
                new ChunkCoord(chunkX, 0, chunkZ));

        for (var offset = 0; offset < columnIndices.Length; offset++)
        {
            var columnIndex = columnIndices[offset];
            if ((uint)columnIndex >= Chunk.Area)
                throw new ArgumentOutOfRangeException(nameof(columnIndices));

            var localX = columnIndex % Chunk.Size;
            var localZ = columnIndex / Chunk.Size;
            var worldX = checked(originX + localX);
            var worldZ = checked(originZ + localZ);
            volumeBiomes[columnIndex] = _volumeBiomes.SamplePlacement(
                volumeSampler, worldX, worldZ);
        }
    }

    public SurfaceTerrainColumn SampleColumn(int chunkX, int chunkZ)
    {
        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(
                new ChunkCoord(chunkX, 0, chunkZ));
        var biomes = _surfaceBiomes.SampleGrid(
            originX,
            originZ,
            Chunk.Size,
            Chunk.Size);
        var volumeBiomes =
            _volumeBiomes.SamplePlacementGrid(
                originX,
                originZ,
                Chunk.Size,
                Chunk.Size);
        var caveBiomes =
            _volumeBiomes.SampleCaveGrid(
                originX,
                originZ,
                Chunk.Size,
                Chunk.Size);
        var baseHeights = new int[Chunk.Size * Chunk.Size];
        var surfaceFluidCutDepths =
            new int[baseHeights.Length];
        var surfaceHeights = new int[baseHeights.Length];
        var minimum = int.MaxValue;
        var maximum = int.MinValue;

        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                var worldX = checked(originX + x);
                var worldZ = checked(originZ + z);
                var biome = biomes[x, z];
                var resolved =
                    BaseHeightAndSurfaceFluidCutAt(
                        biome,
                        worldX,
                        worldZ);
                var surfaceY = FinalSurfaceHeight(
                    biome,
                    resolved.BaseY,
                    worldX,
                    worldZ,
                    volumeBiomes[x, z]);
                var index = z * Chunk.Size + x;
                baseHeights[index] =
                    resolved.BaseY;
                surfaceFluidCutDepths[index] =
                    resolved.SurfaceFluidCutDepth;
                surfaceHeights[index] = surfaceY;
                minimum = Math.Min(minimum, surfaceY);
                maximum = Math.Max(maximum, surfaceY);
            }
        }

        return new SurfaceTerrainColumn(
            biomes,
            volumeBiomes,
            caveBiomes,
            baseHeights,
            surfaceFluidCutDepths,
            surfaceHeights,
            new ChunkSurfaceRange(minimum, maximum));
    }

    private int BaseHeightAt(
        BiomeSample sample,
        int worldX,
        int worldZ) =>
        BaseHeightAndSurfaceFluidCutAt(
            sample,
            worldX,
            worldZ)
        .BaseY;

    private double HeightOffsetAt(
        BiomeSample sample,
        int worldX,
        int worldZ)
    {
        // A higher neighboring biome must not raise terrain outside its
        // own boundary. Its slope grows only after it becomes primary.
        var primaryRule = _surfaceRules[sample.Primary];
        var primaryHeight = primaryRule.HeightOffsetAt(
            _seed,
            worldX,
            worldZ,
            sample.PrimaryTerrainStrength);

        var neighboringHeightSum = 0d;
        var neighboringWeightSum = 0d;
        var strongestNeighborWeight = 0d;

        foreach (var influence in sample.Influences)
        {
            if (influence.Weight <= 0f ||
                string.Equals(
                    influence.BiomeId,
                    sample.Primary,
                    StringComparison.Ordinal))
            {
                continue;
            }

            // A higher landform starts rising inside its own biome.
            // The ocean contributes its authored shore elevation (not
            // the deep seabed) to the boundary; this does not raise any
            // ocean-owned column to match a neighboring mountain.
            var influenceHeight =
                _coastProfile is { } coast &&
                coast.IsOcean(influence.BiomeId)
                    ? coast.LandBoundaryHeightOffset
                    : _surfaceRules[influence.BiomeId].HeightOffsetAt(
                        _seed, worldX, worldZ, influence.TerrainStrength);
            // Elevated terrain starts only within its primary biome.
            if (influenceHeight >= primaryHeight)
            {
                continue;
            }

            neighboringHeightSum += influenceHeight * influence.Weight;
            neighboringWeightSum += influence.Weight;
            strongestNeighborWeight = Math.Max(
                strongestNeighborWeight,
                influence.Weight);
        }

        if (neighboringWeightSum <= 0d)
        {
            return primaryHeight;
        }

        var dominance = Math.Clamp(
            sample.PrimaryWeight - strongestNeighborWeight,
            0d, 1d);
        var slope = WorldGenerationEntropy.SmoothStep(dominance);
        var neighborHeight = neighboringHeightSum / neighboringWeightSum;
        return neighborHeight + (primaryHeight - neighborHeight) * slope;
    }

    private (
        int BaseY,
        int SurfaceFluidCutDepth)
        BaseHeightAndSurfaceFluidCutAt(
            BiomeSample sample,
            int worldX,
            int worldZ)
    {
        if (_mode == WorldGenerationMode.Flat)
        {
            // Even an ocean biome may not change the planar surface.
            return (_planarSurfaceY, 0);
        }
        if (_mode == WorldGenerationMode.Void)
            return (-1, 0); // No solid surface; Y voxels still start at zero.

        var offset =
            HeightOffsetAt(
                sample,
                worldX,
                worldZ);

        if (_coastProfile is not null)
        {
            offset =
                _coastProfile.AdjustHeightOffset(sample, offset);
        }

        var height =
            _seaLevel +
            offset;
        var authoredSurfaceY =
            checked(
                (int)Math.Floor(
                    height));
        if (_roofY is
                { } roofY &&
            authoredSurfaceY >=
                roofY)
        {
            authoredSurfaceY =
                roofY -
                1;
        }

        var requestedCutDepth =
            _generatedFluids
                .SurfaceCutDepthAt(
                    sample,
                    worldX,
                    worldZ);
        // Natural terrain always covers the innermost face of the floor.
        // Void worlds deliberately have no natural terrain, so their lining
        // is supplied by the boundary materialization instead.
        if (_floorY is { } shellFloorY)
        {
            authoredSurfaceY = Math.Max(
                authoredSurfaceY,
                checked(shellFloorY + 1));
        }

        if (requestedCutDepth <= 0)
        {
            return (
                authoredSurfaceY,
                0);
        }

        var minimumSurfaceY =
            _floorY is
                { } floorY
                ? (long)floorY +
                  1L
                : 0L;
        var availableDepth =
            Math.Max(
                0L,
                (long)authoredSurfaceY -
                minimumSurfaceY);
        var actualCutDepth =
            checked(
                (int)Math.Min(
                    requestedCutDepth,
                    availableDepth));

        return (
            checked(
                authoredSurfaceY -
                actualCutDepth),
            actualCutDepth);
    }

    private int FinalSurfaceHeight(
        BiomeSample surfaceBiome,
        int baseY,
        int worldX,
        int worldZ,
        BiomeSample? volumeBiome)
    {
        if (volumeBiome is null ||
            !_additiveRules.TryGetValue(
                volumeBiome.Primary,
                out var rule) ||
            rule.MaximumY <= baseY)
        {
            return baseY;
        }

        const int StackSupportCapacity = 16;
        Span<double> additiveHorizontalSupports =
            rule.RuleCount <= StackSupportCapacity
                ? stackalloc double[rule.RuleCount]
                : new double[rule.RuleCount];
        rule.SampleHorizontalSupports(
            _seed, worldX, worldZ, additiveHorizontalSupports);
        if (!rule.MayExistAt(
                volumeBiome.PrimaryWeight, additiveHorizontalSupports))
        {
            return baseY;
        }

        var lowestCandidate =
            rule.MinimumY;
        var highestCandidate =
            Math.Max(
                baseY,
                rule.MaximumY);

        if (_roofY is { } roofY)
        {
            highestCandidate =
                Math.Min(
                    highestCandidate,
                    roofY - 1);
        }

        for (var y = highestCandidate;
             y > baseY &&
             y >= lowestCandidate;
             y--)
        {
            if (DensityAt(
                    surfaceBiome, baseY, worldX, y, worldZ, volumeBiome,
                    caveSampler: null, localX: 0, localY: 0, localZ: 0,
                    rule, additiveHorizontalSupports,
                    additiveRuleResolved: true) >= 0d)
            {
                return y;
            }
        }

        return baseY;
    }

    private sealed class CoastProfileRule
    {
        private readonly string _biome;
        private readonly IReadOnlyList<DimensionShoreSampleDefinition> _samples;

        public CoastProfileRule(
            string biome,
            DimensionShoreProfileDefinition definition)
        {
            _biome = biome;
            _samples = definition.Samples;
            // The two biomes share one authored boundary elevation; never
            // borrow a mountain's potentially unbounded height on the ocean
            // side. Dominance = 0.5 is the ownership boundary.
            LandBoundaryHeightOffset = SampleBoundaryHeight(_samples);
        }

        public double LandBoundaryHeightOffset { get; }

        private static double SampleBoundaryHeight(
            IReadOnlyList<DimensionShoreSampleDefinition> samples)
        {
            for (var i = 1; i < samples.Count; i++)
            {
                if (samples[i].Dominance < 0.5d)
                    continue;
                var left = samples[i - 1];
                var right = samples[i];
                var delta = right.Dominance - left.Dominance;
                var t = delta > 0d
                    ? WorldGenerationEntropy.SmoothStep(
                        (0.5d - left.Dominance) / delta)
                    : 0d;
                return left.MinimumHeight +
                    (right.MinimumHeight - left.MinimumHeight) * t;
            }

            return samples[^1].MinimumHeight;
        }

        public bool IsOcean(string biomeId) =>
            string.Equals(biomeId, _biome, StringComparison.Ordinal);

        public double AdjustHeightOffset(
            BiomeSample sample,
            double rawOffset)
        {
            // The coast belongs entirely to the ocean. A shared transition
            // must never raise (or lower) terrain outside that biome.
            if (!IsOcean(sample.Primary))
            {
                return rawOffset;
            }

            var coastWeight = sample.PrimaryWeight;
            var strongestOther = 0d;

            foreach (var influence in sample.Influences)
            {
                if (!IsOcean(influence.BiomeId))
                    strongestOther = Math.Max(strongestOther, influence.Weight);
            }

            if (strongestOther <= 0d)
                return rawOffset;

            var dominance = coastWeight /
                (coastWeight + strongestOther);
            // Shore height is authored by the Sphere, independent of the
            // elevation of adjoining Plains, Alps or Mountains. High
            // terrain transitions upward only within the high biome.

            // The strictly increasing authored knots cover [0,1].
            // Interpolation is deterministic, allocation-free, and
            // continuous at all profile boundaries.
            for (var i = 1; i < _samples.Count; i++)
            {
                var right = _samples[i];
                if (dominance > right.Dominance)
                {
                    continue;
                }

                var left = _samples[i - 1];
                var t = WorldGenerationEntropy.SmoothStep(
                    (dominance - left.Dominance) /
                    (right.Dominance - left.Dominance));
                var floor = left.MinimumHeight +
                    (right.MinimumHeight - left.MinimumHeight) * t;
                var strength = left.Strength +
                    (right.Strength - left.Strength) * t;
                return rawOffset +
                    (Math.Max(rawOffset, floor) -
                     rawOffset) * strength;
            }

            return rawOffset;
        }
    }

    private sealed class AdditiveRuleSet
    {
        private readonly AdditiveRule[] _rules;

        public AdditiveRuleSet(
            string biomeId,
            IReadOnlyList<BiomeAdditiveDensityDefinition> definitions)
        {
            _rules = definitions
                .Select((definition, index) =>
                    new AdditiveRule(biomeId, index, definition))
                .ToArray();
            MinimumY = _rules.Min(rule => rule.MinimumY);
            MaximumY = _rules.Max(rule => rule.MaximumY);
        }

        public int MinimumY { get; }
        public int MaximumY { get; }
        public int RuleCount => _rules.Length;

        public bool IntersectsYRange(int minimumY, int maximumY)
        {
            for (var index = 0; index < _rules.Length; index++)
            {
                var rule = _rules[index];
                if (minimumY <= rule.MaximumY &&
                    maximumY >= rule.MinimumY)
                    return true;
            }

            return false;
        }

        public int SampleHorizontalSupports(
            ulong seed,
            int x,
            int z,
            Span<double> destination)
        {
            if (destination.Length < _rules.Length)
                throw new ArgumentException(
                    "Additive support buffer is shorter than the authored rule set.",
                    nameof(destination));
            for (var index = 0; index < _rules.Length; index++)
                destination[index] = _rules[index].HorizontalSupportAt(
                    seed, x, z);
            return _rules.Length;
        }

        public bool MayExistAt(
            ulong seed,
            int x,
            int z,
            float influenceWeight)
        {
            foreach (var rule in _rules)
                if (rule.MayExistAt(seed, x, z, influenceWeight))
                    return true;
            return false;
        }

        public bool MayExistAt(
            float influenceWeight,
            ReadOnlySpan<double> horizontalSupports)
        {
            if (horizontalSupports.Length != _rules.Length)
                throw new ArgumentException(
                    "Additive support samples do not match the authored rule set.",
                    nameof(horizontalSupports));
            for (var index = 0; index < _rules.Length; index++)
                if (_rules[index].MayExistAt(
                        influenceWeight, horizontalSupports[index]))
                    return true;
            return false;
        }

        public double DensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            float influenceWeight)
        {
            var density = double.NegativeInfinity;
            foreach (var rule in _rules)
                if (y >= rule.MinimumY && y <= rule.MaximumY)
                    density = Math.Max(
                        density,
                        rule.DensityAt(seed, x, y, z, influenceWeight));
            return density;
        }

        public double DensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            float influenceWeight,
            ReadOnlySpan<double> horizontalSupports)
        {
            if (horizontalSupports.Length != _rules.Length)
                throw new ArgumentException(
                    "Additive support samples do not match the authored rule set.",
                    nameof(horizontalSupports));
            var density = double.NegativeInfinity;
            for (var index = 0; index < _rules.Length; index++)
            {
                var rule = _rules[index];
                if (y >= rule.MinimumY && y <= rule.MaximumY)
                    density = Math.Max(
                        density,
                        rule.DensityAt(
                            seed, x, y, z, influenceWeight,
                            horizontalSupports[index]));
            }
            return density;
        }
    }

    private sealed class AdditiveRule
    {
        private readonly BiomeAdditiveDensityDefinition _authored;
        private readonly GenerationDomain _maskDomain;
        private readonly GenerationDomain _detailDomain;

        public AdditiveRule(
            string biomeId,
            int index,
            BiomeAdditiveDensityDefinition authored)
        {
            _authored = authored;
            _maskDomain = GenerationDomain.Named(
                $"terrain/density/additive/mask/v1/{biomeId}/{index}");
            _detailDomain = GenerationDomain.Named(
                $"terrain/density/additive/detail/v1/{biomeId}/{index}");
        }

        public int MinimumY => _authored.MinY;
        public int MaximumY => _authored.MaxY;

        public bool MayExistAt(
            ulong seed,
            int x,
            int z,
            float influenceWeight) =>
            MayExistAt(influenceWeight, HorizontalSupport(seed, x, z));

        public bool MayExistAt(
            float influenceWeight,
            double horizontalSupport) =>
            Math.Pow(horizontalSupport, _authored.HorizontalFalloff) +
            _authored.Roughness -
            _authored.DensityBias >
            1d - influenceWeight;

        public double DensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            float influenceWeight) =>
            DensityAt(
                seed, x, y, z, influenceWeight,
                HorizontalSupport(seed, x, z));

        public double DensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            float influenceWeight,
            double horizontalSupport)
        {
            var center = (_authored.MinY + (double)_authored.MaxY) * 0.5d;
            var halfSpan = (_authored.MaxY - (double)_authored.MinY) * 0.5d;
            var verticalDistance = Math.Abs((y - center) / halfSpan);
            var shape = Math.Pow(
                horizontalSupport,
                _authored.HorizontalFalloff) -
                Math.Pow(verticalDistance, _authored.VerticalFalloff);
            var detail = WorldGenerationEntropy.ValueNoise3D(
                seed,
                _detailDomain,
                x,
                y,
                z,
                _authored.DetailScale,
                _authored.DetailScale);
            return (shape +
                    detail * _authored.Roughness -
                    (1d - influenceWeight) -
                    _authored.DensityBias) *
                   _authored.DensityScale;
        }

        public double HorizontalSupportAt(
            ulong seed,
            int x,
            int z) =>
            HorizontalSupport(seed, x, z);

        private double HorizontalSupport(
            ulong seed,
            int x,
            int z)
        {
            var mask = WorldGenerationEntropy.ValueNoise2D(
                seed,
                _maskDomain,
                x,
                z,
                _authored.HorizontalScale);
            var unit = mask * 0.5d + 0.5d;
            var threshold = 1d - _authored.Coverage;
            return Math.Clamp(
                (unit - threshold) / _authored.Coverage,
                0d,
                1d);
        }
    }

    private sealed class CaveNoiseVolumeSampler(
        WorldGenerationEntropy.ValueNoise3DGridSampler[]?[] channels,
        WorldGenerationEntropy.ValueNoise3DGridSampler?[] chambers,
        int[] localYOffsets)
    {
        public WorldGenerationEntropy.ValueNoise3DGridSampler[]?[] Channels { get; } = channels;
        public WorldGenerationEntropy.ValueNoise3DGridSampler?[] Chambers { get; } = chambers;
        public int[] LocalYOffsets { get; } = localYOffsets;
    }

    private sealed class CaveRule
    {
        private readonly CaveLayerRule[] _layers;

        public CaveRule(DimensionCaveDefinition authored)
        {
            _layers = authored.Layers
                .Select((layer, index) => new CaveLayerRule(index, layer))
                .ToArray();
            MinimumDepth = authored.MinimumDepth;
            MaximumDepth = authored.MaximumDepth;
        }

        public uint MinimumDepth { get; }
        public uint MaximumDepth { get; }

        public CaveNoiseVolumeSampler CreateSampler(
            ulong seed,
            int originX,
            int originY,
            int originZ,
            int width,
            int height,
            int depth,
            int minimumBaseY,
            int maximumBaseY)
        {
            var maximumY = checked(originY + height - 1);
            var channels =
                new WorldGenerationEntropy.ValueNoise3DGridSampler[]?[_layers.Length];
            var chambers =
                new WorldGenerationEntropy.ValueNoise3DGridSampler?[_layers.Length];
            var localYOffsets = new int[_layers.Length];
            for (var index = 0; index < _layers.Length; index++)
            {
                var layer = _layers[index];
                var layerMinimumY = Math.Max(
                    (long)originY,
                    (long)minimumBaseY - layer.MaximumDepth);
                var layerMaximumY = Math.Min(
                    (long)maximumY,
                    (long)maximumBaseY - layer.MinimumDepth);
                if (layerMinimumY > layerMaximumY)
                {
                    continue;
                }

                var sampleOriginY = checked((int)layerMinimumY);
                var sampleHeight = checked((int)(layerMaximumY - layerMinimumY + 1L));
                channels[index] = layer.CreateChannelSamplers(
                    seed, originX, sampleOriginY, originZ, width, sampleHeight, depth);
                chambers[index] = layer.CreateChamberSampler(
                    seed, originX, sampleOriginY, originZ, width, sampleHeight, depth);
                localYOffsets[index] = sampleOriginY - originY;
            }

            return new CaveNoiseVolumeSampler(channels, chambers, localYOffsets);
        }

        public double VoidDensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            long depth,
            CaveNoiseVolumeSampler? sampler = null,
            int localX = 0,
            int localY = 0,
            int localZ = 0)
        {
            var result = 0d;
            for (var index = 0; index < _layers.Length; index++)
            {
                var layer = _layers[index];
                if (depth < layer.MinimumDepth ||
                    depth > layer.MaximumDepth)
                {
                    continue;
                }

                var layerChannels = sampler?.Channels[index];
                var sampleLocalY = layerChannels is null
                    ? localY
                    : localY - sampler!.LocalYOffsets[index];
                result = Math.Max(
                    result,
                    layer.VoidDensityAt(
                        seed,
                        x,
                        y,
                        z,
                        depth,
                        layerChannels,
                        sampler?.Chambers[index],
                        localX,
                        sampleLocalY,
                        localZ));
            }

            return result;
        }
    }

    private sealed class CaveLayerRule
    {
        private readonly DimensionCaveLayerDefinition _authored;
        private readonly CaveNoiseChannel[] _channels;
        private readonly GenerationDomain _chamberDomain;

        public CaveLayerRule(
            int layerIndex,
            DimensionCaveLayerDefinition authored)
        {
            _authored = authored;
            _chamberDomain = GenerationDomain.Named(
                $"terrain/density/caves/layer/v1/{layerIndex}/chambers");
            _channels = authored.Channels
                .Select((channel, index) =>
                    new CaveNoiseChannel(
                        GenerationDomain.Named(
                            $"terrain/density/caves/layer/v1/{layerIndex}/{index}"),
                        channel.HorizontalScale,
                        channel.VerticalScale))
                .ToArray();
        }

        public uint MinimumDepth => _authored.MinDepth;
        public uint MaximumDepth => _authored.MaxDepth;

        public bool IntersectsChunkDepthRange(
            int minimumBaseY,
            int maximumBaseY,
            int originY,
            int maximumY) =>
            (long)originY <= (long)maximumBaseY - MinimumDepth &&
            (long)maximumY >= (long)minimumBaseY - MaximumDepth;

        public WorldGenerationEntropy.ValueNoise3DGridSampler[] CreateChannelSamplers(
            ulong seed,
            int originX,
            int originY,
            int originZ,
            int width,
            int height,
            int depth)
        {
            var samplers =
                new WorldGenerationEntropy.ValueNoise3DGridSampler[_channels.Length];
            for (var index = 0; index < _channels.Length; index++)
            {
                var channel = _channels[index];
                samplers[index] =
                    new WorldGenerationEntropy.ValueNoise3DGridSampler(
                        seed,
                        channel.Domain,
                        originX,
                        originY,
                        originZ,
                        width,
                        height,
                        depth,
                        channel.HorizontalScale,
                        channel.VerticalScale);
            }

            return samplers;
        }

        public WorldGenerationEntropy.ValueNoise3DGridSampler? CreateChamberSampler(
            ulong seed,
            int originX,
            int originY,
            int originZ,
            int width,
            int height,
            int depth)
        {
            if (_authored.Chambers is not { } chambers)
                return null;

            return new WorldGenerationEntropy.ValueNoise3DGridSampler(
                seed,
                _chamberDomain,
                originX,
                originY,
                originZ,
                width,
                height,
                depth,
                chambers.HorizontalScale,
                chambers.VerticalScale);
        }

        public double VoidDensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            long depth,
            WorldGenerationEntropy.ValueNoise3DGridSampler[]? channelSamplers = null,
            WorldGenerationEntropy.ValueNoise3DGridSampler? chamberSampler = null,
            int localX = 0,
            int localY = 0,
            int localZ = 0)
        {
            var intersection =
                _authored.Combination == CaveNoiseCombination.Intersection;
            var score = intersection ? 0d : double.PositiveInfinity;
            for (var channelIndex = 0; channelIndex < _channels.Length; channelIndex++)
            {
                var channel = _channels[channelIndex];
                var noise = Math.Abs(channelSamplers is null
                    ? WorldGenerationEntropy.ValueNoise3D(
                        seed,
                        channel.Domain,
                        x,
                        y,
                        z,
                        channel.HorizontalScale,
                        channel.VerticalScale)
                    : channelSamplers[channelIndex].Sample(
                        localX,
                        localY,
                        localZ));
                score = intersection
                    ? Math.Max(score, noise)
                    : Math.Min(score, noise);
            }

            var halfWidth = (double)_authored.NoiseHalfWidth;
            if (_authored.Chambers is { } chambers &&
                score < chambers.MaxNoiseHalfWidth)
            {
                var signal = chamberSampler is null
                    ? WorldGenerationEntropy.ValueNoise3D(
                        seed,
                        _chamberDomain,
                        x,
                        y,
                        z,
                        chambers.HorizontalScale,
                        chambers.VerticalScale)
                    : chamberSampler.Sample(localX, localY, localZ);
                var strength = Math.Clamp(
                    (signal - chambers.ActivationStart) /
                    (chambers.ActivationFull - chambers.ActivationStart),
                    0d,
                    1d);
                strength = strength * strength * (3d - 2d * strength);
                halfWidth +=
                    (chambers.MaxNoiseHalfWidth - halfWidth) * strength;
            }

            var clearance = halfWidth - score;
            if (clearance <= 0d)
            {
                return 0d;
            }

            var fade = Math.Min(
                ((double)depth - _authored.MinDepth) / _authored.BoundaryFade,
                ((double)_authored.MaxDepth - depth) / _authored.BoundaryFade);
            if (fade <= 0d)
            {
                return 0d;
            }

            return clearance / halfWidth *
                   Math.Min(1d, fade) *
                   _authored.DensityScale;
        }
    }

    private readonly record struct CaveNoiseChannel(
        GenerationDomain Domain,
        uint HorizontalScale,
        uint VerticalScale);
}

/// <summary>
/// Read-only X/Z biome/base/final terrain height snapshot shared by
/// vertical materializations and render queries.
/// </summary>
public sealed class SurfaceTerrainColumn
{
    private readonly BiomeSampleGrid _biomes;
    private readonly VolumeBiomePlacementGrid _volumeBiomes;
    private readonly BiomeSampleGrid? _caveBiomes;
    private readonly int[] _baseHeights;
    private readonly int[] _surfaceFluidCutDepths;
    private readonly int[] _surfaceHeights;

    internal SurfaceTerrainColumn(
        BiomeSampleGrid biomes,
        VolumeBiomePlacementGrid volumeBiomes,
        BiomeSampleGrid? caveBiomes,
        int[] baseHeights,
        int[] surfaceFluidCutDepths,
        int[] surfaceHeights,
        ChunkSurfaceRange range)
    {
        _biomes = biomes;
        _volumeBiomes = volumeBiomes;
        _caveBiomes = caveBiomes;
        _baseHeights = baseHeights;
        _surfaceFluidCutDepths =
            surfaceFluidCutDepths;
        _surfaceHeights = surfaceHeights;
        Range = range;
    }

    public ChunkSurfaceRange Range { get; }

    internal VolumeBiomePlacementGrid VolumeBiomes => _volumeBiomes;

    internal void PrimeBiomeSamplers(
        BiomeField.Sampler surfaceSampler,
        BiomeField.Sampler? volumeSampler,
        int originX,
        int originZ)
    {
        ArgumentNullException.ThrowIfNull(surfaceSampler);
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var worldX = checked(originX + x);
            var worldZ = checked(originZ + z);
            surfaceSampler.Prime(worldX, worldZ, _biomes[x, z]);
            if (volumeSampler is not null &&
                _volumeBiomes[x, z] is { } volumeBiome)
            {
                volumeSampler.Prime(worldX, worldZ, volumeBiome);
            }
        }
    }

    internal BiomeSample? CaveBiomeAt(int localX, int localZ) =>
        _caveBiomes?[localX, localZ];

    public BiomeSample BiomeAt(int localX, int localZ) =>
        _biomes[localX, localZ];

    public int BaseHeightAt(int localX, int localZ) =>
        _baseHeights[Index(localX, localZ)];

    public int SurfaceFluidCutDepthAt(
        int localX,
        int localZ) =>
        _surfaceFluidCutDepths[
            Index(
                localX,
                localZ)];

    public int HeightAt(int localX, int localZ) =>
        _surfaceHeights[Index(localX, localZ)];

    private static int Index(int x, int z)
    {
        if ((uint)x >= Chunk.Size ||
            (uint)z >= Chunk.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        return z * Chunk.Size + x;
    }
}

/// <summary>
/// Minimal immutable X/Z snapshot used by density-only halo sampling. It can
/// wrap an existing full column without copying, or own only the three grids
/// required to determine voxel occupancy.
/// </summary>
internal readonly struct SurfaceTerrainDensityColumn
{
    private readonly SurfaceTerrainColumn? _fullColumn;
    private readonly BiomeSample?[]? _biomes;
    private readonly BiomeSample?[]? _volumeBiomes;
    private readonly int[]? _baseHeights;

    internal SurfaceTerrainDensityColumn(SurfaceTerrainColumn fullColumn)
    {
        ArgumentNullException.ThrowIfNull(fullColumn);
        _fullColumn = fullColumn;
        _biomes = null;
        _volumeBiomes = null;
        _baseHeights = null;
    }

    internal SurfaceTerrainDensityColumn(
        BiomeSample?[] biomes,
        BiomeSample?[] volumeBiomes,
        int[] baseHeights)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(volumeBiomes);
        ArgumentNullException.ThrowIfNull(baseHeights);
        if (biomes.Length != Chunk.Area ||
            volumeBiomes.Length != Chunk.Area ||
            baseHeights.Length != Chunk.Area)
        {
            throw new ArgumentException(
                "Density terrain columns must use chunk-sized buffers.");
        }

        _fullColumn = null;
        _biomes = biomes;
        _volumeBiomes = volumeBiomes;
        _baseHeights = baseHeights;
    }

    internal BiomeSample BiomeAt(int localX, int localZ)
    {
        if (_fullColumn is { } fullColumn)
            return fullColumn.BiomeAt(localX, localZ);

        var index = Index(localX, localZ);
        return _biomes![index] ??
            throw new InvalidOperationException(
                $"Density terrain column {localX},{localZ} was not sampled.");
    }

    internal int BaseHeightAt(int localX, int localZ)
    {
        if (_fullColumn is { } fullColumn)
            return fullColumn.BaseHeightAt(localX, localZ);
        return _baseHeights![Index(localX, localZ)];
    }

    internal BiomeSample? VolumeBiomeAt(int localX, int localZ)
    {
        if (_fullColumn is { } fullColumn)
            return fullColumn.VolumeBiomes[localX, localZ];
        return _volumeBiomes![Index(localX, localZ)];
    }

    private static int Index(int localX, int localZ)
    {
        if ((uint)localX >= Chunk.Size || (uint)localZ >= Chunk.Size)
            throw new ArgumentOutOfRangeException(nameof(localX));
        return localZ * Chunk.Size + localX;
    }
}

/// <summary>
/// Immutable bounded 3D query snapshot with Z/Y/X row major storage.
/// Solids are stored as a compact bitset; exact density values are expanded
/// lazily for query consumers that request them.
/// </summary>
public sealed class TerrainDensityVolume
{
    private double[]? _densities;
    private readonly ushort[]? _columnSolidMask;
    private readonly Func<int, int, int, double>? _densityProvider;
    private readonly object _densityGate = new();
    private readonly VolumeBiomePlacementGrid? _volumeBiomes;
    private readonly VolumeBiomeField? _volumeBiomeField;

    internal TerrainDensityVolume(
        int width,
        int height,
        int depth,
        double[] densities,
        VolumeBiomePlacementGrid? volumeBiomes = null,
        VolumeBiomeField? volumeBiomeField = null)
    {
        if ((volumeBiomes is null) !=
            (volumeBiomeField is null))
        {
            throw new ArgumentException(
                "Volume biome snapshot metadata must be supplied together.");
        }

        Width = width;
        Height = height;
        Depth = depth;
        _densities = densities ??
            throw new ArgumentNullException(nameof(densities));
        _volumeBiomes = volumeBiomes;
        _volumeBiomeField = volumeBiomeField;
    }

    internal TerrainDensityVolume(
        int width,
        int height,
        int depth,
        ushort[] columnSolidMask,
        Func<int, int, int, double> densityProvider,
        VolumeBiomePlacementGrid? volumeBiomes,
        VolumeBiomeField? volumeBiomeField)
    {
        if ((volumeBiomes is null) !=
            (volumeBiomeField is null))
        {
            throw new ArgumentException(
                "Volume biome snapshot metadata must be supplied together.");
        }

        Width = width;
        Height = height;
        Depth = depth;
        if (height > sizeof(ushort) * 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Column solidity masks support at most 16 voxels of vertical height.");
        }

        _columnSolidMask = columnSolidMask ??
            throw new ArgumentNullException(nameof(columnSolidMask));
        _densityProvider = densityProvider ??
            throw new ArgumentNullException(nameof(densityProvider));
        _volumeBiomes = volumeBiomes;
        _volumeBiomeField = volumeBiomeField;
    }

    internal TerrainDensityVolume(
        int width,
        int height,
        int depth,
        Func<int, int, int, double> densityProvider,
        VolumeBiomePlacementGrid? volumeBiomes,
        VolumeBiomeField? volumeBiomeField)
    {
        if ((volumeBiomes is null) != (volumeBiomeField is null))
        {
            throw new ArgumentException(
                "Volume biome snapshot metadata must be supplied together.");
        }

        if (width is < 1 or > Chunk.Size ||
            height is < 1 or > Chunk.Size ||
            depth is < 1 or > Chunk.Size)
            throw new ArgumentOutOfRangeException(nameof(width));

        Width = width;
        Height = height;
        Depth = depth;
        _densityProvider = densityProvider ??
            throw new ArgumentNullException(nameof(densityProvider));
        _volumeBiomes = volumeBiomes;
        _volumeBiomeField = volumeBiomeField;
    }

    public int Width { get; }

    public int Height { get; }

    public int Depth { get; }

    public double DensityAt(
        int localX,
        int localY,
        int localZ)
    {
        var index = Index(localX, localY, localZ);
        if (_columnSolidMask is null &&
            Volatile.Read(ref _densities) is null)
            return _densityProvider!(localX, localY, localZ);

        var densities = Volatile.Read(ref _densities);
        if (densities is null)
        {
            lock (_densityGate)
            {
                densities = Volatile.Read(ref _densities);
                if (densities is null)
                {
                    if (_densityProvider is null)
                    {
                        throw new InvalidOperationException(
                            "Density values are unavailable for this volume.");
                    }

                    densities = new double[
                        checked(Width * Height * Depth)];
                    for (var z = 0; z < Depth; z++)
                    for (var y = 0; y < Height; y++)
                    for (var x = 0; x < Width; x++)
                    {
                        densities[(z * Height + y) * Width + x] =
                            _densityProvider(x, y, z);
                    }

                    Volatile.Write(ref _densities, densities);
                }
            }
        }

        return densities[index];
    }

    /// <summary>
    /// Tests voxel occupancy without retaining a double per voxel in the
    /// materialization path.
    /// </summary>
    public bool IsSolidAt(
        int localX,
        int localY,
        int localZ)
    {
        if (_columnSolidMask is { } columnSolidMask)
        {
            if ((uint)localY >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(localY));
            }

            var columnIndex = ColumnIndex(localX, localZ);
            return (columnSolidMask[columnIndex] & (1u << localY)) != 0;
        }

        var index = Index(localX, localY, localZ);
        var densities = Volatile.Read(ref _densities);
        if (densities is not null)
        {
            return densities[index] >= 0d;
        }

        return _densityProvider!(localX, localY, localZ) >= 0d;
    }

    /// <summary>
    /// Counts the contiguous solid voxels immediately above a position while
    /// they remain inside this snapshot. The result is capped by the caller's
    /// finite material depth. The caller can continue with world density only
    /// when this run reaches the top of the sampled volume.
    /// </summary>
    internal uint ContiguousSolidDepthAboveInVolume(
        int localX,
        int localY,
        int localZ,
        uint maximumDepth)
    {
        if ((uint)localX >= Width ||
            (uint)localZ >= Depth)
            throw new ArgumentOutOfRangeException(nameof(localX));
        if (localY < -1)
            throw new ArgumentOutOfRangeException(nameof(localY));
        if (maximumDepth == 0 || localY >= Height - 1)
            return 0;

        var firstAboveY = localY + 1;
        var availableHeight = Height - firstAboveY;
        var limit = Math.Min(maximumDepth, (uint)availableHeight);
        if (limit == 0)
            return 0;

        if (_columnSolidMask is { } columnSolidMask)
        {
            var shiftedMask = (uint)(
                columnSolidMask[ColumnIndex(localX, localZ)] >>
                firstAboveY);
            var contiguous = (uint)System.Numerics.BitOperations
                .TrailingZeroCount(~shiftedMask);
            return Math.Min(limit, contiguous);
        }

        var depth = 0u;
        while (depth < limit &&
               IsSolidAtUnchecked(
                   localX,
                   firstAboveY + (int)depth,
                   localZ))
        {
            depth++;
        }

        return depth;
    }

    /// <summary>
    /// Fast occupancy read for materializer loops that have already proven
    /// all coordinates are inside this chunk's compact column-mask volume.
    /// </summary>
    internal bool IsSolidAtUnchecked(
        int localX,
        int localY,
        int localZ)
    {
        if (_columnSolidMask is { } mask)
            return (mask[(localZ * Width) + localX] & (1u << localY)) != 0;

        var index = (localZ * Height + localY) * Width + localX;
        var densities = Volatile.Read(ref _densities);
        return densities is not null
            ? densities[index] >= 0d
            : _densityProvider!(localX, localY, localZ) >= 0d;
    }

    internal ushort SolidColumnMaskAtUnchecked(
        int localX,
        int localZ)
    {
        if (_columnSolidMask is { } columnMasks)
            return columnMasks[(localZ * Width) + localX];

        ushort mask = 0;
        for (var y = 0; y < Height; y++)
        {
            if (IsSolidAtUnchecked(localX, y, localZ))
                mask |= (ushort)(1u << y);
        }

        return mask;
    }

    // Materialization traverses one Y layer at a time. Expose the compact
    // per-column masks so the hot loop can hoist the array lookup and bit
    // selection outside TerrainDensityVolume's general-purpose accessor.
    internal ushort[]? ColumnSolidMasks => _columnSolidMask;

    internal BiomeSample? VolumePlacementAt(
        int localX,
        int localZ)
    {
        if (_volumeBiomes is null)
        {
            throw new InvalidOperationException(
                "Density volume does not carry volume-biome placement metadata.");
        }

        return _volumeBiomes[
            localX,
            localZ];
    }

    internal BiomeSample? VolumeBiomeAt(
        int localX,
        int worldY,
        int localZ)
    {
        if (_volumeBiomes is null ||
            _volumeBiomeField is null)
        {
            throw new InvalidOperationException(
                "Density volume does not carry volume-biome placement metadata.");
        }
        return _volumeBiomeField.SampleAtY(
            _volumeBiomes,
            localX,
            worldY,
            localZ);
    }

    private int ColumnIndex(
        int localX,
        int localZ)
    {
        if ((uint)localX >= Width ||
            (uint)localZ >= Depth)
        {
            throw new ArgumentOutOfRangeException(nameof(localX));
        }

        return localZ * Width + localX;
    }

    private int Index(
        int localX,
        int localY,
        int localZ)
    {
        if ((uint)localX >= Width ||
            (uint)localY >= Height ||
            (uint)localZ >= Depth)
        {
            throw new ArgumentOutOfRangeException(nameof(localX));
        }

        return (localZ * Height + localY) * Width + localX;
    }
}
