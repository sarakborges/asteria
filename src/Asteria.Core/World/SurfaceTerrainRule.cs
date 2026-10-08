namespace Asteria.Core.World;

public enum SurfaceHeightInfluencePolicy
{
    Blend,
    LowerOnly,
    Primary,
}

internal sealed class SurfaceTerrainRule
{
    private readonly string _biomeId;
    private readonly BiomeTerrainDefinition _terrain;
    private readonly BiomeTerrainShapeDefinition _shape;
    private readonly IReadOnlyList<BiomeTerrainModifierDefinition> _modifiers;
    private readonly GenerationDomain _macroDomain;
    private readonly GenerationDomain _detailDomain;
    private readonly GenerationDomain _secondaryDomain;
    private readonly GenerationDomain _warpXDomain;
    private readonly GenerationDomain _warpZDomain;
    private readonly GenerationDomain _craterDomain;

    public SurfaceTerrainRule(
        BiomeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var terrain =
            definition.SurfaceTerrain ??
            throw new ArgumentException(
                $"Surface biome {definition.Id} requires surfaceTerrain.");

        _biomeId = definition.Id;
        _terrain = terrain;
        _shape = terrain.Shape;
        _modifiers = terrain.Modifiers;
        _macroDomain =
            GenerationDomain.Named(
                $"terrain/shape/macro/v2/{definition.Id}");
        _detailDomain =
            GenerationDomain.Named(
                $"terrain/shape/detail/v2/{definition.Id}");
        _secondaryDomain =
            GenerationDomain.Named(
                $"terrain/shape/secondary/v2/{definition.Id}");
        _warpXDomain =
            GenerationDomain.Named(
                $"terrain/shape/warp-x/v2/{definition.Id}");
        _warpZDomain =
            GenerationDomain.Named(
                $"terrain/shape/warp-z/v2/{definition.Id}");
        _craterDomain =
            GenerationDomain.Named(
                $"terrain/shape/crater/v2/{definition.Id}");
    }

    public SurfaceHeightInfluencePolicy InfluencePolicy =>
        _terrain.InfluencePolicy;

    public double HeightOffsetAt(
        ulong seed,
        int x,
        int z,
        float terrainStrength)
    {
        var strength =
            Math.Clamp(
                (double)terrainStrength,
                0d,
                1d);

        var craterStrength = strength;
        double height;
        if (_shape is BiomeConeTerrainShapeDefinition cone)
        {
            craterStrength = ConeStrength(seed, x, z, strength, cone);
            height = cone.BaseHeight + cone.Height * craterStrength;
        }
        else
        {
            height =
                _shape switch
                {
                    BiomeNoiseTerrainShapeDefinition noise =>
                        NoiseHeight(seed, x, z, noise),
                    BiomeRollingTerrainShapeDefinition rolling =>
                        RollingHeight(seed, x, z, rolling),
                    BiomeDunesTerrainShapeDefinition dunes =>
                        DunesHeight(seed, x, z, dunes),
                    BiomeRidgesTerrainShapeDefinition ridges =>
                        RidgesHeight(seed, x, z, ridges),
                    BiomeValleyTerrainShapeDefinition valley =>
                        ValleyHeight(seed, x, z, strength, valley),
                    _ => throw new InvalidOperationException(
                        $"Unsupported terrain shape for {_biomeId}."),
                };
        }

        if (_terrain.Crater is { } crater)
        {
            height -= CraterDepthAt(seed, x, z, craterStrength, crater);
        }

        for (var index = 0;
             index < _modifiers.Count;
             index++)
        {
            height +=
                ModifierHeight(
                    seed,
                    x,
                    z,
                    index,
                    strength,
                    _modifiers[index]);
        }

        return height;
    }

    private double NoiseHeight(
        ulong seed,
        int x,
        int z,
        BiomeNoiseTerrainShapeDefinition terrain) =>
        terrain.BaseHeightOffset +
        WorldGenerationEntropy.ValueNoise2D(
            seed,
            _macroDomain,
            x,
            z,
            terrain.MacroScale) *
        terrain.MacroAmplitude +
        WorldGenerationEntropy.ValueNoise2D(
            seed,
            _detailDomain,
            x,
            z,
            terrain.DetailScale) *
        terrain.DetailAmplitude;

    private double RollingHeight(
        ulong seed,
        int x,
        int z,
        BiomeRollingTerrainShapeDefinition terrain) =>
        terrain.BaseHeight +
        Fractal(
            seed,
            _macroDomain,
            x,
            z,
            terrain.Scale) *
        terrain.Amplitude +
        Fractal(
            seed,
            _detailDomain,
            x,
            z,
            terrain.DetailScale) *
        terrain.DetailAmplitude;

    private double DunesHeight(
        ulong seed,
        int x,
        int z,
        BiomeDunesTerrainShapeDefinition terrain)
    {
        var warpX =
            Fractal(
                seed,
                _warpXDomain,
                x,
                z,
                terrain.WarpScale);
        var warpZ =
            Fractal(
                seed,
                _warpZDomain,
                x + 31.7d,
                z - 17.9d,
                terrain.WarpScale);
        var warpedX =
            x +
            warpX *
            terrain.WarpStrength;
        var warpedZ =
            z +
            warpZ *
            terrain.WarpStrength;
        var phase =
            (warpedX +
             warpedZ * terrain.WaveDirectionZ) *
            terrain.Scale *
            Math.Tau;
        var wave =
            Math.Clamp(
                (Math.Sin(phase) + 1d) *
                0.5d,
                0d,
                1d);
        var broad =
            Math.Clamp(
                (Fractal(
                     seed,
                     _secondaryDomain,
                     warpedX,
                     warpedZ,
                     terrain.Scale * terrain.BroadScaleMultiplier) +
                 1d) *
                0.5d,
                0d,
                1d);
        var dune =
            Math.Pow(
                Math.Clamp(
                    wave * terrain.WaveWeight +
                    broad * (1d - terrain.WaveWeight),
                    0d,
                    1d),
                terrain.Sharpness);
        var detail =
            Fractal(
                seed,
                _detailDomain,
                x,
                z,
                terrain.DetailScale);

        return terrain.BaseHeight +
               dune *
               terrain.Amplitude +
               detail *
               terrain.DetailAmplitude;
    }

    private double RidgesHeight(
        ulong seed,
        int x,
        int z,
        BiomeRidgesTerrainShapeDefinition terrain)
    {
        var broad = Fractal(seed, _macroDomain, x, z, terrain.Scale);
        var ridge = Math.Pow(
            Math.Clamp(1d - Math.Abs(broad), 0d, 1d),
            terrain.Sharpness);
        if (terrain.DetailAmplitude == 0f)
        {
            return terrain.BaseHeight + ridge * terrain.Amplitude;
        }

        var detail = Fractal(seed, _detailDomain, x, z, terrain.DetailScale);
        var detailShape = terrain.DetailMode switch
        {
            BiomeRidgeDetailMode.Ridged => Math.Pow(
                Math.Clamp(1d - Math.Abs(detail), 0d, 1d),
                terrain.DetailSharpness),
            BiomeRidgeDetailMode.Modulated => detail * ridge,
            _ => throw new InvalidOperationException(
                $"Unsupported ridge detail mode for {_biomeId}."),
        };
        return terrain.BaseHeight +
               ridge * terrain.Amplitude +
               detailShape * terrain.DetailAmplitude;
    }

    private double ValleyHeight(
        ulong seed,
        int x,
        int z,
        double terrainStrength,
        BiomeValleyTerrainShapeDefinition terrain)
    {
        var strength = WorldGenerationEntropy.SmoothStep(terrainStrength);
        var topNoise = Fractal(seed, _macroDomain, x, z, terrain.TopScale);
        var floorNoise = Fractal(seed, _detailDomain, x, z, terrain.FloorScale);
        var rimShape = Math.Pow(1d - strength, terrain.RimFalloff);
        var floorShape = Math.Pow(strength, terrain.FloorFalloff);
        return terrain.BaseHeight +
               terrain.WallHeight * (1d - strength) +
               topNoise * terrain.TopAmplitude * rimShape -
               terrain.Depth * strength +
               floorNoise * terrain.FloorAmplitude * floorShape;
    }

    private double DepressionsHeight(
        ulong seed,
        int x,
        int z,
        int index,
        double terrainStrength,
        BiomeDepressionsTerrainModifierDefinition definition)
    {
        var prefix = $"terrain/shape/modifier/{index}/depressions/v1/{_biomeId}";
        var broadDomain = GenerationDomain.Named(prefix + "/broad");
        var detailDomain = GenerationDomain.Named(prefix + "/detail");
        var broad = Fractal(seed, broadDomain, x, z, definition.BroadScale);
        var detail = Fractal(seed, detailDomain, x, z, definition.DetailScale);
        var signal =
            broad * definition.BroadWeight +
            detail * (1d - definition.BroadWeight);
        var depressionStrength = Math.Pow(
            WorldGenerationEntropy.SmoothStep(
                Math.Clamp(
                    (signal + definition.Bias) / definition.TransitionWidth,
                    0d,
                    1d)),
            definition.Sharpness);
        return -definition.Depth *
               WorldGenerationEntropy.SmoothStep(terrainStrength) *
               depressionStrength;
    }

    private double ConeStrength(
        ulong seed,
        int x,
        int z,
        double strength,
        BiomeConeTerrainShapeDefinition cone)
    {
        var broad =
            Fractal(seed, _macroDomain, x, z, cone.IrregularityScale);
        var detail =
            Fractal(seed, _detailDomain, x, z, cone.DetailScale);
        var slopeBand =
            cone.SlopeNoiseGain * strength * (1d - strength);
        return Math.Clamp(
            strength +
            (broad * cone.Irregularity +
             detail * cone.DetailIrregularity) * slopeBand,
            0d,
            1d);
    }

    private double CraterDepthAt(
        ulong seed,
        int x,
        int z,
        double strength,
        BiomeCraterDefinition crater)
    {
        var noise =
            Fractal(seed, _craterDomain, x, z, crater.NoiseScale);
        var start =
            Math.Clamp(
                1d - crater.Radius + noise * crater.Irregularity,
                0d,
                0.99d);
        var width =
            Math.Max(crater.TransitionWidth, 1d - start);
        var depthStrength =
            WorldGenerationEntropy.SmoothStep(
                Math.Clamp((strength - start) / width, 0d, 1d));
        return crater.Depth * depthStrength;
    }

    private double ModifierHeight(
        ulong seed,
        int x,
        int z,
        int index,
        double terrainStrength,
        BiomeTerrainModifierDefinition modifier)
    {
        if (modifier is BiomeDepressionsTerrainModifierDefinition depressions)
        {
            return DepressionsHeight(seed, x, z, index, terrainStrength, depressions);
        }

        if (modifier is
            BiomeHeightOffsetTerrainModifierDefinition heightOffset)
        {
            return heightOffset.Height;
        }

        if (modifier is not
            BiomeCliffsTerrainModifierDefinition cliffs)
        {
            throw new InvalidOperationException(
                $"Unsupported terrain modifier for {_biomeId}.");
        }

        var prefix =
            $"terrain/shape/modifier/{index}/v2/{_biomeId}";
        var warpXDomain =
            GenerationDomain.Named(
                prefix +
                "/warp-x");
        var warpZDomain =
            GenerationDomain.Named(
                prefix +
                "/warp-z");
        var noiseDomain =
            GenerationDomain.Named(
                prefix +
                "/noise");
        var warpX =
            Fractal(
                seed,
                warpXDomain,
                x,
                z,
                cliffs.WarpScale);
        var warpZ =
            Fractal(
                seed,
                warpZDomain,
                x - 23.1d,
                z + 41.9d,
                cliffs.WarpScale);
        var warpedX =
            x +
            warpX *
            cliffs.WarpStrength;
        var warpedZ =
            z +
            warpZ *
            cliffs.WarpStrength;
        var value =
            Math.Clamp(
                (Fractal(
                     seed,
                     noiseDomain,
                     warpedX,
                     warpedZ,
                     cliffs.Scale) +
                 1d) *
                0.5d,
                0d,
                1d);
        var halfEdge =
            cliffs.EdgeWidth *
            0.5d;
        var lower =
            Math.Clamp(
                cliffs.Threshold -
                halfEdge,
                0d,
                1d);
        var upper =
            Math.Clamp(
                cliffs.Threshold +
                halfEdge,
                0d,
                1d);
        var progress =
            upper >
            lower
                ? Math.Clamp(
                    (value - lower) /
                    (upper - lower),
                    0d,
                    1d)
                : value >=
                  cliffs.Threshold
                    ? 1d
                    : 0d;

        return WorldGenerationEntropy
                   .SmoothStep(
                       progress) *
               cliffs.Height;
    }

    private static double Fractal(
        ulong seed,
        GenerationDomain domain,
        double x,
        double z,
        double frequency) =>
        WorldGenerationNoise
            .FractalNoise2D(
                seed,
                domain,
                x,
                z,
                frequency);
}
