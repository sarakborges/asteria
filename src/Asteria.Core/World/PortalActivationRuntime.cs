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
    private const string PortalTargetPrefix = "portal_destination:";
    private const int MaximumFrameVoxels = 1024;
    private const int MaximumInteriorVoxels = 512;
    private const int MaximumSpan = 64;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly Dictionary<string, BlockRuntimeId> _portalsByTarget =
        new(StringComparer.Ordinal);
    private readonly Dictionary<BlockRuntimeId, string> _targetsByPortal = [];

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

        foreach (var (id, block) in blocks.AuthoredDefinitions())
        foreach (var tag in block.Tags)
        {
            if (!tag.StartsWith(PortalTargetPrefix, StringComparison.Ordinal))
                continue;

            var target = tag[PortalTargetPrefix.Length..];
            BlockDefinition.ValidateId(target);
            if (!block.Shape.IsCenteredLayer || block.IsCollidable ||
                block.LightDampening != 0 ||
                !block.Orientations.Contains(BlockOrientation.X) ||
                !block.Orientations.Contains(BlockOrientation.Y) ||
                !block.Orientations.Contains(BlockOrientation.Z))
                throw new InvalidOperationException(
                    $"Portal interior {block.Id} must be a noncolliding, oriented centered layer.");

            if (!_portalsByTarget.TryAdd(target, id) ||
                !_targetsByPortal.TryAdd(id, target))
                throw new InvalidOperationException(
                    $"Duplicate or ambiguous portal destination: {target}");
        }
    }

    public static bool IsSlicer(InventoryStack? selected) =>
        selected is { Kind: InventoryEntryKind.Item, Id: SlicerItemId };

    public bool TryGetDestinationAt(WorldVoxelCoord position, out string destination)
    {
        destination = string.Empty;
        if (!_world.TryGetCell(position, out var cell) || cell.IsEmpty ||
            !_targetsByPortal.TryGetValue(cell.Block, out var target))
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
            !_portalsByTarget.TryGetValue(target, out var portal))
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

        var blockState = BlockStateSnapshot.FromCell(
            new VoxelCell(portal, orientation: orientation));
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
