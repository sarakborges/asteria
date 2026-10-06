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
        float gravityStrength,
        DimensionSpawnDefinition spawn,
        DimensionEnvironmentDefinition environment)
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
    }

    public DimensionId Id { get; }

    public IReadOnlyList<string> Biomes { get; }

    public float GravityStrength { get; }

    public DimensionSpawnDefinition Spawn { get; }

    public DimensionEnvironmentDefinition Environment { get; }
}
