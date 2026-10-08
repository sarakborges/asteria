using System.Text.Json;

namespace Asteria.Core.Content;

public readonly record struct ContentVector3(float X, float Y, float Z);

public sealed record CreatureCollider(ContentVector3 Size, ContentVector3 CenterOffset);

public sealed record CreatureParticleEffect(
    int Count,
    IReadOnlyList<float> Color,
    IReadOnlyDictionary<string, float> Properties);

/// <summary>
/// Authored creature capabilities. Gameplay simulation and Godot presentation
/// consume these immutable definitions independently.
/// </summary>
public sealed record CreatureDefinition(
    string Id,
    string Model,
    int Health,
    int MaxPerType,
    CreatureCollider Collider,
    CreatureCollider? TargetCollider,
    float JumpSpeed,
    float MoveSpeed,
    float JumpInterval,
    float FallGravityScale,
    float AnticipationSeconds,
    float LandingSeconds,
    IReadOnlyDictionary<string, string> Animations,
    IReadOnlyDictionary<string, string> Textures,
    IReadOnlyList<string> UnlitMaterials,
    IReadOnlyDictionary<string, CreatureParticleEffect> ParticleEffects)
{
    public static CreatureDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var unlit = root.TryGetProperty("unlitMaterials", out var materials)
            ? ParseUnlitMaterials(materials)
            : Array.Empty<string>();

        return new CreatureDefinition(
            PackContentFields.Id(root),
            PackContentFields.ResourcePath(PackContentFields.RequiredString(root, "model"), "model"),
            PackContentFields.PositiveInt(root, "health"),
            PackContentFields.PositiveInt(root, "maxPerType"),
            ParseCollider(PackContentFields.RequiredObject(root, "collider")),
            root.TryGetProperty("targetCollider", out var target) ?
                ParseCollider(target) : null,
            PackContentFields.PositiveFloat(root, "jumpSpeed"),
            PackContentFields.PositiveFloat(root, "moveSpeed"),
            PackContentFields.PositiveFloat(root, "jumpInterval"),
            PackContentFields.OptionalPositiveFloat(root, "fallGravityScale", 1f),
            PackContentFields.NonNegativeFloat(root, "anticipationSeconds"),
            PackContentFields.NonNegativeFloat(root, "landingSeconds"),
            PackContentFields.Strings(root, "animations"),
            ParseTextures(root),
            unlit,
            ParseParticleEffects(root));
    }

    private static CreatureCollider ParseCollider(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Creature collider must be an object");
        }

        return new CreatureCollider(
            ParseVector(element, "size", positive: true),
            ParseVector(element, "centerOffset", positive: false));
    }

    private static ContentVector3 ParseVector(JsonElement source, string field, bool positive)
    {
        if (!source.TryGetProperty(field, out var vector) ||
            vector.ValueKind != JsonValueKind.Array ||
            vector.GetArrayLength() != 3)
        {
            throw new FormatException($"{field} must contain exactly three coordinates");
        }

        var values = vector.EnumerateArray().Select(element => {
            if (element.ValueKind != JsonValueKind.Number ||
                !element.TryGetSingle(out var value) ||
                !float.IsFinite(value) ||
                (positive && value <= 0f))
            {
                throw new FormatException($"{field} contains an invalid coordinate");
            }

            return value;
        }).ToArray();

        return new ContentVector3(values[0], values[1], values[2]);
    }

    private static IReadOnlyList<string> ParseUnlitMaterials(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("unlitMaterials must be an array");
        }

        var result = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            var name = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                throw new FormatException("unlitMaterials has an invalid or duplicate name");
            }

            result.Add(name);
        }

        return result.AsReadOnly();
    }

    private static IReadOnlyDictionary<string, string> ParseTextures(JsonElement root)
    {
        var source = PackContentFields.Strings(root, "textures");
        var validated = source.ToDictionary(
            pair => pair.Key,
            pair => PackContentFields.ResourcePath(pair.Value, "textures." + pair.Key),
            StringComparer.Ordinal);
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(validated);
    }

    private static IReadOnlyDictionary<string, CreatureParticleEffect> ParseParticleEffects(JsonElement root)
    {
        if (!root.TryGetProperty("particleEffects", out var effects))
        {
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, CreatureParticleEffect>(
                new Dictionary<string, CreatureParticleEffect>(StringComparer.Ordinal));
        }

        if (effects.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("particleEffects must be an object");
        }

        var result = new Dictionary<string, CreatureParticleEffect>(StringComparer.Ordinal);
        foreach (var property in effects.EnumerateObject())
        {
            var source = property.Value;
            if (source.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"Invalid particle effect: {property.Name}");
            }

            var count = PackContentFields.PositiveInt(source, "count");
            if (!source.TryGetProperty("color", out var color) ||
                color.ValueKind != JsonValueKind.Array ||
                color.GetArrayLength() != 4)
            {
                throw new FormatException($"Particle {property.Name} color requires four channels");
            }

            var channels = new List<float>();
            foreach (var element in color.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Number ||
                    !element.TryGetSingle(out var channel) ||
                    !float.IsFinite(channel))
                {
                    throw new FormatException($"Particle {property.Name} has invalid color");
                }

                channels.Add(channel);
            }

            var scalar = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var parameter in source.EnumerateObject())
            {
                if (parameter.Name is "count" or "color") continue;
                if (parameter.Value.ValueKind != JsonValueKind.Number ||
                    !parameter.Value.TryGetSingle(out var value) ||
                    !float.IsFinite(value))
                {
                    throw new FormatException($"Particle {property.Name}.{parameter.Name} must be finite");
                }

                scalar.Add(parameter.Name, value);
            }

            result.Add(property.Name, new CreatureParticleEffect(
                count,
                channels.AsReadOnly(),
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, float>(scalar)));
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, CreatureParticleEffect>(result);
    }
}
