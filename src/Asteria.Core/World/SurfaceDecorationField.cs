using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Resolves authored ground decorators from a sampled surface biome and
/// its actual supporting surface material; never mutates a world.
/// </summary>
public sealed class SurfaceDecorationField
{
    private readonly ulong _seed;
    private readonly SurfaceTerrainField? _terrain;
    private readonly SurfaceHabitatField _habitats;

    private readonly IReadOnlyDictionary<string, DecorationRule[]> _rules;
    private readonly HashSet<string> _objectBiomes;
    private readonly HashSet<string> _verticalBiomes;
    private readonly HashSet<string> _ceilingBiomes;
    private readonly HashSet<string> _wallLayerBiomes;
    private readonly HashSet<string> _wallBlockBiomes;
    private readonly HashSet<string> _wallObjectBiomes;

    public SurfaceDecorationField(
        ulong seed,
        IEnumerable<BiomeDefinition> biomes,
        BlockRegistry blocks,
        SurfaceTerrainField? terrain = null,
        SurfaceHabitatField? habitats = null,
        bool generateDecorations = true,
        FluidRegistry? fluids = null,
        AttachedLayerRegistry? layers = null,
        PackContentRegistry<ObjectDefinition>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(blocks);
        _seed = seed;
        _terrain = terrain;
        var definitions = biomes.ToArray();
        _objectBiomes = definitions
            .Where(b => generateDecorations && objects is not null && b.Decorations.Any(d => d.IsObject))
            .Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        _verticalBiomes = definitions
            .Where(biome => generateDecorations &&
                            biome.SurfaceLayout is null &&
                            biome.Decorations.Count > 0)
            .Select(biome => biome.Id)
            .ToHashSet(StringComparer.Ordinal);
        _ceilingBiomes = definitions
            .Where(biome => generateDecorations &&
                            biome.Decorations.Any(d => d.SupportSurface ==
                                DecorationSupportSurface.Ceiling))
            .Select(biome => biome.Id)
            .ToHashSet(StringComparer.Ordinal);
        _wallLayerBiomes = definitions
            .Where(biome => generateDecorations && layers is not null &&
                biome.Decorations.Any(rule =>
                    rule.IsLayer && rule.SupportSurface == DecorationSupportSurface.Wall))
            .Select(biome => biome.Id)
            .ToHashSet(StringComparer.Ordinal);
        _wallObjectBiomes = definitions
            .Where(biome => generateDecorations && objects is not null &&
                biome.Decorations.Any(rule =>
                    rule.IsObject &&
                    rule.SupportSurface == DecorationSupportSurface.Wall))
            .Select(biome => biome.Id).ToHashSet(StringComparer.Ordinal);
        _wallBlockBiomes = definitions
            .Where(biome => generateDecorations &&
                biome.Decorations.Any(rule =>
                    rule.SupportSurface == DecorationSupportSurface.Wall &&
                    !rule.IsLayer && !rule.IsObject &&
                    blocks.GetDefinition(blocks.GetId(rule.Block))
                        .HasTag(BlockPhysicsCapabilities.SupportWall)))
            .Select(biome => biome.Id)
            .ToHashSet(StringComparer.Ordinal);
        _habitats = habitats ?? new SurfaceHabitatField(seed, definitions);
        _rules = definitions
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(
                biome => biome.Id,
                biome => (generateDecorations
                    ? biome.Decorations.Where(decoration =>
                        decoration.SupportSurface != DecorationSupportSurface.Wall ||
                        (decoration.IsLayer && layers is not null) ||
                        (!decoration.IsLayer && !decoration.IsObject &&
                         blocks.GetDefinition(blocks.GetId(decoration.Block))
                            .HasTag(BlockPhysicsCapabilities.SupportWall)) ||
                        (decoration.IsObject && objects is not null))
                    : Array.Empty<BiomeDecorationDefinition>())
                    .OrderBy(
                        decoration => decoration.Block,
                        StringComparer.Ordinal)
                    .Select(
                        decoration => new DecorationRule(
                            decoration.IsLayer || decoration.IsObject
                                ? BlockRuntimeId.Air
                                : blocks.GetId(decoration.Block),
                            decoration.IsLayer ? decoration.Block : null,
                            decoration.IsObject ? decoration.Block : null,
                            decoration.Chance,
                            decoration.SurfaceBlocks
                                .Select(blocks.GetId)
                                .ToHashSet(),
                            GenerationDomain.Named(
                                $"worldgen/decorator/{biome.Id}/{decoration.Block}/v1"),
                            decoration.Cluster,
                            decoration.Conditions,
                            biome.SurfaceLayout is not null,
                            GenerationDomain.Named(
                                $"worldgen/decorator-cluster/{biome.Id}/{decoration.Block}/v1"),
                            decoration.HabitatWeights,
                            decoration.FluidPlacement,
                            decoration.FluidRequirement is { } requirement
                                ? (fluids ?? throw new ArgumentException(
                                    "Fluid-restricted decorators require a fluid registry.",
                                    nameof(fluids))).GetId(requirement.Fluid)
                                : FluidRuntimeId.None,
                            decoration.FluidRequirement?.Relation ?? DecorationFluidRelation.Nearby,
                            decoration.FluidRequirement?.MaxDistance ?? 0,
                            decoration.SupportSurface))
                    .ToArray(),
                StringComparer.Ordinal);
        foreach (var biome in definitions)
            foreach (var decoration in biome.Decorations)
                _habitats.Validate(biome.Id, decoration.HabitatWeights);
    }

    public bool HasObjectDecorations(BiomeSample sample) =>
        sample.Influences.Any(influence => _objectBiomes.Contains(influence.BiomeId));

    /// <summary>
    /// Samples a conditional placement context once for a support column when
    /// every eligible condition uses the same authoritative height source.
    /// A null result means there are no eligible conditions, or independent
    /// selection must retain its existing per-call sampling behavior.
    /// </summary>
    internal SurfacePlacementContext? SamplePlacementContextIfRequired(
        BiomeSample sample,
        BlockRuntimeId supportBlock,
        int worldX,
        int worldZ,
        DecorationSupportSurface supportSurface = DecorationSupportSurface.Floor)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (_terrain is null)
            return null;

        var foundConditions = false;
        var useBaseSurface = false;
        var includeSlope = false;
        foreach (var influence in sample.Influences)
        {
            if (!_rules.TryGetValue(influence.BiomeId, out var rules))
                continue;

            foreach (var rule in rules)
            {
                if (rule.SupportSurface != supportSurface ||
                    !rule.SurfaceBlocks.Contains(supportBlock) ||
                    rule.Conditions is not { } conditions)
                    continue;

                if (!foundConditions)
                {
                    foundConditions = true;
                    useBaseSurface = rule.UsesBaseSurface;
                }
                else if (useBaseSurface != rule.UsesBaseSurface)
                {
                    // SelectRule owns fallback behavior if authored rules
                    // require different height sources in the same sample.
                    return null;
                }

                includeSlope |= conditions.RequiresSlope;
            }
        }

        return foundConditions
            ? SurfacePlacementContext.Sample(
                _terrain, worldX, worldZ, includeSlope, useBaseSurface)
            : null;
    }

    public bool HasVerticalDecorations => _verticalBiomes.Count > 0;
    public bool HasCeilingDecorations => _ceilingBiomes.Count > 0;
    public bool HasWallLayers => _wallLayerBiomes.Count > 0;
    public bool HasWallBlocks => _wallBlockBiomes.Count > 0;
    public bool HasWallObjects => _wallObjectBiomes.Count > 0;
    public bool HasWallObjectsFor(string biomeId) =>
        _wallObjectBiomes.Contains(biomeId);
    public bool HasWallBlocksFor(string biomeId) =>
        _wallBlockBiomes.Contains(biomeId);
    public bool HasWallLayersFor(string biomeId) =>
        _wallLayerBiomes.Contains(biomeId);
    public bool HasCeilingDecorationsFor(string biomeId) =>
        _ceilingBiomes.Contains(biomeId);

    public bool HasVerticalDecorationsFor(string biomeId) =>
        _verticalBiomes.Contains(biomeId);

    /// <summary>Avoid surface material/fluid work where no influenced biome decorates.</summary>
    public bool HasDecorations(BiomeSample sample)
    {
        foreach (var influence in sample.Influences)
        {
            if (_rules[influence.BiomeId].Any(rule =>
                    rule.SupportSurface != DecorationSupportSurface.Wall))
                return true;
        }

        return false;
    }

    public bool HasFluidSurfaceDecorations(BiomeSample sample)
    {
        foreach (var influence in sample.Influences)
            if (_rules[influence.BiomeId].Any(rule =>
                !rule.RequiredFluid.IsNone &&
                rule.FluidRelation == DecorationFluidRelation.Below))
                return true;
        return false;
    }

    public BlockRuntimeId BlockAt(
        BiomeSample sample,
        BlockRuntimeId surfaceBlock,
        int worldX,
        int worldZ,
        SurfacePlacementContext? suppliedPlacement = null,
        int? verticalY = null,
        bool submerged = false,
        int? supportY = null,
        bool onFluidSurface = false,
        FluidRuntimeId fluidBelow = default,
        Func<FluidRuntimeId, int, int, int, int, bool>? nearbyFluid = null,
        DecorationSupportSurface supportSurface = DecorationSupportSurface.Floor)
    {
        if (supportSurface == DecorationSupportSurface.Wall)
            return BlockRuntimeId.Air;
        return SelectRule(sample, surfaceBlock, worldX, worldZ,
            suppliedPlacement, verticalY, submerged, supportY, onFluidSurface,
            fluidBelow, nearbyFluid, supportSurface, requireObject: false)?.Block ?? BlockRuntimeId.Air;
    }

    public string? ObjectAt(BiomeSample sample, BlockRuntimeId supportBlock,
        int worldX, int worldZ,
        SurfacePlacementContext? placement = null,
        int? verticalY = null, bool submerged = false,
        int? supportY = null,
        Func<FluidRuntimeId, int, int, int, int, bool>? nearbyFluid = null,
        DecorationSupportSurface supportSurface = DecorationSupportSurface.Floor,
        bool onFluidSurface = false, FluidRuntimeId fluidBelow = default)
    {
        if (!HasObjectDecorations(sample)) return null;
        return SelectRule(sample, supportBlock, worldX, worldZ,
            placement, verticalY, submerged, supportY,
            onFluidSurface, fluidBelow, nearbyFluid, supportSurface,
            requireLayer: false, requireObject: true)?.ObjectId;
    }

    public string? WallObjectAt(
        BiomeSample sample, BlockRuntimeId supportBlock,
        int worldX, int worldY, int worldZ)
    {
        if (!_wallObjectBiomes.Contains(sample.Primary)) return null;
        return ObjectAt(sample, supportBlock, worldX, worldZ,
            new SurfacePlacementContext(worldY, 0),
            verticalY: worldY, supportY: worldY,
            supportSurface: DecorationSupportSurface.Wall);
    }

    public string? WallLayerAt(
        BiomeSample sample, BlockRuntimeId supportBlock,
        int worldX, int worldY, int worldZ)
    {
        if (!_wallLayerBiomes.Contains(sample.Primary))
            return null;
        return SelectRule(sample, supportBlock, worldX, worldZ,
            new SurfacePlacementContext(worldY, 0), worldY, false,
            worldY, false, default, null, DecorationSupportSurface.Wall,
            requireLayer: true, requireObject: false)?.LayerId;
    }

    /// <summary>
    /// A projected object occupies the adjacent air voxel, unlike a face
    /// layer, which is owned by the solid support voxel.
    /// </summary>
    public BlockRuntimeId WallBlockAt(
        BiomeSample sample, BlockRuntimeId supportBlock,
        int worldX, int worldY, int worldZ)
    {
        if (!_wallBlockBiomes.Contains(sample.Primary))
            return BlockRuntimeId.Air;
        return SelectRule(sample, supportBlock, worldX, worldZ,
            new SurfacePlacementContext(worldY, 0), worldY, false,
            worldY, false, default, null, DecorationSupportSurface.Wall,
            requireLayer: false, requireObject: false)?.Block ?? BlockRuntimeId.Air;
    }

    // Common authored placement policy for block decorators and face layers.
    private DecorationRule? SelectRule(
        BiomeSample sample, BlockRuntimeId surfaceBlock,
        int worldX, int worldZ,
        SurfacePlacementContext? suppliedPlacement, int? verticalY,
        bool submerged, int? supportY, bool onFluidSurface,
        FluidRuntimeId fluidBelow,
        Func<FluidRuntimeId, int, int, int, int, bool>? nearbyFluid,
        DecorationSupportSurface supportSurface,
        bool? requireLayer = null, bool? requireObject = null)
    {
        SurfacePlacementContext? placement = suppliedPlacement;
        var slopeSampled = suppliedPlacement.HasValue;
        foreach (var influence in sample.Influences)
        {
            double? sampledHabitat = null;
            foreach (var rule in _rules[influence.BiomeId])
            {
                if (rule.SupportSurface != supportSurface ||
                    (requireLayer is { } wanted &&
                     (rule.LayerId is not null) != wanted) ||
                    (requireObject is { } objectWanted &&
                     (rule.ObjectId is not null) != objectWanted))
                    continue;

                var fluidSupported = !rule.RequiredFluid.IsNone &&
                    rule.FluidRelation == DecorationFluidRelation.Below;
                if (fluidSupported != onFluidSurface)
                    continue;
                if (fluidSupported && fluidBelow != rule.RequiredFluid)
                    continue;

                if ((submerged && rule.FluidPlacement == DecorationFluidPlacement.Dry) ||
                    (!submerged && rule.FluidPlacement == DecorationFluidPlacement.Submerged))
                    continue;

                if (!rule.SurfaceBlocks.Contains(surfaceBlock))
                {
                    continue;
                }

                if (rule.Conditions is { } conditions)
                {
                    if (placement is null ||
                        (conditions.RequiresSlope && !slopeSampled))
                    {
                        if (_terrain is null)
                        {
                            throw new InvalidOperationException(
                                "Conditional ground decorators require a terrain field.");
                        }

                        placement = SurfacePlacementContext.Sample(
                            _terrain, worldX, worldZ,
                            conditions.RequiresSlope,
                            rule.UsesBaseSurface);
                        slopeSampled = conditions.RequiresSlope;
                    }

                    if (!conditions.Allows(placement.Value))
                    {
                        continue;
                    }
                }

                double effectiveChance = rule.Chance * influence.Weight;
                if (rule.HabitatWeights is { } weights)
                {
                    sampledHabitat ??= _habitats.Sample(
                        influence.BiomeId, worldX, worldZ);
                    effectiveChance *= _habitats.Weight(
                        influence.BiomeId, weights, sampledHabitat.Value);
                    if (effectiveChance <= 0d)
                        continue;
                }
                if (rule.Cluster is { } cluster)
                {
                    var noise = WorldGenerationNoise.FractalNoise2D(
                        _seed,
                        rule.ClusterDomain,
                        worldX,
                        worldZ,
                        1d / cluster.Scale,
                        octaves: cluster.Octaves);
                    var distribution = cluster.TransitionWidth <= 0f
                        ? (noise >= cluster.Threshold ? 1d : 0d)
                        : WorldGenerationEntropy.SmoothStep(
                            Math.Clamp(
                                (noise - cluster.Threshold) /
                                cluster.TransitionWidth,
                                0d,
                                1d));
                    effectiveChance *= distribution;
                }

                var roll = WorldGenerationEntropy.Unit(
                    verticalY is { } y
                        ? WorldGenerationEntropy.Sample3D(
                            _seed, rule.Domain, worldX, y, worldZ)
                        : WorldGenerationEntropy.Sample2D(
                            _seed, rule.Domain, worldX, worldZ));
                if (roll < effectiveChance)
                {
                    if (!rule.RequiredFluid.IsNone &&
                        rule.FluidRelation == DecorationFluidRelation.Nearby)
                    {
                        if (nearbyFluid is null || supportY is null)
                            throw new InvalidOperationException(
                                "Nearby-fluid decorator needs a fluid query and support Y.");
                        if (!nearbyFluid(rule.RequiredFluid, rule.FluidDistance,
                                worldX, supportY.Value, worldZ))
                            continue;
                    }
                    return rule;
                }
            }
        }

        return null;
    }

    private sealed record DecorationRule(
        BlockRuntimeId Block,
        string? LayerId,
        string? ObjectId,
        float Chance,
        HashSet<BlockRuntimeId> SurfaceBlocks,
        GenerationDomain Domain,
        BiomeDecorationClusterDefinition? Cluster,
        SurfacePlacementConditions? Conditions,
        bool UsesBaseSurface,
        GenerationDomain ClusterDomain,
        SurfaceHabitatWeights? HabitatWeights,
        DecorationFluidPlacement FluidPlacement,
        FluidRuntimeId RequiredFluid,
        DecorationFluidRelation FluidRelation,
        int FluidDistance,
        DecorationSupportSurface SupportSurface);
}
