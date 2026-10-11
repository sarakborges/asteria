namespace Asteria.Core.World;

public readonly record struct DimensionColor(
    byte Red,
    byte Green,
    byte Blue)
{
    public static DimensionColor ParseHex(
        string value,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 6 ||
            !uint.TryParse(
                value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var packed))
        {
            throw new FormatException(
                $"{fieldName} must be a 6-digit RGB hex string.");
        }

        return new DimensionColor(
            (byte)(packed >> 16),
            (byte)(packed >> 8),
            (byte)packed);
    }
}

public sealed record DimensionSpawnDefinition(
    int X,
    int Z);

public sealed class DimensionShellDefinition
{
    public DimensionShellDefinition(
        string block,
        int? floorY = null,
        int? roofY = null)
    {
        BlockDefinition.ValidateId(
            block);

        if (floorY is null &&
            roofY is null)
        {
            throw new ArgumentException(
                "Sphere shell must define a floor, a roof, or both.");
        }

        if (floorY is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(floorY),
                "Sphere shell floor cannot use negative Y.");
        }

        if (roofY is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roofY),
                "Sphere shell roof cannot use negative Y.");
        }

        if (floorY is { } floor &&
            roofY is { } roof &&
            (long)roof - floor <= 1L)
        {
            throw new ArgumentException(
                "Sphere shell roof must leave at least one interior voxel above the floor.");
        }

        Block = block;
        FloorY = floorY;
        RoofY = roofY;
    }

    public string Block { get; }

    public int? FloorY { get; }

    public int? RoofY { get; }
}

/// <summary>
/// Minimum terrain-height envelope authored against a named biome's
/// normalized dominance at a border. Heights are relative to sea level.
/// </summary>
public sealed class DimensionShoreProfileDefinition
{
    public static DimensionShoreProfileDefinition Default { get; } = new(
    [
        new DimensionShoreSampleDefinition(0d, 0d, 0d),
        new DimensionShoreSampleDefinition(0.15d, 0d, 0d),
        new DimensionShoreSampleDefinition(0.28d, 0d, 1d),
        new DimensionShoreSampleDefinition(0.38d, 2d, 1d),
        new DimensionShoreSampleDefinition(0.50d, 2d, 1d),
        new DimensionShoreSampleDefinition(0.62d, 2d, 1d),
        new DimensionShoreSampleDefinition(0.72d, -4d, 1d),
        new DimensionShoreSampleDefinition(0.85d, -4d, 0d),
        new DimensionShoreSampleDefinition(1d, -4d, 0d),
    ]);

    public DimensionShoreProfileDefinition(
        IEnumerable<DimensionShoreSampleDefinition> samples)
    {
        var authored = samples?.ToArray() ??
            throw new ArgumentNullException(nameof(samples));
        if (authored.Length is < 2 or > 32 ||
            authored.Any(sample => sample is null) ||
            authored[0].Dominance != 0d ||
            authored[^1].Dominance != 1d ||
            authored[0].Strength != 0d ||
            authored[^1].Strength != 0d)
        {
            throw new ArgumentException(
                "Shore profiles require 2..32 samples, 0/1 dominance endpoints and inactive endpoints.",
                nameof(samples));
        }

        for (var i = 1; i < authored.Length; i++)
        {
            if (authored[i].Dominance <= authored[i - 1].Dominance)
            {
                throw new ArgumentException(
                    "Shore sample dominance must increase strictly.",
                    nameof(samples));
            }
        }

        Samples = Array.AsReadOnly(authored);
    }

    public IReadOnlyList<DimensionShoreSampleDefinition> Samples { get; }
}

public sealed class DimensionShoreSampleDefinition
{
    public DimensionShoreSampleDefinition(
        double dominance,
        double minimumHeight,
        double strength)
    {
        if (!double.IsFinite(dominance) ||
            dominance is < 0d or > 1d ||
            !double.IsFinite(minimumHeight) ||
            minimumHeight is < -128d or > 128d ||
            !double.IsFinite(strength) ||
            strength is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dominance),
                "Shore dominance and strength must be within 0..1 and the height floor within -128..128.");
        }

        Dominance = dominance;
        MinimumHeight = minimumHeight;
        Strength = strength;
    }

    public double Dominance { get; }
    public double MinimumHeight { get; }
    public double Strength { get; }
}

public sealed class DimensionGeneratedOceanDefinition
{
    public DimensionGeneratedOceanDefinition(
        string biome,
        string fluid,
        DimensionShoreProfileDefinition? shore = null)
    {
        BiomeDefinition.ValidateId(biome);
        FluidDefinition.ValidateId(fluid);
        Biome = biome;
        Fluid = fluid;
        Shore =
            shore ??
            DimensionShoreProfileDefinition.Default;
    }

    public string Biome { get; }

    public string Fluid { get; }

    public DimensionShoreProfileDefinition Shore { get; }
}

public sealed class DimensionGeneratedSurfaceFluidDefinition
{
    public DimensionGeneratedSurfaceFluidDefinition(
        string biome,
        string fluid,
        int spacing,
        int radius,
        int jitter = 0,
        float chance = 1f,
        int depth = 1)
    {
        BiomeDefinition.ValidateId(
            biome);
        FluidDefinition.ValidateId(
            fluid);

        if (spacing is < 2 or > 512)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spacing),
                "Generated surface fluid spacing must be within 2..512.");
        }

        if (radius is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "Generated surface fluid radius must be within 1..256.");
        }

        if (jitter < 0 ||
            jitter > spacing / 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(jitter),
                "Generated surface fluid jitter must be within 0..spacing/2.");
        }

        if ((long)radius +
            jitter >
            spacing)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "Generated surface fluid radius + jitter must not exceed spacing.");
        }

        if (!float.IsFinite(
                chance) ||
            chance <= 0f ||
            chance > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chance),
                "Generated surface fluid chance must be within (0, 1].");
        }

        if (depth is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                "Generated surface fluid depth must be within 1..4.");
        }

        Biome = biome;
        Fluid = fluid;
        Spacing = spacing;
        Radius = radius;
        Jitter = jitter;
        Chance = chance;
        Depth = depth;
    }

    public string Biome { get; }

    public string Fluid { get; }

    public int Spacing { get; }

    public int Radius { get; }

    public int Jitter { get; }

    public float Chance { get; }

    public int Depth { get; }
}

public enum DimensionGeneratedSurfaceStructurePlacement
{
    BiomeInterior,
    BiomeMargin,
}

public sealed class DimensionGeneratedSurfaceStructureDefinition
{
    public DimensionGeneratedSurfaceStructureDefinition(
        string biome,
        string structure,
        int spacing,
        float chance,
        int jitter = 0,
        DimensionGeneratedSurfaceStructurePlacement placement =
            DimensionGeneratedSurfaceStructurePlacement.BiomeInterior,
        SurfaceHabitatWeights? habitatWeights = null)
    {
        BiomeDefinition.ValidateId(
            biome);
        StructureDefinition.ValidateId(
            structure);

        if (spacing is < 4 or > 16_384)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spacing),
                "Generated surface structure spacing must be within 4..16384.");
        }

        if (jitter < 0 ||
            jitter > spacing / 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(jitter),
                "Generated surface structure jitter must be within 0..spacing/2.");
        }

        if (!float.IsFinite(chance) ||
            chance is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chance),
                "Generated surface structure chance must be within 0..1.");
        }

        Biome = biome;
        Structure = structure;
        Spacing = spacing;
        Chance = chance;
        Jitter = jitter;
        Placement = placement;
        HabitatWeights = habitatWeights;
        if (habitatWeights is not null &&
            placement == DimensionGeneratedSurfaceStructurePlacement.BiomeMargin)
            throw new ArgumentException("Habitat-weighted roots require biomeInterior.");
    }

    public string Biome { get; }

    public string Structure { get; }

    public int Spacing { get; }

    public float Chance { get; }

    public int Jitter { get; }

    public DimensionGeneratedSurfaceStructurePlacement Placement { get; }
    public SurfaceHabitatWeights? HabitatWeights { get; }
}

public sealed class DimensionSkyLayersDefinition
{
    public static DimensionSkyLayersDefinition Disabled { get; } =
        new(0f, new DimensionColor(255, 255, 255),
            0f, new DimensionColor(255, 255, 255));

    public DimensionSkyLayersDefinition(
        float starDensity,
        DimensionColor starColor,
        float cloudDensity,
        DimensionColor cloudColor)
    {
        if (!float.IsFinite(starDensity) || starDensity is < 0 or > 1 ||
            !float.IsFinite(cloudDensity) || cloudDensity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(starDensity),
                "Sky-layer densities must be finite fractions between 0 and 1.");
        }
        StarDensity = starDensity;
        StarColor = starColor;
        CloudDensity = cloudDensity;
        CloudColor = cloudColor;
    }

    public float StarDensity { get; }
    public DimensionColor StarColor { get; }
    public float CloudDensity { get; }
    public DimensionColor CloudColor { get; }
}

public sealed class DimensionEnvironmentDefinition
{
    public DimensionEnvironmentDefinition(
        DimensionColor backgroundColor,
        DimensionColor ambientColor,
        float ambientEnergy,
        DimensionColor fogColor,
        float fogDensity,
        DimensionWindDefinition? wind = null,
        DimensionSkyLayersDefinition? skyLayers = null)
    {
        if (!float.IsFinite(ambientEnergy) ||
            ambientEnergy < 0f ||
            ambientEnergy > 8f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ambientEnergy),
                "Ambient energy must be finite and within 0..8.");
        }

        if (!float.IsFinite(fogDensity) ||
            fogDensity < 0f ||
            fogDensity > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fogDensity),
                "Fog density must be finite and within 0..1.");
        }

        BackgroundColor = backgroundColor;
        AmbientColor = ambientColor;
        AmbientEnergy = ambientEnergy;
        FogColor = fogColor;
        FogDensity = fogDensity;
        Wind = wind ?? DimensionWindDefinition.Default;
        SkyLayers = skyLayers ?? DimensionSkyLayersDefinition.Disabled;
    }

    public DimensionColor BackgroundColor { get; }

    public DimensionColor AmbientColor { get; }

    public float AmbientEnergy { get; }

    public DimensionColor FogColor { get; }

    public float FogDensity { get; }
    public DimensionWindDefinition Wind { get; }
    public DimensionSkyLayersDefinition SkyLayers { get; }
}

public sealed class DimensionDefinition
{
    public DimensionDefinition(
        DimensionId id,
        IEnumerable<string> surfaceBiomes,
        int seaLevel,
        float gravityStrength,
        DimensionSpawnDefinition spawn,
        DimensionEnvironmentDefinition environment,
        DimensionShellDefinition? shell = null,
        DimensionCaveDefinition? caves = null,
        DimensionGeneratedOceanDefinition? generatedOcean = null,
        IEnumerable<string>? volumeBiomes = null,
        IEnumerable<DimensionGeneratedSurfaceStructureDefinition>? generatedSurfaceStructures = null,
        IEnumerable<DimensionGeneratedSurfaceFluidDefinition>? generatedSurfaceFluids = null,
        string? dayNightCycleId = null,
        BiomeBlendingDefinition? biomeBlending = null)
    {
        if (!float.IsFinite(gravityStrength) ||
            gravityStrength < 0f ||
            gravityStrength > 256f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gravityStrength),
                "Dimension gravity strength must be finite and within 0..256.");
        }

        var authoredSurfaceBiomes =
            surfaceBiomes?.ToArray() ??
            throw new ArgumentNullException(
                nameof(surfaceBiomes));
        var authoredVolumeBiomes =
            volumeBiomes?.ToArray() ??
            Array.Empty<string>();
        var authoredSurfaceStructures =
            generatedSurfaceStructures?.ToArray() ??
            Array.Empty<DimensionGeneratedSurfaceStructureDefinition>();
        var authoredSurfaceFluids =
            generatedSurfaceFluids?.ToArray() ??
            Array.Empty<DimensionGeneratedSurfaceFluidDefinition>();

        if (authoredSurfaceBiomes.Length == 0)
        {
            throw new ArgumentException(
                "Dimension must reference at least one surface biome.",
                nameof(surfaceBiomes));
        }

        var unique =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var biomeId in
                 authoredSurfaceBiomes)
        {
            BiomeDefinition.ValidateId(
                biomeId);

            if (!unique.Add(
                    biomeId))
            {
                throw new ArgumentException(
                    $"Dimension {id} repeats biome {biomeId}.",
                    nameof(surfaceBiomes));
            }
        }

        foreach (var biomeId in
                 authoredVolumeBiomes)
        {
            BiomeDefinition.ValidateId(
                biomeId);

            if (!unique.Add(
                    biomeId))
            {
                throw new ArgumentException(
                    $"Dimension {id} repeats biome {biomeId} across placement domains.",
                    nameof(volumeBiomes));
            }
        }

        var generatedSurfaceFluidBiomes =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var generated in
                 authoredSurfaceFluids)
        {
            if (!authoredSurfaceBiomes.Contains(
                    generated.Biome,
                    StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Dimension {id} generated surface fluid biome {generated.Biome} must be part of the surface biome pool.",
                    nameof(generatedSurfaceFluids));
            }

            if (!generatedSurfaceFluidBiomes.Add(
                    generated.Biome))
            {
                throw new ArgumentException(
                    $"Dimension {id} repeats generated surface fluid for biome {generated.Biome}.",
                    nameof(generatedSurfaceFluids));
            }
        }

        var generatedStructureKeys =
            new HashSet<(string Biome, string Structure)>();

        foreach (var generated in
                 authoredSurfaceStructures)
        {
            if (!authoredSurfaceBiomes.Contains(
                    generated.Biome,
                    StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Dimension {id} generated surface structure biome {generated.Biome} must be part of the surface biome pool.",
                    nameof(generatedSurfaceStructures));
            }

            if (!generatedStructureKeys.Add(
                    (
                        generated.Biome,
                        generated.Structure)))
            {
                throw new ArgumentException(
                    $"Dimension {id} repeats generated surface structure {generated.Structure} for biome {generated.Biome}.",
                    nameof(generatedSurfaceStructures));
            }
        }

        if (generatedOcean is { } ocean &&
            !authoredSurfaceBiomes.Contains(
                ocean.Biome,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Dimension {id} generated ocean biome {ocean.Biome} must be part of the surface biome pool.",
                nameof(generatedOcean));
        }

        if (dayNightCycleId is not null)
        {
            BlockDefinition.ValidateId(dayNightCycleId);
        }

        Id = id;
        DayNightCycleId = dayNightCycleId;
        SurfaceBiomes =
            Array.AsReadOnly(
                authoredSurfaceBiomes);
        VolumeBiomes =
            Array.AsReadOnly(
                authoredVolumeBiomes);
        SeaLevel =
            seaLevel;
        GravityStrength =
            gravityStrength;
        Spawn =
            spawn ??
            throw new ArgumentNullException(
                nameof(spawn));
        Environment =
            environment ??
            throw new ArgumentNullException(
                nameof(environment));
        Shell = shell;
        Caves = caves;
        BiomeBlending = biomeBlending ?? BiomeBlendingDefinition.Default;
        GeneratedOcean = generatedOcean;
        GeneratedSurfaceFluids =
            Array.AsReadOnly(
                authoredSurfaceFluids);
        GeneratedSurfaceStructures =
            Array.AsReadOnly(
                authoredSurfaceStructures);
    }

    public DimensionId Id { get; }

    public string? DayNightCycleId { get; }

    public IReadOnlyList<string> SurfaceBiomes { get; }

    public IReadOnlyList<string> VolumeBiomes { get; }

    public int SeaLevel { get; }

    public float GravityStrength { get; }

    public DimensionSpawnDefinition Spawn { get; }

    public DimensionEnvironmentDefinition Environment { get; }

    public DimensionShellDefinition? Shell { get; }

    public DimensionCaveDefinition? Caves { get; }

    public BiomeBlendingDefinition BiomeBlending { get; }

    public DimensionGeneratedOceanDefinition? GeneratedOcean { get; }

    public IReadOnlyList<DimensionGeneratedSurfaceFluidDefinition>
        GeneratedSurfaceFluids { get; }

    public IReadOnlyList<DimensionGeneratedSurfaceStructureDefinition>
        GeneratedSurfaceStructures { get; }
}


/// <summary>
/// Sphere-wide subtractive density: independent depth-bounded cave layers,
/// each composed from one or more noise channels.
/// </summary>
public sealed class DimensionCaveDefinition
{
    public DimensionCaveDefinition(
        IEnumerable<DimensionCaveLayerDefinition> layers)
    {
        var authored = layers?.ToArray() ??
            throw new ArgumentNullException(nameof(layers));
        if (authored.Length is < 1 or > 8 ||
            authored.Any(layer => layer is null))
        {
            throw new ArgumentOutOfRangeException(
                nameof(layers),
                "Caves require one to eight authored layers.");
        }

        Layers = Array.AsReadOnly(authored);
        MinimumDepth = authored.Min(layer => layer.MinDepth);
        MaximumDepth = authored.Max(layer => layer.MaxDepth);
    }

    public IReadOnlyList<DimensionCaveLayerDefinition> Layers { get; }
    public uint MinimumDepth { get; }
    public uint MaximumDepth { get; }
}

public enum CaveNoiseCombination
{
    Intersection,
    Union,
}

public sealed class DimensionCaveNoiseChannelDefinition
{
    public DimensionCaveNoiseChannelDefinition(
        uint horizontalScale,
        uint verticalScale)
    {
        if (horizontalScale is < 2 or > 16_384 ||
            verticalScale is < 2 or > 16_384)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalScale),
                "Cave channel noise scales must be within 2..16384.");
        }

        HorizontalScale = horizontalScale;
        VerticalScale = verticalScale;
    }

    public uint HorizontalScale { get; }
    public uint VerticalScale { get; }
}

/// <summary>
/// Organic expansion of existing cave channels into larger chambers.
/// Broad 3D noise changes the cave clearance smoothly, so every chamber
/// remains part of the original tunnel field rather than an isolated cut.
/// </summary>
public sealed class DimensionCaveChamberDefinition
{
    public DimensionCaveChamberDefinition(
        uint horizontalScale,
        uint verticalScale,
        float activationStart,
        float activationFull,
        float maxNoiseHalfWidth)
    {
        if (horizontalScale is < 2 or > 16_384 ||
            verticalScale is < 2 or > 16_384)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalScale),
                "Chamber noise scales must be within 2..16384.");
        }

        if (!float.IsFinite(activationStart) ||
            !float.IsFinite(activationFull) ||
            activationStart < -1f ||
            activationFull > 1f ||
            activationStart >= activationFull)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activationStart),
                "Chamber activation range must increase within -1..1.");
        }

        if (!float.IsFinite(maxNoiseHalfWidth) ||
            maxNoiseHalfWidth is <= 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxNoiseHalfWidth),
                "Chamber maximum half-width must be within (0, 1].");
        }

        HorizontalScale = horizontalScale;
        VerticalScale = verticalScale;
        ActivationStart = activationStart;
        ActivationFull = activationFull;
        MaxNoiseHalfWidth = maxNoiseHalfWidth;
    }

    public uint HorizontalScale { get; }
    public uint VerticalScale { get; }
    public float ActivationStart { get; }
    public float ActivationFull { get; }
    public float MaxNoiseHalfWidth { get; }
}

public sealed class DimensionCaveLayerDefinition
{
    public DimensionCaveLayerDefinition(
        uint minDepth,
        uint maxDepth,
        IEnumerable<DimensionCaveNoiseChannelDefinition> channels,
        float noiseHalfWidth,
        float densityScale,
        uint boundaryFade,
        CaveNoiseCombination combination = CaveNoiseCombination.Intersection,
        DimensionCaveChamberDefinition? chambers = null)
    {
        if (minDepth < 2 || maxDepth <= minDepth ||
            maxDepth > 2048 || boundaryFade == 0 ||
            boundaryFade * 2UL > (ulong)maxDepth - minDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDepth),
                "Cave depth and boundary fade must fit a positive bounded underground band.");
        }

        var authored = channels?.ToArray() ??
            throw new ArgumentNullException(nameof(channels));
        if (authored.Length is < 1 or > 6 ||
            authored.Any(channel => channel is null))
        {
            throw new ArgumentException(
                "Cave layers require one to six noise channels.",
                nameof(channels));
        }

        if (!Enum.IsDefined(combination))
        {
            throw new ArgumentOutOfRangeException(nameof(combination));
        }

        if (!float.IsFinite(noiseHalfWidth) ||
            noiseHalfWidth is <= 0f or > 1f ||
            !float.IsFinite(densityScale) ||
            densityScale is <= 0f or > 512f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(noiseHalfWidth));
        }

        if (chambers is not null &&
            chambers.MaxNoiseHalfWidth <= noiseHalfWidth)
        {
            throw new ArgumentException(
                "Chamber maximum half-width must exceed its parent cave width.",
                nameof(chambers));
        }

        MinDepth = minDepth;
        MaxDepth = maxDepth;
        Channels = Array.AsReadOnly(authored);
        Chambers = chambers;
        NoiseHalfWidth = noiseHalfWidth;
        DensityScale = densityScale;
        BoundaryFade = boundaryFade;
        Combination = combination;
    }

    public uint MinDepth { get; }
    public uint MaxDepth { get; }
    public IReadOnlyList<DimensionCaveNoiseChannelDefinition> Channels { get; }
    public DimensionCaveChamberDefinition? Chambers { get; }
    public float NoiseHalfWidth { get; }
    public float DensityScale { get; }
    public uint BoundaryFade { get; }
    public CaveNoiseCombination Combination { get; }
}
