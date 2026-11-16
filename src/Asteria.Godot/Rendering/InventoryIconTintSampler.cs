using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Preview-only sampled biome palette, recalculated on horizontal voxel
/// changes and republished only when the quantized color changes.
/// </summary>
internal sealed class InventoryIconTintSampler
{
    private static readonly BlockPreviewColor White = new(255, 255, 255);
    private (int X, int Z)? _position;
    private BiomeTintSampleGrid? _grid;

    public InventoryIconTintPalette? Palette { get; private set; }

    public bool Sync(BiomeTintField tints, Vector3 position)
    {
        ArgumentNullException.ThrowIfNull(tints);
        var x = Mathf.FloorToInt(position.X);
        var z = Mathf.FloorToInt(position.Z);
        if (_position == (x, z)) return false;
        var chunk = VoxelCoordinates.FromWorld(x, 0, z).Chunk;
        var grid = tints.SampleChunkGrid(chunk.X, chunk.Z);
        BlockPreviewColor Channel(BlockTint tint) =>
            BlockPreviewColor.FromNormalized(grid.Resolve(tint, White, x, z));
        var next = new InventoryIconTintPalette(
            Channel(BlockTint.Grass), Channel(BlockTint.Leaf),
            Channel(BlockTint.Foliage));
        var changed = Palette != next;
        _position = (x, z);
        _grid = grid;
        Palette = next;
        return changed;
    }

    public BlockPreviewColor? Resolve(BlockTint tint, BlockPreviewColor fallback)
    {
        if (tint == BlockTint.None || _position is not { } p || _grid is null)
            return null;
        return BlockPreviewColor.FromNormalized(
            _grid.Resolve(tint, fallback, p.X, p.Z));
    }

    public void Reset()
    {
        _position = null;
        _grid = null;
        Palette = null;
    }
}

internal readonly record struct InventoryIconTintPalette(
    BlockPreviewColor Grass, BlockPreviewColor Leaf, BlockPreviewColor Foliage);
