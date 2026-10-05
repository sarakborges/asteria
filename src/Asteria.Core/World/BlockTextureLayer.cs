namespace Asteria.Core.World;

public readonly record struct BlockTextureLayer
{
    public BlockTextureLayer(string texture, bool dyable = false)
    {
        if (string.IsNullOrWhiteSpace(texture) || texture != texture.Trim())
        {
            throw new ArgumentException("Block texture path must be non-empty and trimmed.", nameof(texture));
        }

        Texture = texture;
        Dyable = dyable;
    }

    public string Texture { get; }

    public bool Dyable { get; }
}
