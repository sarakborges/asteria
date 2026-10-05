namespace Asteria.Core.World;

public sealed class BlockDefinition
{
    private readonly HashSet<string> _tags;

    public BlockDefinition(
        string id,
        string category = "uncategorized",
        IEnumerable<string>? tags = null,
        BlockTint tint = BlockTint.None,
        BlockTextureSet? textures = null,
        BlockTextureRotations rotateTexture = default,
        BlockMiningDefinition? mining = null,
        bool isCollidable = true,
        BlockRenderMode renderMode = BlockRenderMode.Opaque,
        bool castsShadow = true,
        byte lightDampening = 15,
        BlockLightEmission lightEmission = default,
        BlockPreviewColor? previewColor = null)
    {
        ValidateId(id);

        if (string.IsNullOrWhiteSpace(category) || category != category.Trim())
        {
            throw new ArgumentException("Block category must be non-empty and trimmed.", nameof(category));
        }

        if (lightDampening > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(lightDampening), "Light dampening must be within 0..15.");
        }

        Id = id;
        Category = category;
        _tags = ValidateTags(tags);
        Tint = tint;
        Textures = textures ?? new BlockTextureSet();
        RotateTexture = rotateTexture;
        Mining = mining ?? new BlockMiningDefinition();
        IsCollidable = isCollidable;
        RenderMode = renderMode;
        CastsShadow = castsShadow;
        LightDampening = lightDampening;
        LightEmission = lightEmission;
        PreviewColor = previewColor ?? BlockPreviewColor.Missing;
    }

    public string Id { get; }
    public string Category { get; }
    public IReadOnlySet<string> Tags => _tags;
    public BlockTint Tint { get; }
    public BlockTextureSet Textures { get; }
    public BlockTextureRotations RotateTexture { get; }
    public BlockMiningDefinition Mining { get; }
    public bool IsCollidable { get; }
    public BlockRenderMode RenderMode { get; }
    public bool IsOpaque => RenderMode == BlockRenderMode.Opaque;
    public bool CastsShadow { get; }
    public byte LightDampening { get; }
    public BlockLightEmission LightEmission { get; }
    public BlockPreviewColor PreviewColor { get; }

    public bool HasTag(string tag) => _tags.Contains(tag);

    internal static BlockDefinition Air { get; } = new(
        "asteria:air",
        category: "runtime",
        isCollidable: false,
        renderMode: BlockRenderMode.Translucent,
        castsShadow: false,
        lightDampening: 0,
        previewColor: new BlockPreviewColor(0, 0, 0));

    private static HashSet<string> ValidateTags(IEnumerable<string>? tags)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (tags is null)
        {
            return result;
        }

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag) || tag != tag.Trim())
            {
                throw new ArgumentException("Block tags must be non-empty and trimmed.", nameof(tags));
            }

            if (!result.Add(tag))
            {
                throw new ArgumentException($"Duplicate block tag: {tag}", nameof(tags));
            }
        }

        return result;
    }

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
