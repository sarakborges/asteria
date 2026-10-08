namespace Asteria.Core.World;

/// <summary>
/// Tick-driven survival mining. Progress belongs to the active Sphere and
/// never survives loss of input, target, voxel state or selected item.
/// The interaction runtime remains the only owner of the final mutation.
/// </summary>
public sealed class BlockMiningRuntime
{
    // MineClone world-systems-rebuild's authored baseline.
    public const double DefaultBreakWorkTicks = 200.0;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly ToolGameplayRuntime _tools;
    private readonly BlockInteractionRuntime _interactions;
    private MiningTarget? _target;
    private double _work;

    public BlockMiningRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        ToolGameplayRuntime tools,
        BlockInteractionRuntime interactions)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _interactions = interactions ??
            throw new ArgumentNullException(nameof(interactions));
    }

    public float? Progress { get; private set; }
    public WorldVoxelCoord? Target => _target?.Position;

    public void Cancel()
    {
        _target = null;
        _work = 0;
        Progress = null;
    }

    public bool Advance(
        VoxelWorldHit? hit,
        InventoryStack? held,
        int hotbarSlot,
        uint elapsedTicks,
        PlayerGameMode mode)
    {
        if (mode != PlayerGameMode.Survival || hit is null ||
            hotbarSlot is < 0 or >= PlayerInventory.HotbarSlots)
        {
            Cancel();
            return false;
        }

        var position = hit.Value.Voxel;
        if (!_world.TryGetCell(position, out var cell) || cell.IsEmpty)
        {
            Cancel();
            return false;
        }

        var block = _blocks.GetDefinition(cell.Block);
        if (block.Mining.Unbreakable ||
            _tools.EffectiveMiningSpeed(held, block) is not { } speed)
        {
            Cancel();
            return false;
        }

        var target = new MiningTarget(
            position, cell, held?.Entry, hotbarSlot);
        if (_target != target)
        {
            _target = target;
            _work = 0;
        }

        var requiredWork = DefaultBreakWorkTicks * block.Mining.Hardness;
        _work = Math.Min(
            requiredWork,
            _work + (double)elapsedTicks * speed);
        if (requiredWork > 0 && _work < requiredWork)
        {
            Progress = (float)(_work / requiredWork);
            return false;
        }

        var completed = _interactions.Break(
            hit.Value,
            BlockBreakLootPolicy.DropSelf).Accepted;
        Cancel();
        return completed;
    }

    private readonly record struct MiningTarget(
        WorldVoxelCoord Position,
        VoxelCell Cell,
        InventoryEntry? Held,
        int HotbarSlot);
}
