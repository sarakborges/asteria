namespace Asteria.Core.World;

/// <summary>
/// Physical glass-frame activation. Geometry is bounded, deterministic and
/// belongs in Core; canonical voxel mutations remain the sole world writer.
/// Persistent portal interiors are non-collidable centered Layer-shape voxels
/// because empty voxels cannot hold AttachedBlockLayer in Asteria.
/// </summary>
public sealed class PortalActivationRuntime
{
    public const string SlicerItemId = "asteria:dimensional_slicer";
    public const string TargetMetadataKey = "target_dimension";
    private const string FrameTag = "is_portal_frame";
    private const string FrameTargetPrefix = "accept_dimension:";
    public const string PortalBlockId = "asteria:portal";
    // Match MineClone's bounded frame and interior capacity. The separate
    // 64-voxel axis span keeps shape discovery spatially constrained.
    private const int MaximumFrameVoxels = 4096;
    private const int MaximumInteriorVoxels = 4096;
    private const int MaximumSpan = 64;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly BlockRuntimeId _portalBlock;
    private readonly HashSet<string> _validTargets = new(StringComparer.Ordinal);

    private enum CellKind : byte { Frame, Empty, Blocked }

    private readonly record struct PortalPlane(
        BlockOrientation Orientation,
        (int X, int Y, int Z) AxisU,
        (int X, int Y, int Z) AxisV);

    private static readonly PortalPlane[] Planes =
    [
        new(BlockOrientation.X, (0, 1, 0), (0, 0, 1)),
        new(BlockOrientation.Y, (1, 0, 0), (0, 0, 1)),
        new(BlockOrientation.Z, (1, 0, 0), (0, 1, 0)),
    ];

    public PortalActivationRuntime(
        VoxelWorld world, BlockRegistry blocks, VoxelMutationRuntime mutations)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));

        _portalBlock = blocks.GetId(PortalBlockId);
        var portal = blocks.GetDefinition(_portalBlock);
        if (!portal.Shape.IsCenteredLayer || portal.IsCollidable ||
            portal.LightDampening != 0 ||
            !portal.Orientations.Contains(BlockOrientation.X) ||
            !portal.Orientations.Contains(BlockOrientation.Y) ||
            !portal.Orientations.Contains(BlockOrientation.Z))
            throw new InvalidOperationException(
                $"Portal {PortalBlockId} must be a noncolliding oriented centered layer.");

        foreach (var (_, block) in blocks.AuthoredDefinitions())
        {
            if (!block.HasTag(FrameTag)) continue;
            foreach (var tag in block.Tags)
            {
                if (!tag.StartsWith(FrameTargetPrefix, StringComparison.Ordinal))
                    continue;
                var target = tag[FrameTargetPrefix.Length..];
                BlockDefinition.ValidateId(target);
                _validTargets.Add(target);
            }
        }

        // Frame destruction invalidates its bounded portal region using the
        // existing mutation owner, so invisible portals never remain live.
        _mutations.BlockCellChanged += OnBlockCellChanged;
    }

    private void OnBlockCellChanged(VoxelWorldEdit edit)
    {
        var previous = _blocks.GetDefinition(edit.Previous.Block);
        if (!previous.HasTag(FrameTag))
            return;

        var next = _blocks.GetDefinition(edit.Current.Block);
        foreach (var plane in Planes)
        foreach (var neighbor in Neighbors(edit.Position, plane))
        {
            if (!_world.TryGetCell(neighbor, out var cell) || cell.IsEmpty ||
                cell.Block != _portalBlock ||
                !_world.GetBlockSurfaceStateOrEmpty(neighbor).MetaTags
                    .TryGetValue(TargetMetadataKey, out var destination) ||
                !previous.HasTag(FrameTargetPrefix + destination) ||
                (next.HasTag(FrameTag) &&
                 next.HasTag(FrameTargetPrefix + destination)) ||
                cell.Orientation != plane.Orientation)
                continue;

            // The removed boundary segment touched this portal plane.
            // Revoke the complete contiguous interior, never leave an
            // unframed, unbreakable or invisible travel trigger behind.
            var region = CollectPortalRegion(
                neighbor, destination, plane);
            foreach (var at in region)
            {
                if (!_mutations.SetCellAt(at, VoxelCell.Empty, out _))
                    throw new InvalidOperationException(
                        $"Resident portal cleanup failed at {at}.");
            }
        }
    }

    private IReadOnlyList<WorldVoxelCoord> CollectPortalRegion(
        WorldVoxelCoord start, string destination, PortalPlane plane)
    {
        var visited = new HashSet<WorldVoxelCoord> { start };
        var queue = new Queue<WorldVoxelCoord>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in Neighbors(current, plane))
            {
                if (visited.Contains(neighbor) ||
                    !_world.TryGetCell(neighbor, out var cell) ||
                    cell.Block != _portalBlock ||
                    cell.Orientation != plane.Orientation ||
                    !_world.GetBlockSurfaceStateOrEmpty(neighbor).MetaTags
                        .TryGetValue(TargetMetadataKey, out var neighborDestination) ||
                    !StringComparer.Ordinal.Equals(neighborDestination, destination))
                    continue;

                if (visited.Count >= MaximumInteriorVoxels)
                    throw new InvalidOperationException(
                        "Resident portal exceeds bounded activation area.");
                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }
        return visited.OrderBy(v => v.X)
            .ThenBy(v => v.Z).ThenBy(v => v.Y).ToArray();
    }

    public static bool IsSlicer(InventoryStack? selected) =>
        selected is { Kind: InventoryEntryKind.Item, Id: SlicerItemId };

    public bool TryGetDestinationAt(WorldVoxelCoord position, out string destination)
    {
        destination = string.Empty;
        if (!_world.TryGetCell(position, out var cell) || cell.IsEmpty ||
            cell.Block != _portalBlock ||
            !_world.GetBlockSurfaceStateOrEmpty(position).MetaTags
                .TryGetValue(TargetMetadataKey, out var target) ||
            !_validTargets.Contains(target))
            return false;
        destination = target;
        return true;
    }

    /// <summary>
    /// The entire closed, empty, fluid-free region must pass preflight;
    /// no selected Slicer is consumed here. The caller handles inventory
    /// only after one accepted mutation transaction.
    /// </summary>
    public bool TryActivate(
        InventoryStack? selected, VoxelWorldHit hit,
        out string destination)
    {
        destination = string.Empty;
        if (!IsSlicer(selected) ||
            !selected!.Entry.Metadata.TryGetValue(TargetMetadataKey, out var target) ||
            !_validTargets.Contains(target))
            return false;

        if (Classify(hit.Voxel, target) != CellKind.Frame)
            return false;

        List<WorldVoxelCoord>? interior = null;
        BlockOrientation orientation = default;
        foreach (var plane in Planes)
        {
            var candidate = FindInterior(hit.Voxel, target, plane);
            if (candidate is null)
                continue;

            if (interior is not null) // Ambiguous plane: never guess.
                return false;
            interior = candidate;
            orientation = plane.Orientation;
        }

        if (interior is null || interior.Count == 0)
            return false;

        var blockState = new BlockStateSnapshot(
            new VoxelCell(_portalBlock, orientation: orientation),
            MicroblockMask.Empty,
            new BlockSurfaceState(null,
                metaTags: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [TargetMetadataKey] = target,
                }));
        var changes = interior.Select(at =>
            new VoxelStructureChange(at, blockState, FluidCell.Empty)).ToArray();

        if (!_mutations.ApplyStructureChanges(changes))
            return false;

        destination = target;
        return true;
    }

    private List<WorldVoxelCoord>? FindInterior(
        WorldVoxelCoord clicked, string target, PortalPlane plane)
    {
        var frame = new HashSet<WorldVoxelCoord> { clicked };
        var queue = new Queue<WorldVoxelCoord>();
        queue.Enqueue(clicked);

        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var neighbor in Neighbors(at, plane))
            {
                if (!WithinSpan(clicked, neighbor) ||
                    Classify(neighbor, target) != CellKind.Frame ||
                    !frame.Add(neighbor))
                    continue;

                if (frame.Count > MaximumFrameVoxels)
                    return null;
                queue.Enqueue(neighbor);
            }
        }

        var testedSeeds = new HashSet<WorldVoxelCoord>();
        foreach (var at in frame.OrderBy(v => v.X)
                     .ThenBy(v => v.Y).ThenBy(v => v.Z))
        foreach (var seed in Neighbors(at, plane))
        {
            if (!testedSeeds.Add(seed) ||
                Classify(seed, target) != CellKind.Empty)
                continue;
            var enclosed = FloodInterior(seed, target, plane);
            if (enclosed is not null)
                return enclosed;
        }
        return null;
    }

    private List<WorldVoxelCoord>? FloodInterior(
        WorldVoxelCoord seed, string target, PortalPlane plane)
    {
        var region = new HashSet<WorldVoxelCoord> { seed };
        var queue = new Queue<WorldVoxelCoord>();
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (region.Count > MaximumInteriorVoxels ||
                !WithinSpan(seed, at))
                return null;

            foreach (var neighbor in Neighbors(at, plane))
            {
                if (!WithinSpan(seed, neighbor))
                    return null;
                switch (Classify(neighbor, target))
                {
                    case CellKind.Frame: break;
                    case CellKind.Blocked: return null;
                    case CellKind.Empty:
                        if (region.Add(neighbor))
                        {
                            if (region.Count > MaximumInteriorVoxels)
                                return null;
                            queue.Enqueue(neighbor);
                        }
                        break;
                }
            }
        }

        return region.OrderBy(v => v.X)
            .ThenBy(v => v.Z).ThenBy(v => v.Y).ToList();
    }

    private CellKind Classify(WorldVoxelCoord at, string target)
    {
        if (at.Y < 0 || !_world.TryGetCell(at, out var cell) ||
            !_world.TryGetFluid(at, out var fluid))
            return CellKind.Blocked;

        if (cell.IsEmpty)
            return fluid.IsEmpty ? CellKind.Empty : CellKind.Blocked;

        var definition = _blocks.GetDefinition(cell.Block);
        return definition.HasTag(FrameTag) &&
               definition.HasTag(FrameTargetPrefix + target)
            ? CellKind.Frame : CellKind.Blocked;
    }

    private static bool WithinSpan(WorldVoxelCoord center, WorldVoxelCoord at) =>
        at.Y >= 0 &&
        Math.Abs((long)at.X - center.X) <= MaximumSpan &&
        Math.Abs((long)at.Y - center.Y) <= MaximumSpan &&
        Math.Abs((long)at.Z - center.Z) <= MaximumSpan;

    private static WorldVoxelCoord[] Neighbors(WorldVoxelCoord at, PortalPlane plane) =>
    [
        at + plane.AxisU,
        at + (-plane.AxisU.X, -plane.AxisU.Y, -plane.AxisU.Z),
        at + plane.AxisV,
        at + (-plane.AxisV.X, -plane.AxisV.Y, -plane.AxisV.Z),
    ];
}
