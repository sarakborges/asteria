using System.Numerics;
using System.Text.Json;

namespace Asteria.Core.World;

public enum AmbientParticleSourceKind : byte
{
    Dimension,
    Biome,
    FluidSurface,
}

public readonly record struct AmbientParticleRange(float Min, float Max)
{
    public void Validate(string name)
    {
        if (!float.IsFinite(Min) || !float.IsFinite(Max) ||
            Min <= 0f || Max < Min)
            throw new FormatException($"{name} must be a positive finite range.");
    }
}

public sealed class AmbientParticleDefinition
{
    private AmbientParticleDefinition(
        string id, AmbientParticleSourceKind sourceKind, string sourceId,
        Vector3 color, float opacity, AmbientParticleRange size,
        AmbientParticleRange lifetime, float spawnRate, Vector3 velocity,
        Vector3 velocityJitter, Vector3 acceleration, float wanderStrength,
        float windInfluence, bool popAtEnd, float spawnRadius, float verticalRange)
    {
        Id = id;
        SourceKind = sourceKind;
        SourceId = sourceId;
        Color = color;
        Opacity = opacity;
        Size = size;
        Lifetime = lifetime;
        SpawnRate = spawnRate;
        Velocity = velocity;
        VelocityJitter = velocityJitter;
        Acceleration = acceleration;
        WanderStrength = wanderStrength;
        WindInfluence = windInfluence;
        PopAtEnd = popAtEnd;
        SpawnRadius = spawnRadius;
        VerticalRange = verticalRange;
    }

    public string Id { get; }
    public AmbientParticleSourceKind SourceKind { get; }
    public string SourceId { get; }
    public Vector3 Color { get; }
    public float Opacity { get; }
    public AmbientParticleRange Size { get; }
    public AmbientParticleRange Lifetime { get; }
    public float SpawnRate { get; }
    public Vector3 Velocity { get; }
    public Vector3 VelocityJitter { get; }
    public Vector3 Acceleration { get; }
    public float WanderStrength { get; }
    public float WindInfluence { get; }
    public bool PopAtEnd { get; }
    public float SpawnRadius { get; }
    public float VerticalRange { get; }

    public static AmbientParticleDefinition Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("Ambient particle definition must be an object.");

        var id = RequiredString(root, "id");
        var source = RequiredObject(root, "source");
        var sourceKind = RequiredString(source, "type") switch
        {
            "dimension" => AmbientParticleSourceKind.Dimension,
            "biome" => AmbientParticleSourceKind.Biome,
            "fluid_surface" => AmbientParticleSourceKind.FluidSurface,
            var value => throw new FormatException($"Unknown particle source type: {value}"),
        };
        var sourceId = RequiredString(source, "id");
        BlockDefinition.ValidateId(id);
        BlockDefinition.ValidateId(sourceId);

        var hsi = RequiredObject(root, "color");
        var hue = RequiredFloat(hsi, "hue");
        var saturation = RequiredFloat(hsi, "saturation");
        var intensity = RequiredFloat(hsi, "intensity");
        if (!float.IsFinite(hue) || !float.IsFinite(saturation) ||
            !float.IsFinite(intensity) || saturation is < 0f or > 1f ||
            intensity is < 0f or > 1f)
            throw new FormatException($"{id}: color HSI must be finite and within its ranges.");
        var size = ReadRange(root, "size", new(0.05f, 0.1f));
        var lifetime = ReadRange(root, "lifetime", new(2f, 4f));
        size.Validate("size");
        lifetime.Validate("lifetime");

        var opacity = OptionalFloat(root, "opacity", 1f);
        var spawnRate = RequiredFloat(root, "spawnRate");
        var wanderStrength = OptionalFloat(root, "wanderStrength", 0f);
        var windInfluence = OptionalFloat(root, "windInfluence", 0f);
        var spawnRadius = OptionalFloat(root, "spawnRadius", 16f);
        var verticalRange = OptionalFloat(root, "verticalRange", 8f);
        if (opacity is < 0f or > 1f ||
            spawnRate is < 0f or > 1024f ||
            wanderStrength < 0f || windInfluence < 0f ||
            spawnRadius is <= 0f or > 256f ||
            verticalRange is < 0f or > 256f)
            throw new FormatException($"{id}: ambient particle numeric parameter is out of range.");

        var velocity = ReadVector(root, "velocity", false);
        var jitter = ReadVector(root, "velocityJitter", true);
        var acceleration = ReadVector(root, "acceleration", false);
        var popAtEnd = false;
        if (root.TryGetProperty("popAtEnd", out var pop))
        {
            if (pop.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new FormatException("popAtEnd must be boolean.");
            popAtEnd = pop.GetBoolean();
        }

        return new AmbientParticleDefinition(
            id, sourceKind, sourceId, HsiToSrgb(hue, saturation, intensity),
            opacity, size, lifetime, spawnRate, velocity, jitter, acceleration,
            wanderStrength, windInfluence, popAtEnd, spawnRadius, verticalRange);
    }

    private static Vector3 HsiToSrgb(float hue, float saturation, float intensity)
    {
        if (intensity == 0f) return Vector3.Zero;
        if (saturation == 0f) return new(intensity);
        var sector = (hue % 360f + 360f) % 360f;
        var shifted = (sector % 120f) * MathF.PI / 180f;
        var primary = intensity * (1f + saturation * MathF.Cos(shifted) /
            MathF.Max(MathF.Cos(MathF.PI / 3f - shifted), float.Epsilon));
        var secondary = intensity * (1f - saturation);
        var other = 3f * intensity - primary - secondary;
        var color = sector < 120f
            ? new Vector3(primary, other, secondary)
            : sector < 240f
                ? new Vector3(secondary, primary, other)
                : new Vector3(other, secondary, primary);
        return Vector3.Clamp(color, Vector3.Zero, Vector3.One);
    }

    private static AmbientParticleRange ReadRange(
        JsonElement root, string name, AmbientParticleRange fallback)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{name} must be an object.");
        return new(RequiredFloat(value, "min"), RequiredFloat(value, "max"));
    }

    private static Vector3 ReadVector(JsonElement root, string name, bool nonNegative)
    {
        if (!root.TryGetProperty(name, out var value)) return Vector3.Zero;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3)
            throw new FormatException($"{name} must contain three numbers.");
        var numbers = value.EnumerateArray().Select(x =>
            x.ValueKind == JsonValueKind.Number && x.TryGetSingle(out var number)
                ? number : throw new FormatException($"{name} requires numbers.")).ToArray();
        if (numbers.Any(x => !float.IsFinite(x) || (nonNegative && x < 0f)))
            throw new FormatException($"{name} contains an invalid component.");
        return new(numbers[0], numbers[1], numbers[2]);
    }

    private static float OptionalFloat(JsonElement root, string name, float fallback)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetSingle(out var number) || !float.IsFinite(number))
            throw new FormatException($"{name} must be finite.");
        return number;
    }

    private static float RequiredFloat(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out _))
            throw new FormatException($"Missing number: {name}.");
        return OptionalFloat(root, name, 0f);
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new FormatException($"Missing string: {name}.");

    private static JsonElement RequiredObject(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value : throw new FormatException($"{name} must be an object.");
}

public sealed class AmbientParticleRegistry
{
    public AmbientParticleRegistry(IEnumerable<AmbientParticleDefinition> definitions)
    {
        var ordered = definitions.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        if (ordered.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Duplicate ambient particle id.", nameof(definitions));
        Definitions = Array.AsReadOnly(ordered);
    }

    public IReadOnlyList<AmbientParticleDefinition> Definitions { get; }

    public static AmbientParticleRegistry FromJson(IEnumerable<string> documents) =>
        new(documents.Select(AmbientParticleDefinition.Parse));

    public void ValidateReferences(
        DimensionRegistry dimensions, BiomeRegistry biomes, FluidRegistry fluids)
    {
        var dimensionIds = dimensions.Definitions().Select(d => d.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            var valid = definition.SourceKind switch
            {
                AmbientParticleSourceKind.Dimension =>
                    dimensionIds.Contains(definition.SourceId),
                AmbientParticleSourceKind.Biome =>
                    dimensions.Definitions().Any(d =>
                        d.SurfaceBiomes.Contains(definition.SourceId) ||
                        d.VolumeBiomes.Contains(definition.SourceId)),
                AmbientParticleSourceKind.FluidSurface =>
                    fluids.TryGetId(definition.SourceId, out _),
                _ => false,
            };
            if (!valid)
                throw new ArgumentException(
                    $"Ambient particle {definition.Id} references missing {definition.SourceId}.");
        }
    }
}
