namespace Asteria.Core.Content;

/// <summary>
/// Material override requested by an authored creature definition. Texture and
/// unlit flags are independent: decorative body/accent materials can be unlit
/// without supplying a replacement texture.
/// </summary>
public readonly record struct CreatureMaterialAppearance(
    string? TexturePath,
    bool Unlit)
{
    public bool HasOverrides => TexturePath is not null || Unlit;

    public static CreatureMaterialAppearance For(
        CreatureDefinition creature, string materialName)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(materialName);
        creature.Textures.TryGetValue(materialName, out var texturePath);
        return new CreatureMaterialAppearance(
            texturePath,
            creature.UnlitMaterials.Contains(materialName, StringComparer.Ordinal));
    }
}
