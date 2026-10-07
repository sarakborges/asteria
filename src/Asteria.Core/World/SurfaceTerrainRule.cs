namespace Asteria.Core.World;

internal enum SurfaceHeightInfluencePolicy
{
    Blend,
    LowerOnly,
}

internal sealed class SurfaceTerrainRule
{
    private readonly string _biomeId;
    private readonly BiomeTerrainShapeDefinition _shape;
    private readonly ModifierRule[] _modifiers;
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
        _shape = terrain.Shape;
        _modifiers =
            terrain.Modifiers
                .Select(
                    (modifier, index) =>
                        ModifierRule.Create(
                            definition.Id,
                            index,
                            modifier))
                .ToArray();
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

    public bool IsVolcano =>
        _shape is BiomeVolcanoTerrainShapeDefinition;

    public SurfaceHeightInfluencePolicy InfluencePolicy =>
        _shape is
            BiomeOceanTerrainShapeDefinition or
            BiomeSwampTerrainShapeDefinition
            ? SurfaceHeightInfluencePolicy.LowerOnly
            : SurfaceHeightInfluencePolicy.Blend;

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

        var height =
            _shape switch
            {
                BiomeNoiseTerrainShapeDefinition noise =>
                    NoiseHeight(
                        seed,
                        x,
                        z,
                        noise),
                BiomeRollingTerrainShapeDefinition rolling =>
                    RollingHeight(
                        seed,
                        x,
                        z,
                        rolling),
                BiomeDunesTerrainShapeDefinition dunes =>
                    DunesHeight(
                        seed,
                        x,
                        z,
                        dunes),
                BiomeOceanTerrainShapeDefinition ocean =>
                    OceanHeight(
                        seed,
                        x,
                        z,
                        ocean),
                BiomeSwampTerrainShapeDefinition swamp =>
                    SwampHeight(
                        seed,
                        x,
                        z,
                        strength,
                        swamp),
                BiomeMountainsTerrainShapeDefinition mountains =>
                    MountainsHeight(
                        seed,
                        x,
                        z,
                        mountains),
                BiomeGorgeTerrainShapeDefinition gorge =>
                    GorgeHeight(
                        seed,
                        x,
                        z,
                        strength,
                        gorge),
                BiomeAlpsTerrainShapeDefinition alps =>
                    AlpsHeight(
                        seed,
                        x,
                        z,
                        alps),
                BiomeMountainBeltTerrainShapeDefinition belt =>
                    MountainBeltHeight(
                        seed,
                        x,
                        z,
                        belt),
                BiomeVolcanoTerrainShapeDefinition volcano =>
                    VolcanoHeight(
                        seed,
                        x,
                        z,
                        strength,
                        volcano),
                _ =>
                    throw new InvalidOperationException(
                        $"Unsupported terrain shape for {_biomeId}."),
            };

        for (var index = 0;
             index < _modifiers.Length;
             index++)
        {
            height +=
                ModifierHeight(
                    seed,
                    x,
                    z,
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
             warpedZ * 0.35d) *
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
                     terrain.Scale * 0.55d) +
                 1d) *
                0.5d,
                0d,
                1d);
        var dune =
            Math.Pow(
                Math.Clamp(
                    wave * 0.72d +
                    broad * 0.28d,
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

    private double OceanHeight(
        ulong seed,
        int x,
        int z,
        BiomeOceanTerrainShapeDefinition terrain) =>
        -terrain.Depth +
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

    private double SwampHeight(
        ulong seed,
        int x,
        int z,
        double terrainStrength,
        BiomeSwampTerrainShapeDefinition terrain)
    {
        var strength =
            WorldGenerationEntropy
                .SmoothStep(
                    terrainStrength);
        var broad =
            Fractal(
                seed,
                _macroDomain,
                x,
                z,
                terrain.Scale);
        var detail =
            Fractal(
                seed,
                _detailDomain,
                x,
                z,
                terrain.DetailScale);
        var pondBroad =
            Fractal(
                seed,
                _secondaryDomain,
                x,
                z,
                terrain.Scale *
                3.2d);
        var pondDetail =
            Fractal(
                seed,
                _craterDomain,
                x,
                z,
                terrain.DetailScale *
                0.85d);
        var pondSignal =
            pondBroad *
            0.66d +
            pondDetail *
            0.34d;
        var pondStrength =
            Math.Pow(
                WorldGenerationEntropy
                    .SmoothStep(
                        Math.Clamp(
                            (pondSignal + 0.05d) /
                            0.42d,
                            0d,
                            1d)),
                0.82d);

        return terrain.BaseHeight -
               terrain.Depth *
               strength *
               pondStrength +
               broad *
               terrain.Amplitude +
               detail *
               terrain.DetailAmplitude;
    }

    private double MountainsHeight(
        ulong seed,
        int x,
        int z,
        BiomeMountainsTerrainShapeDefinition terrain)
    {
        var noise =
            Fractal(
                seed,
                _macroDomain,
                x,
                z,
                terrain.Scale);
        var ridge =
            Math.Pow(
                Math.Clamp(
                    1d -
                    Math.Abs(
                        noise),
                    0d,
                    1d),
                terrain.Sharpness);

        return terrain.BaseHeight +
               ridge *
               terrain.Amplitude;
    }

    private double GorgeHeight(
        ulong seed,
        int x,
        int z,
        double terrainStrength,
        BiomeGorgeTerrainShapeDefinition terrain)
    {
        var strength =
            WorldGenerationEntropy
                .SmoothStep(
                    terrainStrength);
        var topNoise =
            Fractal(
                seed,
                _macroDomain,
                x,
                z,
                terrain.TopScale);
        var floorNoise =
            Fractal(
                seed,
                _detailDomain,
                x,
                z,
                terrain.FloorScale);
        var rimShape =
            Math.Pow(
                1d -
                strength,
                0.8d);
        var floorShape =
            Math.Pow(
                strength,
                1.35d);

        return terrain.BaseHeight +
               terrain.WallHeight *
               (1d - strength) +
               topNoise *
               terrain.TopAmplitude *
               rimShape -
               terrain.Depth *
               strength +
               floorNoise *
               terrain.FloorAmplitude *
               floorShape;
    }

    private double AlpsHeight(
        ulong seed,
        int x,
        int z,
        BiomeAlpsTerrainShapeDefinition terrain)
    {
        var broad =
            Fractal(
                seed,
                _macroDomain,
                x,
                z,
                terrain.Scale);
        var ridge =
            Math.Pow(
                Math.Clamp(
                    1d -
                    Math.Abs(
                        broad),
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
        var jagged =
            Math.Pow(
                Math.Clamp(
                    1d -
                    Math.Abs(
                        detail),
                    0d,
                    1d),
                1.35d);

        return terrain.BaseHeight +
               ridge *
               terrain.Amplitude +
               jagged *
               terrain.DetailAmplitude;
    }

    private double MountainBeltHeight(
        ulong seed,
        int x,
        int z,
        BiomeMountainBeltTerrainShapeDefinition terrain)
    {
        var broad =
            Fractal(
                seed,
                _macroDomain,
                x,
                z,
                terrain.Scale);
        var ridge =
            Math.Pow(
                Math.Clamp(
                    1d -
                    Math.Abs(
                        broad),
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
               ridge *
               terrain.Amplitude +
               detail *
               terrain.DetailAmplitude *
               ridge;
    }

    private double VolcanoHeight(
        ulong seed,
        int x,
        int z,
        double terrainStrength,
        BiomeVolcanoTerrainShapeDefinition terrain)
    {
        var strength =
            Math.Clamp(
                terrainStrength,
                0d,
                1d);
        var broad =
            Fractal(
                seed,
                _macroDomain,
                x,
                z,
                terrain.IrregularityScale);
        var detail =
            Fractal(
                seed,
                _detailDomain,
                x,
                z,
                terrain.DetailScale);
        var slopeBand =
            4d *
            strength *
            (1d - strength);
        var distortedStrength =
            Math.Clamp(
                strength +
                (broad *
                     terrain.Irregularity +
                 detail *
                     terrain.DetailIrregularity) *
                slopeBand,
                0d,
                1d);
        var craterNoise =
            Fractal(
                seed,
                _craterDomain,
                x,
                z,
                terrain.IrregularityScale *
                1.7d);
        var craterStart =
            Math.Clamp(
                1d -
                terrain.CraterRadius +
                craterNoise *
                terrain.CraterIrregularity,
                0d,
                0.99d);
        var craterWidth =
            Math.Max(
                0.01d,
                1d -
                craterStart);
        var craterStrength =
            WorldGenerationEntropy
                .SmoothStep(
                    Math.Clamp(
                        (distortedStrength -
                         craterStart) /
                        craterWidth,
                        0d,
                        1d));

        return terrain.BaseHeight +
               terrain.Height *
               distortedStrength -
               terrain.CraterDepth *
               craterStrength;
    }

    private double ModifierHeight(
        ulong seed,
        int x,
        int z,
        ModifierRule rule)
    {
        var modifier =
            rule.Definition;

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

        var warpX =
            Fractal(
                seed,
                rule.WarpXDomain,
                x,
                z,
                cliffs.WarpScale);
        var warpZ =
            Fractal(
                seed,
                rule.WarpZDomain,
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
                     rule.NoiseDomain,
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

    private readonly record struct ModifierRule(
        BiomeTerrainModifierDefinition Definition,
        GenerationDomain WarpXDomain,
        GenerationDomain WarpZDomain,
        GenerationDomain NoiseDomain)
    {
        public static ModifierRule Create(
            string biomeId,
            int index,
            BiomeTerrainModifierDefinition definition)
        {
            var prefix =
                $"terrain/shape/modifier/{index}/v2/{biomeId}";

            return new ModifierRule(
                definition,
                GenerationDomain.Named(
                    prefix +
                    "/warp-x"),
                GenerationDomain.Named(
                    prefix +
                    "/warp-z"),
                GenerationDomain.Named(
                    prefix +
                    "/noise"));
        }
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
