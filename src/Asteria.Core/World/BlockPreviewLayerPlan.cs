namespace Asteria.Core.World;

/// <summary>
/// Immutable renderer-facing view of a real attached layer stack. The layer
/// registry and voxel surface remain the sole owners of authored identity,
/// insertion order, face and UV rotation.
/// </summary>
public readonly record struct PreviewAttachedLayer(
    string Texture, TextureRotation Rotation, BlockTint Tint,
    float? AlphaCutoff);

public static class BlockPreviewLayerPlan
{
    public static IReadOnlyList<PreviewAttachedLayer> ForWorldFace(
        BlockSurfaceState surface, AttachedLayerRegistry registry,
        BlockFace worldFace)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(registry);
        if (!Enum.IsDefined(worldFace))
            throw new ArgumentOutOfRangeException(nameof(worldFace));

        var result = new List<PreviewAttachedLayer>();
        foreach (var attached in surface.Layers)
        {
            if (attached.Face != worldFace) continue;
            var layer = registry.Get(attached.LayerId);
            if (!layer.Supports(worldFace))
                throw new InvalidOperationException(
                    $"Layer {layer.Id} is not permitted on {worldFace}.");
            result.Add(new PreviewAttachedLayer(
                layer.Texture, attached.Rotation, layer.Tint,
                layer.AlphaCutoff));
        }
        return result;
    }
}
