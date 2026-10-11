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
    public const uint SwingIntervalTicks = 8;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly ToolGameplayRuntime _tools;
    private readonly BlockInteractionRuntime _interactions;
    private readonly AttachedLayerRegistry? _layers;
    private MiningTarget? _target;
    private double _work;
    private uint _swingTicks;

    /// <summary>Monotonic visual signal emitted only for valid mining work.
    /// Consumers observe changes; they never drive the mining state.</summary>
    public ulong FeedbackRevision { get; private set; }
    public MiningFeedbackEvent? LastFeedback { get; private set; }

    public BlockMiningRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        ToolGameplayRuntime tools,
        BlockInteractionRuntime interactions,
        AttachedLayerRegistry? layers = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _interactions = interactions ??
            throw new ArgumentNullException(nameof(interactions));
        _layers = layers;
    }

    public float? Progress { get; private set; }
    public WorldVoxelCoord? Target => _target?.Position;

    public void Cancel()
    {
        _target = null;
        _work = 0;
        _swingTicks = 0;
        Progress = null;
    }

    public bool Advance(
        VoxelWorldHit? hit,
        InventoryStack? held,
        int hotbarSlot,
        uint elapsedTicks,
        PlayerGameMode mode, ulong worldTick = 0)
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
        var surface = _world.GetBlockSurfaceStateOrEmpty(position);
        var mining = block.Mining;
        BlockFace? hitFace = null;
        string? layerId = null;
        if (_layers is not null && VoxelHitFace.TryResolve(hit.Value, out var face))
        {
            hitFace = face;
            if (surface.TryGetTop(face, out var top))
            {
                // Never mine through an unrecognized or protected layer.
                if (!_layers.TryGet(top.LayerId, out var definition) ||
                    definition is null)
                {
                    Cancel();
                    return false;
                }
                layerId = top.LayerId;
                mining = definition.Mining;
            }
        }
        if (block.Mining.Unbreakable || mining.Unbreakable ||
            block.Interaction == BlockInteractionKind.Pickup ||
            _tools.EffectiveMiningSpeed(held, mining) is not { } speed)
        {
            Cancel();
            return false;
        }

        var target = new MiningTarget(
            position, cell, surface, hitFace, layerId, held?.Entry, hotbarSlot);
        var started = _target != target;
        if (started)
        {
            _target = target;
            _work = 0;
            _swingTicks = 0;
            PublishFeedback(hit.Value, completed: false);
        }
        else
        {
            var totalTicks = (ulong)_swingTicks + elapsedTicks;
            _swingTicks = (uint)(totalTicks % SwingIntervalTicks);
            if (totalTicks >= SwingIntervalTicks)
                PublishFeedback(hit.Value, completed: false);
        }

        var requiredWork = DefaultBreakWorkTicks * mining.Hardness;
        _work = Math.Min(
            requiredWork,
            _work + (double)elapsedTicks * speed);
        // Authored decimal hardness is stored as float; tolerate its sub-tick
        // representation error so exact required work still completes.
        if (requiredWork > 0 && _work + 0.00001 < requiredWork)
        {
            Progress = (float)(_work / requiredWork);
            return false;
        }

        // Capture appearance before the accepted mutation removes the
        // source voxel or its top interactive layer.
        var lastAppearance = MiningFragmentAppearance.Resolve(
            _world, _blocks, _layers, hit.Value);
        var completed = _interactions.Break(
            hit.Value,
            BlockBreakLootPolicy.DropSelf, worldTick).Accepted;
        if (completed)
            PublishFeedback(hit.Value, completed: true, lastAppearance);
        Cancel();
        return completed;
    }

    private void PublishFeedback(
        VoxelWorldHit hit, bool completed,
        MiningFragmentAppearance? capturedAppearance = null)
    {
        LastFeedback = new MiningFeedbackEvent(hit, completed,
            capturedAppearance ?? MiningFragmentAppearance.Resolve(
                _world, _blocks, _layers, hit));
        FeedbackRevision++;
    }

    private readonly record struct MiningTarget(
        WorldVoxelCoord Position,
        VoxelCell Cell,
        BlockSurfaceState Surface,
        BlockFace? Face,
        string? LayerId,
        InventoryEntry? Held,
        int HotbarSlot);
}

/// <summary>Transient, presentation-only cue; no physical or inventory effect.</summary>
public readonly record struct MiningFeedbackEvent(
    VoxelWorldHit Hit, bool Completed,
    MiningFragmentAppearance? Appearance = null);
