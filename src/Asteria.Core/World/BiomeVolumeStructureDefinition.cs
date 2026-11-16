namespace Asteria.Core.World;

/// <summary>
/// One sparse, bounded 3D structure family owned by a volume biome.
/// Y bounds describe the supporting solid, not the emitted voxels.
/// </summary>
public sealed class BiomeVolumeStructureDefinition
{
    public BiomeVolumeStructureDefinition(
        string structure,
        int spacing,
        float chance,
        int minY,
        int maxY,
        DecorationSupportSurface supportSurface =
            DecorationSupportSurface.Floor)
    {
        if (string.IsNullOrWhiteSpace(structure) ||
            !structure.Contains(':'))
            throw new ArgumentException(
                "Volume structure reference must be namespaced.", nameof(structure));
        if (spacing is < 16 or > 256)
            throw new ArgumentOutOfRangeException(nameof(spacing));
        if (!float.IsFinite(chance) || chance is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(chance));
        if (minY < 1 || maxY <= minY || maxY - minY > 512)
            throw new ArgumentOutOfRangeException(nameof(maxY));
        if (!Enum.IsDefined(supportSurface))
            throw new ArgumentOutOfRangeException(nameof(supportSurface));

        Structure = structure;
        Spacing = spacing;
        Chance = chance;
        MinY = minY;
        MaxY = maxY;
        SupportSurface = supportSurface;
    }

    public string Structure { get; }
    public int Spacing { get; }
    public float Chance { get; }
    public int MinY { get; }
    public int MaxY { get; }
    public DecorationSupportSurface SupportSurface { get; }
}
