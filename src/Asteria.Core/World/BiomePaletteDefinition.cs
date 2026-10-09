namespace Asteria.Core.World;

public enum BiomePaletteFace : byte { Floor, Walls, Ceiling }

/// <summary>
/// Shared block palette for surface and volume biomes. Missing directional
/// profiles inherit Default; depths count inward from exposed solid faces.
/// </summary>
public sealed class BiomePaletteDefinition
{
    public BiomePaletteDefinition(
        IEnumerable<BiomeSurfaceLayerDefinition> @default,
        IEnumerable<BiomeSurfaceLayerDefinition>? floor = null,
        IEnumerable<BiomeSurfaceLayerDefinition>? walls = null,
        IEnumerable<BiomeSurfaceLayerDefinition>? ceiling = null)
    {
        Default = Validate(@default, nameof(@default));
        Floor = floor is null ? null : Validate(floor, nameof(floor));
        Walls = walls is null ? null : Validate(walls, nameof(walls));
        Ceiling = ceiling is null ? null : Validate(ceiling, nameof(ceiling));
    }

    public IReadOnlyList<BiomeSurfaceLayerDefinition> Default { get; }
    public IReadOnlyList<BiomeSurfaceLayerDefinition>? Floor { get; }
    public IReadOnlyList<BiomeSurfaceLayerDefinition>? Walls { get; }
    public IReadOnlyList<BiomeSurfaceLayerDefinition>? Ceiling { get; }

    public IReadOnlyList<BiomeSurfaceLayerDefinition> For(BiomePaletteFace face) =>
        face switch
        {
            BiomePaletteFace.Floor => Floor ?? Default,
            BiomePaletteFace.Walls => Walls ?? Default,
            BiomePaletteFace.Ceiling => Ceiling ?? Default,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };

    public bool HasOverride(BiomePaletteFace face) => face switch
    {
        BiomePaletteFace.Floor => Floor is not null,
        BiomePaletteFace.Walls => Walls is not null,
        BiomePaletteFace.Ceiling => Ceiling is not null,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    public IEnumerable<IReadOnlyList<BiomeSurfaceLayerDefinition>> AuthoredProfiles()
    {
        yield return Default;
        if (Floor is not null) yield return Floor;
        if (Walls is not null) yield return Walls;
        if (Ceiling is not null) yield return Ceiling;
    }

    private static IReadOnlyList<BiomeSurfaceLayerDefinition> Validate(
        IEnumerable<BiomeSurfaceLayerDefinition> layers, string argument)
    {
        var entries = layers?.ToArray() ??
            throw new ArgumentNullException(argument);
        BiomeDefinition.ValidateSurfaceLayers(entries);
        return Array.AsReadOnly(entries);
    }
}
