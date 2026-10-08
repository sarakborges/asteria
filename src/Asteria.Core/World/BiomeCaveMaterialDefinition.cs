namespace Asteria.Core.World;

public enum CaveSurfaceFace : byte { Floor, Ceiling, Wall }

/// <summary>Validated, biome-authored replacement of exposed cave solids.</summary>
public sealed class BiomeCaveMaterialDefinition
{
    public BiomeCaveMaterialDefinition(
        string block,
        IEnumerable<string> replaceBlocks,
        IEnumerable<CaveSurfaceFace> faces,
        float chance,
        CaveSpikeClusterDefinition? cluster = null)
    {
        BlockDefinition.ValidateId(block);
        if (!float.IsFinite(chance) || chance <= 0f || chance > 1f)
            throw new ArgumentOutOfRangeException(nameof(chance));

        var replacements = replaceBlocks?.ToArray() ??
            throw new ArgumentNullException(nameof(replaceBlocks));
        if (replacements.Length == 0 ||
            replacements.Distinct(StringComparer.Ordinal).Count() != replacements.Length ||
            replacements.Contains(block, StringComparer.Ordinal))
            throw new ArgumentException(
                "Cave replacements must be nonempty, distinct and unique.",
                nameof(replaceBlocks));
        foreach (var id in replacements)
            BlockDefinition.ValidateId(id);

        var allowedFaces = faces?.ToArray() ??
            throw new ArgumentNullException(nameof(faces));
        if (allowedFaces.Length == 0 ||
            allowedFaces.Distinct().Count() != allowedFaces.Length ||
            allowedFaces.Any(face => !Enum.IsDefined(face)))
            throw new ArgumentException(
                "Cave material faces must be nonempty, unique and valid.",
                nameof(faces));

        Block = block;
        ReplaceBlocks = Array.AsReadOnly(replacements);
        Faces = Array.AsReadOnly(allowedFaces);
        Chance = chance;
        Cluster = cluster;
    }

    public string Block { get; }
    public IReadOnlyList<string> ReplaceBlocks { get; }
    public IReadOnlyList<CaveSurfaceFace> Faces { get; }
    public float Chance { get; }
    public CaveSpikeClusterDefinition? Cluster { get; }
}
