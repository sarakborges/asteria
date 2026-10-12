namespace Asteria.Core.World;

/// <summary>Shared authored biome color channel for blocks and Objects.</summary>
public static class BlockTintParser
{
    public static BlockTint Parse(string? value) => value switch
    {
        null or "none" => BlockTint.None,
        "grass" => BlockTint.Grass,
        "leaf" => BlockTint.Leaf,
        "foliage" => BlockTint.Foliage,
        _ => throw new FormatException($"Unknown block/object tint: {value}"),
    };
}
