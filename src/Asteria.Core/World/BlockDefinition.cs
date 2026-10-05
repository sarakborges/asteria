namespace Asteria.Core.World;

public sealed class BlockDefinition
{
    public BlockDefinition(
        string id,
        bool isCollidable = true,
        BlockRenderMode renderMode = BlockRenderMode.Opaque,
        bool castsShadow = true,
        byte lightDampening = 15,
        BlockLightEmission lightEmission = default)
    {
        ValidateId(id);

        if (lightDampening > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(lightDampening), "Light dampening must be within 0..15.");
        }

        Id = id;
        IsCollidable = isCollidable;
        RenderMode = renderMode;
        CastsShadow = castsShadow;
        LightDampening = lightDampening;
        LightEmission = lightEmission;
    }

    public string Id { get; }

    public bool IsCollidable { get; }

    public BlockRenderMode RenderMode { get; }

    public bool IsOpaque => RenderMode == BlockRenderMode.Opaque;

    public bool CastsShadow { get; }

    public byte LightDampening { get; }

    public BlockLightEmission LightEmission { get; }

    internal static BlockDefinition Air { get; } = new(
        "asteria:air",
        isCollidable: false,
        renderMode: BlockRenderMode.Translucent,
        castsShadow: false,
        lightDampening: 0);

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
        {
            throw new ArgumentException("Block id must be non-empty and trimmed.", nameof(id));
        }

        var separator = id.IndexOf(':');
        if (separator <= 0 || separator == id.Length - 1 || id.IndexOf(':', separator + 1) >= 0)
        {
            throw new ArgumentException("Block id must use the namespaced form namespace:name.", nameof(id));
        }
    }
}
