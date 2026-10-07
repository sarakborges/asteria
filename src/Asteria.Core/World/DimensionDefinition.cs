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

public sealed class DimensionEnvironmentDefinition
{
    public DimensionEnvironmentDefinition(
        DimensionColor backgroundColor,
        DimensionColor ambientColor,
        float ambientEnergy,
        DimensionColor fogColor,
        float fogDensity)
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
    }

    public DimensionColor BackgroundColor { get; }

    public DimensionColor AmbientColor { get; }

    public float AmbientEnergy { get; }

    public DimensionColor FogColor { get; }

    public float FogDensity { get; }
}

public sealed class DimensionDefinition
{
    public DimensionDefinition(
        DimensionId id,
        IEnumerable<string> biomes,
        int seaLevel,
        float gravityStrength,
        DimensionSpawnDefinition spawn,
        DimensionEnvironmentDefinition environment,
        DimensionShellDefinition? shell = null,
        DimensionCaveDefinition? caves = null)
    {
        if (!float.IsFinite(gravityStrength) ||
            gravityStrength < 0f ||
            gravityStrength > 256f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gravityStrength),
                "Dimension gravity strength must be finite and within 0..256.");
        }

        var authoredBiomes =
            biomes?.ToArray() ??
            throw new ArgumentNullException(
                nameof(biomes));

        if (authoredBiomes.Length == 0)
        {
            throw new ArgumentException(
                "Dimension must reference at least one biome.",
                nameof(biomes));
        }

        var unique =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var biomeId in authoredBiomes)
        {
            BiomeDefinition.ValidateId(
                biomeId);

            if (!unique.Add(
                    biomeId))
            {
                throw new ArgumentException(
                    $"Dimension {id} repeats biome {biomeId}.",
                    nameof(biomes));
            }
        }

        Id = id;
        Biomes =
            Array.AsReadOnly(
                authoredBiomes);
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
    }

    public DimensionId Id { get; }

    public IReadOnlyList<string> Biomes { get; }

    public int SeaLevel { get; }

    public float GravityStrength { get; }

    public DimensionSpawnDefinition Spawn { get; }

    public DimensionEnvironmentDefinition Environment { get; }

    public DimensionShellDefinition? Shell { get; }

    public DimensionCaveDefinition? Caves { get; }
}


/// <summary>
/// Optional Sphere-wide subtractive cave contribution. A null definition
/// leaves the 3D terrain field without caves.
/// </summary>
public sealed class DimensionCaveDefinition
{
    public DimensionCaveDefinition(
        uint minDepth,
        uint maxDepth,
        uint horizontalScale,
        uint verticalScale,
        float noiseHalfWidth,
        float densityScale,
        uint boundaryFade)
    {
        if (minDepth < 2 || maxDepth <= minDepth ||
            maxDepth > 2048 || boundaryFade == 0 ||
            boundaryFade * 2UL > (ulong)maxDepth - minDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDepth),
                "Cave depth and boundary fade must fit a positive bounded underground band.");
        }

        if (horizontalScale is < 2 or > 16_384 ||
            verticalScale is < 2 or > 16_384)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalScale));
        }

        if (!float.IsFinite(noiseHalfWidth) ||
            noiseHalfWidth is <= 0f or > 1f ||
            !float.IsFinite(densityScale) ||
            densityScale is <= 0f or > 512f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(noiseHalfWidth));
        }

        MinDepth = minDepth;
        MaxDepth = maxDepth;
        HorizontalScale = horizontalScale;
        VerticalScale = verticalScale;
        NoiseHalfWidth = noiseHalfWidth;
        DensityScale = densityScale;
        BoundaryFade = boundaryFade;
    }

    public uint MinDepth { get; }
    public uint MaxDepth { get; }
    public uint HorizontalScale { get; }
    public uint VerticalScale { get; }
    public float NoiseHalfWidth { get; }
    public float DensityScale { get; }
    public uint BoundaryFade { get; }
}
