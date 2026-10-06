namespace Asteria.Core.World;

public sealed class BlockDefinition
{
    private static readonly BlockOrientation[] DefaultOrientations = [BlockOrientation.Y];
    private readonly HashSet<string> _tags;

    public BlockDefinition(
        string id,
        string category = "uncategorized",
        IEnumerable<string>? tags = null,
        BlockTint tint = BlockTint.None,
        BlockTextureSet? textures = null,
        BlockVisualDefinition? visual = null,
        BlockTextureRotations rotateTexture = default,
        BlockMiningDefinition? mining = null,
        BlockShapeDefinition? shape = null,
        IEnumerable<BlockOrientation>? orientations = null,
        IEnumerable<BlockFace>? placementFaces = null,
        BlockVariantDefinition? variant = null,
        bool isCollidable = true,
        BlockRenderMode renderMode = BlockRenderMode.Opaque,
        bool castsShadow = true,
        byte lightDampening = 15,
        BlockLightEmission lightEmission = default,
        BlockPreviewColor? previewColor = null,
        bool dropsSelf = true)
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

        var allowedOrientations = (orientations ?? DefaultOrientations).ToArray();
        if (allowedOrientations.Length == 0 ||
            allowedOrientations.Distinct().Count() != allowedOrientations.Length)
        {
            throw new ArgumentException("Block orientations must be non-empty and unique.", nameof(orientations));
        }

        Id = id;
        Category = category;
        _tags = ValidateTags(tags);
        Tint = tint;
        Textures = textures ?? new BlockTextureSet();
        Visual =
            visual ??
            BlockVisualDefinition.Geometry;
        RotateTexture = rotateTexture;
        Mining = mining ?? new BlockMiningDefinition();
        Shape = shape ?? BlockShapeDefinition.Cube;
        Orientations = allowedOrientations;
        PlacementFaces =
            FreezePlacementFaces(
                placementFaces);
        Variant = variant;
        IsCollidable = isCollidable;
        RenderMode = renderMode;
        CastsShadow = castsShadow;
        LightDampening = lightDampening;
        LightEmission = lightEmission;
        PreviewColor = previewColor ?? BlockPreviewColor.Missing;
        DropsSelf = dropsSelf;
    }

    public string Id { get; }
    public string Category { get; }
    public IReadOnlySet<string> Tags => _tags;
    public BlockTint Tint { get; }
    public BlockTextureSet Textures { get; }
    public BlockVisualDefinition Visual { get; }
    public BlockTextureRotations RotateTexture { get; }
    public BlockMiningDefinition Mining { get; }
    public BlockShapeDefinition Shape { get; }
    public IReadOnlyList<BlockOrientation> Orientations { get; }
    public IReadOnlySet<BlockFace> PlacementFaces { get; }
    public BlockVariantDefinition? Variant { get; }
    public bool IsCollidable { get; }
    public BlockRenderMode RenderMode { get; }
    public bool IsOpaque => RenderMode == BlockRenderMode.Opaque;
    public bool CastsShadow { get; }
    public byte LightDampening { get; }
    public BlockLightEmission LightEmission { get; }
    public BlockPreviewColor PreviewColor { get; }
    public bool DropsSelf { get; }
    public bool IsRotatable => Orientations.Count > 1;
    public bool SupportsMicroblocks => HasTag("fragmentable");
    public bool UsesHorizontalFacing => HasTag("horizontal_facing");

    public bool HasTag(string tag) => _tags.Contains(tag);

    public bool SupportsPlacementFace(
        BlockFace face) =>
        PlacementFaces.Contains(face);

    internal static BlockDefinition Air { get; } = new(
        "asteria:air",
        category: "runtime",
        isCollidable: false,
        renderMode: BlockRenderMode.Translucent,
        castsShadow: false,
        lightDampening: 0,
        previewColor: new BlockPreviewColor(0, 0, 0),
        dropsSelf: false);

    private static IReadOnlySet<BlockFace> FreezePlacementFaces(
        IEnumerable<BlockFace>? placementFaces)
    {
        var values =
            (placementFaces ??
             Enum.GetValues<BlockFace>())
                .ToArray();

        if (values.Length == 0 ||
            values.Distinct().Count() !=
                values.Length)
        {
            throw new ArgumentException(
                "Block placement faces must be non-empty and unique.",
                nameof(placementFaces));
        }

        return values.ToHashSet();
    }

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

    internal static void ValidateId(string id)
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
