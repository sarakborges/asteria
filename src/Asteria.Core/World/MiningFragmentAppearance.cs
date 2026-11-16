namespace Asteria.Core.World;

/// <summary>
/// Immutable pack material sampled at the validated mining target *before*
/// its block/layer is removed. Presentation never needs to inspect a broken voxel.
/// </summary>
public sealed record MiningFragmentAppearance(
    IReadOnlyList<BlockTextureLayer> Textures,
    BlockTint Tint,
    BlockPreviewColor TintFallback,
    string? DyeId = null)
{
    public static MiningFragmentAppearance? Resolve(
        VoxelWorld world, BlockRegistry blocks, AttachedLayerRegistry? layers,
        VoxelWorldHit hit)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!world.TryGetCell(hit.Voxel, out var cell) || cell.IsEmpty)
            return null;

        var face = VoxelHitFace.TryResolve(hit, out var worldFace)
            ? worldFace : BlockFace.Top;
        if (layers is not null &&
            world.GetBlockSurfaceStateOrEmpty(hit.Voxel).TryGetTop(face, out var top) &&
            layers.TryGet(top.LayerId, out var layer) && layer is not null)
        {
            return new MiningFragmentAppearance(
                [new BlockTextureLayer(layer.Texture,
                    dyable: layer.Tint != BlockTint.None)],
                layer.Tint, new BlockPreviewColor(255, 255, 255));
        }

        var block = blocks.GetDefinition(cell.Block);
        var sourceFace = BlockFaceTransform.SourceFaceForWorldFace(
            face, cell, block);
        var textures = block.Textures.ResolveForFace(sourceFace);
        if (textures.Count == 0 && block.Visual.Texture is { } visual)
            textures = [visual];
        var surface = world.GetBlockSurfaceStateOrEmpty(hit.Voxel);
        return new MiningFragmentAppearance(
            textures.ToArray(), block.Tint, block.PreviewColor,
            block.SupportsDye ? surface.DyeId : null);
    }
}
