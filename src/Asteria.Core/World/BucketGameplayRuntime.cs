using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Collects authored fluid sources and pours fluid into a valid adjacent
/// loaded voxel. A bucket changes only its own metadata and never creates
/// or destroys fluid without a corresponding inventory transition.
/// </summary>
public sealed class BucketGameplayRuntime
{
    public const string FluidMetadataKey = "contained_fluid";
    public const float MaximumReach = 8f;

    private readonly VoxelWorld _world;
    private readonly FluidRegistry _fluids;
    private readonly VoxelMutationRuntime _mutations;
    private readonly PackContentRegistry<ToolDefinition> _tools;

    public BucketGameplayRuntime(
        VoxelWorld world,
        FluidRegistry fluids,
        VoxelMutationRuntime mutations,
        PackContentRegistry<ToolDefinition> tools)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _fluids = fluids ?? throw new ArgumentNullException(nameof(fluids));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public bool IsEquipped(InventoryStack? selected) =>
        selected is { Kind: InventoryEntryKind.Tool } &&
        _tools.TryGet(selected.Id, out var tool) &&
        tool?.RightBehavior == "asteria:bucket/use";

    public bool TryUse(
        PlayerInventory inventory,
        Vector3 origin,
        Vector3 direction,
        VoxelWorldHit? blockTarget)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var selected = inventory.SelectedStack;
        if (!IsEquipped(selected) || selected!.Quantity != 1)
            return false;

        if (!selected.Entry.Metadata.TryGetValue(FluidMetadataKey, out var contained))
        {
            var source = FindCollectibleSource(origin, direction, MaximumReach);
            if (source is not { } found) return false;
            var fluid = _world.GetFluidOrEmpty(found);
            if (!fluid.IsSource || fluid.IsEmpty) return false;
            var fluidId = _fluids.GetDefinition(fluid.Fluid).Id;
            var metadata = new Dictionary<string, string>(
                selected.Entry.Metadata, StringComparer.Ordinal)
            {
                [FluidMetadataKey] = fluidId,
            };
            var filled = InventoryEntry.FromTool(
                selected.Id, metadata, selected.MaxStackSize);

            if (!_mutations.SetFluidAt(found, FluidCell.Empty, out _))
                return false;
            if (inventory.TryReplaceSelected(selected, filled)) return true;
            if (!_mutations.SetFluidAt(found, fluid, out _))
                throw new InvalidOperationException(
                    "Failed to restore source after bucket inventory rejection.");
            return false;
        }

        if (!_fluids.TryGetId(contained, out var fluidRuntimeId) ||
            blockTarget is not { HasSurfaceNormal: true } target)
            return false;

        // A block ray result is a hint, never authority after the world
        // changes: reject a stale or unloaded placement face.
        if (!_world.IsLoadedAt(target.Voxel) ||
            _world.GetCellOrEmpty(target.Voxel).IsEmpty)
            return false;

        var dest = new WorldVoxelCoord(
            target.Voxel.X + target.NormalX,
            target.Voxel.Y + target.NormalY,
            target.Voxel.Z + target.NormalZ);
        if (dest.Y < 0 || !_world.IsLoadedAt(dest) ||
            !_world.GetCellOrEmpty(dest).IsEmpty ||
            !_world.GetFluidOrEmpty(dest).IsEmpty)
            return false;

        var metadataWithoutFluid = new Dictionary<string, string>(
            selected.Entry.Metadata, StringComparer.Ordinal);
        metadataWithoutFluid.Remove(FluidMetadataKey);
        var emptied = InventoryEntry.FromTool(
            selected.Id, metadataWithoutFluid, selected.MaxStackSize);
        var placed = FluidCell.Source(fluidRuntimeId);
        if (!_mutations.SetFluidAt(dest, placed, out _))
            return false;
        if (inventory.TryReplaceSelected(selected, emptied)) return true;
        if (!_mutations.SetFluidAt(dest, FluidCell.Empty, out _))
            throw new InvalidOperationException(
                "Failed to roll back fluid after bucket inventory rejection.");
        return false;
    }

    /// <summary>Block-occluded DDA: only true fluid source cells are collectible.</summary>
    public WorldVoxelCoord? FindCollectibleSource(
        Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (!float.IsFinite(maxDistance) || maxDistance <= 0 ||
            !float.IsFinite(origin.X) || !float.IsFinite(origin.Y) ||
            !float.IsFinite(origin.Z) ||
            !float.IsFinite(direction.X) || !float.IsFinite(direction.Y) ||
            !float.IsFinite(direction.Z) ||
            direction.LengthSquared() <= float.Epsilon)
            return null;

        direction = Vector3.Normalize(direction);
        var x = (int)MathF.Floor(origin.X);
        var y = (int)MathF.Floor(origin.Y);
        var z = (int)MathF.Floor(origin.Z);
        var dx = Math.Sign(direction.X);
        var dy = Math.Sign(direction.Y);
        var dz = Math.Sign(direction.Z);
        var stepX = Reciprocal(direction.X);
        var stepY = Reciprocal(direction.Y);
        var stepZ = Reciprocal(direction.Z);
        var limitX = InitialLimit(origin.X, x, direction.X);
        var limitY = InitialLimit(origin.Y, y, direction.Y);
        var limitZ = InitialLimit(origin.Z, z, direction.Z);
        var travel = 0f;

        while (travel <= maxDistance)
        {
            if (y < 0) return null;
            var pos = new WorldVoxelCoord(x, y, z);
            if (!_world.IsLoadedAt(pos) || !_world.GetCellOrEmpty(pos).IsEmpty)
                return null;
            var fluid = _world.GetFluidOrEmpty(pos);
            if (fluid.IsSource && !fluid.IsEmpty)
                return pos;

            if (limitX <= limitY && limitX <= limitZ)
            {
                travel = limitX;
                x += dx;
                limitX += stepX;
            }
            else if (limitY <= limitZ)
            {
                travel = limitY;
                y += dy;
                limitY += stepY;
            }
            else
            {
                travel = limitZ;
                z += dz;
                limitZ += stepZ;
            }
        }
        return null;
    }

    private static float Reciprocal(float axis) =>
        axis == 0f ? float.PositiveInfinity : 1f / MathF.Abs(axis);

    private static float InitialLimit(float origin, int voxel, float axis) =>
        axis > 0f ? (voxel + 1f - origin) / axis
        : axis < 0f ? (origin - voxel) / -axis
        : float.PositiveInfinity;
}
