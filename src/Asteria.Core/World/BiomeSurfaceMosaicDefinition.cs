namespace Asteria.Core.World;

/// <summary>
/// A single surface-voxel choice: exactly one block or one source fluid.
/// Noise selects among these alternatives; they are not independent patches.
/// </summary>
public sealed class BiomeSurfaceMosaicEntryDefinition
{
    public BiomeSurfaceMosaicEntryDefinition(
        string? block, string? fluid, float weight)
    {
        if ((block is null) == (fluid is null))
            throw new ArgumentException(
                "Surface mosaic entry requires exactly one of block or fluid.");
        if (!float.IsFinite(weight) || weight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(weight));
        BlockDefinition.ValidateId(block ?? fluid!);
        Block = block;
        Fluid = fluid;
        Weight = weight;
    }

    public string? Block { get; }
    public string? Fluid { get; }
    public float Weight { get; }
}

/// <summary>
/// Shared continuous 2D noise field selecting one surface voxel. A fluid
/// occupies precisely the voxel that a dry surface block would occupy.
/// </summary>
public sealed class BiomeSurfaceMosaicDefinition
{
    public BiomeSurfaceMosaicDefinition(
        uint scale,
        uint detailScale,
        float detailStrength,
        IEnumerable<BiomeSurfaceMosaicEntryDefinition> entries)
    {
        if (scale is < 2 or > 2048)
            throw new ArgumentOutOfRangeException(nameof(scale));
        if (detailScale is < 2 or > 2048)
            throw new ArgumentOutOfRangeException(nameof(detailScale));
        if (!float.IsFinite(detailStrength) ||
            detailStrength is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(detailStrength));

        var all = entries?.ToArray() ??
            throw new ArgumentNullException(nameof(entries));
        if (all.Length is < 2 or > 16 ||
            all.GroupBy(entry => entry.Block ?? entry.Fluid,
                    StringComparer.Ordinal).Any(group => group.Count() > 1) ||
            all.All(entry => entry.Fluid is not null))
            throw new ArgumentException(
                "Surface mosaic requires 2..16 unique choices and at least one solid block.",
                nameof(entries));
        var sum = all.Sum(entry => (double)entry.Weight);
        if (!double.IsFinite(sum) || sum <= 0d)
            throw new ArgumentOutOfRangeException(nameof(entries));

        Scale = scale;
        DetailScale = detailScale;
        DetailStrength = detailStrength;
        Entries = Array.AsReadOnly(all);
    }

    public uint Scale { get; }
    public uint DetailScale { get; }
    public float DetailStrength { get; }
    public IReadOnlyList<BiomeSurfaceMosaicEntryDefinition> Entries { get; }
}
