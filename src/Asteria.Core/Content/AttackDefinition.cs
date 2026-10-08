using System.Text.Json;

namespace Asteria.Core.Content;

public sealed record AttackEffectDefinition(string Effect, float Chance, float Strength);

/// <summary>Data-authored player attack, independent of input and presentation.</summary>
public sealed record AttackDefinition(
    string Id,
    float Damage,
    IReadOnlyList<AttackEffectDefinition> Effects)
{
    public static AttackDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = PackContentFields.Id(root);
        var damage = PackContentFields.NonNegativeFloat(root, "damage");
        var effects = new List<AttackEffectDefinition>();
        if (root.TryGetProperty("effects", out var entries))
        {
            if (entries.ValueKind != JsonValueKind.Array)
                throw new FormatException($"Attack {id} effects must be an array.");

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    throw new FormatException($"Invalid effect on attack {id}.");

                var effect = PackContentFields.RequiredString(entry, "effect");
                if (effect != "knockback")
                    throw new FormatException($"Unknown attack effect: {effect}.");

                var chance = entry.TryGetProperty("chance", out _)
                    ? PackContentFields.NonNegativeFloat(entry, "chance") : 1f;
                if (chance > 1f)
                    throw new FormatException($"Attack effect chance exceeds 1: {id}.");

                var strength = entry.TryGetProperty("strength", out _)
                    ? PackContentFields.NonNegativeFloat(entry, "strength") : 0f;
                effects.Add(new AttackEffectDefinition(effect, chance, strength));
            }
        }

        return new AttackDefinition(id, damage, effects.AsReadOnly());
    }
}
