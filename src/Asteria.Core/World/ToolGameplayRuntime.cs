using Asteria.Core.Content;

namespace Asteria.Core.World;

public enum ToolUseHand : byte
{
    Left,
    Right,
}

/// <summary>
/// Resolves authored tool behaviors against the existing world mutation owner.
/// No visual-only fake behavior or generic strings from WebUI enter this API.
/// </summary>
public sealed class ToolGameplayRuntime
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly PackContentRegistry<ToolDefinition> _tools;

    public ToolGameplayRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelMutationRuntime mutations,
        PackContentRegistry<ToolDefinition> tools)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public bool CanMine(
        InventoryStack? held,
        BlockDefinition target,
        PlayerGameMode mode)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (mode == PlayerGameMode.Creative) return true;
        if (mode == PlayerGameMode.Spectator) return false;

        ToolDefinition? tool = null;
        if (held?.Kind == InventoryEntryKind.Tool)
        {
            if (!_tools.TryGet(held.Id, out tool) ||
                tool is null || tool.LeftBehavior != "asteria:mine")
                return false;
        }

        var required = target.Mining.RequiredTools;
        if (required.Count == 0) return true;

        return tool?.Mining is { } mining &&
            required.Contains(mining.Category, StringComparer.Ordinal);
    }

    public bool IsSpecialLeftAction(InventoryStack? held) =>
        GetBehavior(held, ToolUseHand.Left) is
            "asteria:log/hollow" or
            "asteria:artisans_kit/remove" or
            "asteria:brush/paint";

    public bool TryUse(
        InventoryStack? held,
        ToolUseHand hand,
        VoxelWorldHit hit)
    {
        var behavior = GetBehavior(held, hand);
        var targetKey = behavior switch
        {
            "asteria:log/hollow" => "hollow",
            "asteria:log/strip" => "stripped",
            _ => null,
        };
        if (targetKey is null) return false;
        if (!_world.TryGetCell(hit.Voxel, out var cell) ||
            cell.IsEmpty || cell.HasMicroblockGeometry)
            return false;

        var source = _blocks.GetDefinition(cell.Block);
        var key = source.Variant?.Key;
        if (key is null) return false;
        var variantKey = (targetKey, key) switch
        {
            ("hollow", "natural") => "hollow",
            ("hollow", "stripped") => "stripped_hollow",
            ("stripped", "natural") => "stripped",
            ("stripped", "hollow") => "stripped_hollow",
            _ => null,
        };
        if (variantKey is null ||
            !_blocks.TryGetVariant(cell.Block, variantKey, out var variant))
            return false;

        var target = _blocks.GetDefinition(variant);
        if (!target.Orientations.Contains(cell.Orientation))
            return false;

        var converted = new VoxelCell(
            variant, cell.TextureRotation, cell.Orientation, cell.Facing,
            cell.State);
        var snapshot = BlockStateSnapshot.Capture(
            _world, hit.Voxel, cell);
        return _mutations.SetBlockStateAt(
            hit.Voxel,
            new BlockStateSnapshot(converted, snapshot.MicroblockMask),
            out _);
    }

    private string? GetBehavior(InventoryStack? stack, ToolUseHand hand)
    {
        if (stack?.Kind != InventoryEntryKind.Tool ||
            !_tools.TryGet(stack.Id, out var tool) || tool is null)
            return null;
        return hand == ToolUseHand.Left
            ? tool.LeftBehavior
            : hand == ToolUseHand.Right ? tool.RightBehavior : null;
    }
}
