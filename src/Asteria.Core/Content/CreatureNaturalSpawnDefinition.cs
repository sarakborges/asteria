using System.Text.Json;

namespace Asteria.Core.Content;

/// <summary>Opt-in surface spawn policy; missing policy means no natural spawning.</summary>
public sealed record CreatureNaturalSpawnDefinition(
    IReadOnlyList<string> SurfaceBiomes,
    int Weight)
{
    public static CreatureNaturalSpawnDefinition? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("naturalSpawn", out var value))
            return null;

        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("surfaceBiomes", out var biomes) ||
            biomes.ValueKind != JsonValueKind.Array ||
            biomes.GetArrayLength() is < 1 or > 16)
            throw new FormatException("naturalSpawn.surfaceBiomes requires 1–16 biome IDs.");

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in biomes.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
                throw new FormatException("Natural spawn biome must be a string.");
            var id = PackContentFields.Namespaced(
                entry.GetString() ?? "", "naturalSpawn.surfaceBiomes");
            if (!seen.Add(id))
                throw new FormatException($"Duplicate natural spawn biome {id}.");
            ids.Add(id);
        }

        var weight = value.TryGetProperty("weight", out _)
            ? PackContentFields.PositiveInt(value, "weight") : 1;
        if (weight > 100)
            throw new FormatException("Natural spawn weight cannot exceed 100.");

        return new CreatureNaturalSpawnDefinition(ids.AsReadOnly(), weight);
    }
}
