using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Prepares the consequences of replacing or clearing a block that supports
/// Objects. The mutation runtime preflights the shared drop capacity before
/// changing the voxel and commits these exact rewards only after success.
/// </summary>
public sealed class ObjectDetachmentRuntime
{
    private readonly VoxelWorld _world;
    private readonly PackContentRegistry<ObjectDefinition> _objects;
    private readonly DroppedBlockRuntime _drops;
    private readonly FluidRegistry? _fluids;

    public ObjectDetachmentRuntime(VoxelWorld world,
        PackContentRegistry<ObjectDefinition> objects, DroppedBlockRuntime drops,
        FluidRegistry? fluids = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _drops = drops ?? throw new ArgumentNullException(nameof(drops));
        _fluids = fluids;
    }

    internal int AvailableDropCapacity => _drops.AvailableCapacity;

    /// <summary>Authoritative drop count for the currently attached objects,
    /// ignoring non-dropping content. A suppress policy still detaches them.</summary>
    public int DropCount(WorldVoxelCoord support, BlockBreakLootPolicy lootPolicy)
    {
        if (!Enum.IsDefined(lootPolicy))
            throw new ArgumentOutOfRangeException(nameof(lootPolicy));
        if (lootPolicy == BlockBreakLootPolicy.Suppress ||
            !_world.TryGetObjects(support, out var attached))
            return 0;
        return attached.Count(obj =>
            _objects.TryGet(obj.Id, out var definition) &&
            definition is { DropSelf: true });
    }

    internal bool TryPrepare(
        WorldVoxelCoord support, VoxelCell previous, VoxelCell next,
        BlockBreakLootPolicy lootPolicy, out ObjectDetachmentPlan plan)
    {
        plan = ObjectDetachmentPlan.Empty;
        if (previous.IsEmpty || (!next.IsEmpty && previous.Block == next.Block))
            return true;
        if (!Enum.IsDefined(lootPolicy))
            throw new ArgumentOutOfRangeException(nameof(lootPolicy));
        if (lootPolicy == BlockBreakLootPolicy.Suppress ||
            !_world.TryGetObjects(support, out var attached) ||
            attached.Count == 0)
            return true;

        var rewards = new List<ObjectDetachedDrop>();
        // Storage iteration is stable, even if an Object was placed in a
        // different order than its peers. Stacks remain separate per instance.
        foreach (var obj in attached.OrderBy(o => o.Id, StringComparer.Ordinal)
                     .ThenBy(o => o.Face).ThenBy(o => o.Rotation)
                     .ThenBy(o => o.Transform.OffsetX)
                     .ThenBy(o => o.Transform.OffsetY)
                     .ThenBy(o => o.Transform.OffsetZ))
        {
            if (!_objects.TryGet(obj.Id, out var definition) ||
                definition is not { DropSelf: true })
                continue;
            var point = WorldObjectGeometry.Pose(support, obj, definition).Position +
                Vector3.UnitY * .25f;
            rewards.Add(new ObjectDetachedDrop(
                new InventoryStack(definition.RewardEntry), point));
        }

        if (rewards.Count > _drops.AvailableCapacity)
            return false;
        plan = new ObjectDetachmentPlan(rewards);
        return true;
    }

    /// <summary>Preflights solid-supported Objects that need water above.
    /// The target can lie in a different chunk from its supporting block.</summary>
    internal bool TryPrepareFluidAbove(
        WorldVoxelCoord target, FluidCell next,
        out ObjectFluidDetachmentPlan plan)
    {
        plan = ObjectFluidDetachmentPlan.Empty;
        if (target.Y <= 0) return true;
        var support = target + (0, -1, 0);
        if (!_world.TryGetObjects(support, out var attached) || attached.Count == 0)
            return true;

        var removed = new List<ObjectCell>();
        var rewards = new List<ObjectDetachedDrop>();
        foreach (var obj in attached)
        {
            if (obj.Face != BlockFace.Top ||
                !_objects.TryGet(obj.Id, out var definition) ||
                definition is not { RequiredFluidAbove: { } fluidId })
                continue;
            if (_fluids is null || !_fluids.TryGetId(fluidId, out var required))
                throw new InvalidOperationException(
                    $"Submerged Object {obj.Id} requires a registered fluid {fluidId}.");
            if (next.Fluid == required)
                continue;
            removed.Add(obj);
            if (!definition.DropSelf)
                continue;
            rewards.Add(new ObjectDetachedDrop(
                new InventoryStack(definition.RewardEntry),
                WorldObjectGeometry.Pose(support, obj, definition).Position +
                Vector3.UnitY * .25f));
        }

        if (rewards.Count > _drops.AvailableCapacity)
            return false;
        if (removed.Count != 0)
            plan = new ObjectFluidDetachmentPlan(
                support, removed, new ObjectDetachmentPlan(rewards));
        return true;
    }

    internal void Commit(ObjectDetachmentPlan plan)
    {
        if (plan.Rewards.Count > _drops.AvailableCapacity)
            throw new InvalidOperationException(
                "Object detachment drop capacity changed after preflight.");
        foreach (var reward in plan.Rewards)
            _drops.Spawn(reward.Stack, reward.Position);
    }
}

internal readonly record struct ObjectDetachedDrop(
    InventoryStack Stack, Vector3 Position);

internal sealed record ObjectDetachmentPlan(
    IReadOnlyList<ObjectDetachedDrop> Rewards)
{
    public static ObjectDetachmentPlan Empty { get; } = new([]);
}

internal sealed record ObjectFluidDetachmentPlan(
    WorldVoxelCoord Support,
    IReadOnlyList<ObjectCell> Objects,
    ObjectDetachmentPlan Drops)
{
    public static ObjectFluidDetachmentPlan Empty { get; } =
        new(default, [], ObjectDetachmentPlan.Empty);
}

