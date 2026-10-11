using Asteria.Core.World;

namespace Asteria.Core.Content;

public enum HeldVisualKind : byte { Sprite, Cube, GrippedSprite }

/// <summary>
/// Immutable selected-stack appearance. No input, movement or inventory
/// state is duplicated in the model renderer.
/// </summary>
public sealed record HeldVisual(
    HeldVisualKind Kind, string FrontTexture,
    string? TopTexture = null, string? BottomTexture = null,
    string? BackTexture = null, string? LeftTexture = null, string? RightTexture = null,
    string? TintTexture = null);

/// <summary>
/// Maps existing authored catalog identities to their portable display
/// artwork. Non-cubic geometry uses a 2D representation rather than
/// falsely rendering a full cube.
/// </summary>
public sealed class HeldVisualResolver
{
    private readonly BlockRegistry _blocks;
    private readonly PackContentRegistry<ItemDefinition> _items;
    private readonly PackContentRegistry<ToolDefinition> _tools;
    private readonly AttachedLayerRegistry _layers;

    public HeldVisualResolver(BlockRegistry blocks,
        PackContentRegistry<ItemDefinition> items,
        PackContentRegistry<ToolDefinition> tools,
        AttachedLayerRegistry layers)
    {
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
    }

    public HeldVisual? Resolve(InventoryStack? selected)
    {
        if (selected is null) return null;
        switch (selected.Kind)
        {
            case InventoryEntryKind.Item:
            {
                if (!_items.TryGet(selected.Id, out var item)) return null;
                var variant = item!.IconVariants.FirstOrDefault(x =>
                    selected.Entry.Metadata.TryGetValue(
                        x.MetadataKey, out var value) && value == x.MetadataValue);
                return new HeldVisual(HeldVisualKind.Sprite, variant?.Icon ?? item.Icon);
            }
            case InventoryEntryKind.Tool:
            {
                if (!_tools.TryGet(selected.Id, out var tool)) return null;
                var variant = (tool!.IconVariants ?? []).FirstOrDefault(x =>
                    selected.Entry.Metadata.TryGetValue(
                        x.MetadataKey, out var value) && value == x.MetadataValue);
                return new HeldVisual(
                    tool.HeldPresentation == ToolHeldPresentation.GrippedSprite
                        ? HeldVisualKind.GrippedSprite : HeldVisualKind.Sprite,
                    variant?.Icon ?? tool.Icon,
                    TintTexture: tool.HeldPresentation == ToolHeldPresentation.GrippedSprite
                        ? tool.TintIcon : null);
            }
            case InventoryEntryKind.Layer:
            {
                var layer = _layers.Definitions.FirstOrDefault(x => x.Id == selected.Id);
                return layer is null ? null :
                    new HeldVisual(HeldVisualKind.Sprite, layer.Texture);
            }
            case InventoryEntryKind.Block:
            {
                if (selected.Block is null ||
                    !_blocks.TryGetId(selected.Id, out var blockId))
                    return null;
                var definition = _blocks.GetDefinition(blockId);
                var front = definition.Textures.ResolveForFace(BlockFace.Front)
                    .FirstOrDefault().Texture ?? definition.Visual.Texture?.Texture;
                if (front is null) return null;
                if (definition.Shape.Kind != BlockShapeKind.Cube ||
                    definition.Visual.Kind != BlockVisualKind.Geometry ||
                    selected.Block.HasMicroblockGeometry)
                    return new HeldVisual(HeldVisualKind.Sprite, front);

                var top = definition.Textures.ResolveForFace(BlockFace.Top)
                    .FirstOrDefault().Texture ?? front;
                var bottom = definition.Textures.ResolveForFace(BlockFace.Bottom)
                    .FirstOrDefault().Texture ?? front;
                var back = definition.Textures.ResolveForFace(BlockFace.Back)
                    .FirstOrDefault().Texture ?? front;
                var left = definition.Textures.ResolveForFace(BlockFace.Left)
                    .FirstOrDefault().Texture ?? front;
                var right = definition.Textures.ResolveForFace(BlockFace.Right)
                    .FirstOrDefault().Texture ?? front;
                return new HeldVisual(HeldVisualKind.Cube,
                    front, top, bottom, back, left, right);
            }
            default:
                return null;
        }
    }
}
