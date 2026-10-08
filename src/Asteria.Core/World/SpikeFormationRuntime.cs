using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Maintains the local, bounded profile of player-built and edited spikes.
/// Only same-material, same-direction segments join a formation.
/// All writes pass through the authoritative mutation runtime.
/// </summary>
public sealed class SpikeFormationRuntime
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly DroppedBlockRuntime _drops;
    private bool _applying;

    public SpikeFormationRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelMutationRuntime mutations,
        DroppedBlockRuntime drops)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _drops = drops ?? throw new ArgumentNullException(nameof(drops));
        _mutations.BlockCellChanged += OnVoxelEdited;
    }

    public BlockPlacementDecision Place(
        BlockPlacementDecision decision,
        bool down)
    {
        if (!decision.Accepted)
            return decision;

        var target = decision.Position;
        var cell = decision.Cell;
        var direction = down ? -1 : 1;
        var previous = new List<(WorldVoxelCoord Position, VoxelCell Cell)>();
        var support = Offset(target, -direction);

        while (support.Y >= 0)
        {
            if (!_world.TryGetCell(support, out var neighbor))
            {
                return BlockPlacementDecision.Reject(
                    target, cell, BlockPlacementRejection.SupportUnloaded);
            }

            if (neighbor.Block != cell.Block ||
                SpikeSegmentState.IsDown(neighbor.State) != down)
                break;

            previous.Add((support, neighbor));
            if (previous.Count >= SpikeSegmentState.MaximumHeight)
            {
                return BlockPlacementDecision.Reject(
                    target, cell, BlockPlacementRejection.FormationTooTall);
            }

            support = Offset(support, -direction);
        }

        if (!SupportsBase(support, cell.Block, down))
        {
            return BlockPlacementDecision.Reject(
                target, cell,
                support.Y >= 0 && !_world.IsLoadedAt(support)
                    ? BlockPlacementRejection.SupportUnloaded
                    : BlockPlacementRejection.MissingSupport);
        }

        previous.Reverse();
        var height = previous.Count + 1;
        var changes = new List<SpikeVoxelChange>(height);
        for (var index = 0; index < previous.Count; index++)
        {
            var entry = previous[index];
            changes.Add(new SpikeVoxelChange(
                entry.Position,
                WithProfile(entry.Cell, index, height, down)));
        }

        var placed = WithProfile(cell, height - 1, height, down);
        changes.Add(new SpikeVoxelChange(target, placed));
        Apply(changes);
        return decision with { Cell = placed };
    }

    public BlockBreakDecision Break(
        BlockBreakDecision decision,
        BlockBreakLootPolicy lootPolicy)
    {
        var edits = new List<SpikeVoxelChange>();
        var detached = new List<WorldVoxelCoord>();
        if (!PlanRemoval(
                decision.Position, decision.Cell, alreadyRemoved: false,
                edits, detached))
        {
            return BlockBreakDecision.Reject(
                decision.Position, BlockBreakRejection.MutationRejected);
        }

        Apply(edits);
        if (lootPolicy == BlockBreakLootPolicy.DropSelf)
        {
            SpawnDrop(decision.Cell.Block, decision.Position);
            foreach (var position in detached)
                SpawnDrop(decision.Cell.Block, position);
        }

        return decision;
    }

    /// <summary>
    /// Applies removal of a supporting block without routing through an
    /// unrestricted mutation event. This preserves Creative loot policy
    /// when attached spikes are detached by the same player operation.
    /// </summary>
    public bool RemoveSupport(
        WorldVoxelCoord position, BlockBreakLootPolicy lootPolicy)
    {
        _applying = true;
        try
        {
            if (!_mutations.SetCellAt(position, VoxelCell.Empty, out _))
                return false;
        }
        finally
        {
            _applying = false;
        }

        DetachUnsupportedBase(Offset(position, 1), down: false, lootPolicy);
        if (position.Y > 0)
            DetachUnsupportedBase(Offset(position, -1), down: true, lootPolicy);
        return true;
    }

    private bool SupportsBase(
        WorldVoxelCoord position,
        BlockRuntimeId block,
        bool down)
    {
        if (position.Y < 0 ||
            !_world.TryGetCell(position, out var support) ||
            support.IsEmpty)
            return false;

        var definition = _blocks.GetDefinition(support.Block);
        var dependent = _blocks.GetDefinition(block);
        return BlockGeometry.SupportsFaceCoverage(
            definition, support,
            _world.GetMicroblockMaskOrEmpty(position),
            down ? BlockFace.Bottom : BlockFace.Top,
            dependent, new VoxelCell(block,
                state: SpikeSegmentState.Encode(0, 1, down)),
            MicroblockMask.Empty,
            down ? BlockFace.Top : BlockFace.Bottom);
    }

    private bool PlanRemoval(
        WorldVoxelCoord removed,
        VoxelCell former,
        bool alreadyRemoved,
        List<SpikeVoxelChange> edits,
        List<WorldVoxelCoord> detached)
    {
        var down = SpikeSegmentState.IsDown(former.State);
        var index = SpikeSegmentState.Index(former.State);
        var height = SpikeSegmentState.Height(former.State);
        var direction = down ? -1 : 1;
        var origin = Offset(removed, -direction * index);
        var segments = new List<(WorldVoxelCoord Position, VoxelCell Cell, int Index)>();

        for (var part = 0; part < height; part++)
        {
            var position = Offset(origin, direction * part);
            if (position.Y < 0 || !_world.TryGetCell(position, out var existing))
                return false;

            if (part == index)
            {
                if (!alreadyRemoved &&
                    existing != former)
                    return false;
                continue;
            }

            if (existing.Block != former.Block ||
                SpikeSegmentState.IsDown(existing.State) != down ||
                SpikeSegmentState.Index(existing.State) != part ||
                SpikeSegmentState.Height(existing.State) != height)
                continue;

            segments.Add((position, existing, part));
        }

        var rootLength = 0;
        while (rootLength < index &&
               segments.Any(segment => segment.Index == rootLength))
            rootLength++;

        foreach (var segment in segments)
        {
            if (segment.Index < rootLength)
            {
                edits.Add(new SpikeVoxelChange(
                    segment.Position,
                    WithProfile(
                        segment.Cell, segment.Index, rootLength, down)));
            }
            else
            {
                edits.Add(new SpikeVoxelChange(
                    segment.Position, VoxelCell.Empty));
                detached.Add(segment.Position);
            }
        }

        if (!alreadyRemoved)
            edits.Add(new SpikeVoxelChange(removed, VoxelCell.Empty));
        return true;
    }

    private void OnVoxelEdited(VoxelWorldEdit edit)
    {
        if (_applying || edit.Previous == edit.Current)
            return;

        // Handle all normal mutation routes, including tool edits and
        // support removal, not only block interactions by a player.
        var previous = edit.Previous;
        var wasSpike = !previous.IsEmpty &&
            _blocks.GetDefinition(previous.Block).Shape.Kind == BlockShapeKind.Spike;
        if (wasSpike && edit.Current.Block != previous.Block)
        {
            var edits = new List<SpikeVoxelChange>();
            var detached = new List<WorldVoxelCoord>();
            if (PlanRemoval(edit.Position, previous, alreadyRemoved: true,
                    edits, detached))
            {
                Apply(edits);
                foreach (var position in detached)
                    SpawnDrop(previous.Block, position);
            }
        }

        DetachUnsupportedBase(
            Offset(edit.Position, 1), down: false,
            BlockBreakLootPolicy.DropSelf);
        if (edit.Position.Y > 0)
            DetachUnsupportedBase(
                Offset(edit.Position, -1), down: true,
                BlockBreakLootPolicy.DropSelf);
    }

    private void DetachUnsupportedBase(
        WorldVoxelCoord root, bool down, BlockBreakLootPolicy lootPolicy)
    {
        if (root.Y < 0 || !_world.TryGetCell(root, out var cell) ||
            cell.IsEmpty ||
            _blocks.GetDefinition(cell.Block).Shape.Kind != BlockShapeKind.Spike ||
            SpikeSegmentState.Index(cell.State) != 0 ||
            SpikeSegmentState.IsDown(cell.State) != down)
            return;

        var support = Offset(root, down ? 1 : -1);
        if (support.Y >= 0 && !_world.IsLoadedAt(support))
            return;
        if (SupportsBase(support, cell.Block, down))
            return;

        var edits = new List<SpikeVoxelChange>();
        var detached = new List<WorldVoxelCoord>();
        if (!PlanRemoval(root, cell, alreadyRemoved: false, edits, detached))
            return;

        Apply(edits);
        if (lootPolicy == BlockBreakLootPolicy.DropSelf)
        {
            foreach (var position in detached)
                SpawnDrop(cell.Block, position);
            SpawnDrop(cell.Block, root);
        }
    }

    private void Apply(IReadOnlyList<SpikeVoxelChange> changes)
    {
        _applying = true;
        try
        {
            foreach (var change in changes)
            {
                if (_world.GetCellOrEmpty(change.Position) == change.Cell)
                    continue;
                if (!_mutations.SetCellAt(change.Position, change.Cell, out _))
                    throw new InvalidOperationException(
                        $"Validated spike mutation failed at {change.Position}.");
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private void SpawnDrop(BlockRuntimeId block, WorldVoxelCoord position)
    {
        if (!_blocks.GetDefinition(block).DropsSelf)
            return;

        // Segment indexing/direction is structure state, not portable item data.
        _drops.Spawn(
            BlockStateSnapshot.FromCell(new VoxelCell(block)),
            new Vector3(position.X + 0.5f,
                position.Y + 0.5f, position.Z + 0.5f));
    }

    private static VoxelCell WithProfile(
        VoxelCell cell, int index, int height, bool down) =>
        new(cell.Block, cell.TextureRotation, cell.Orientation,
            cell.Facing, SpikeSegmentState.Encode(index, height, down));

    private static WorldVoxelCoord Offset(WorldVoxelCoord point, int dy) =>
        point + (0, dy, 0);

    private readonly record struct SpikeVoxelChange(
        WorldVoxelCoord Position,
        VoxelCell Cell);
}
