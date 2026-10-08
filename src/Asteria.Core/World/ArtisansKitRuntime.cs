using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Authored microblock editing. This capability never writes world voxels
/// except through VoxelMutationRuntime, and never creates a parent in air.
/// </summary>
public sealed class ArtisansKitRuntime
{
    private const float MaximumReach = 8f;
    private const int SamplesPerVoxel = 128;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly PackContentRegistry<ToolDefinition> _tools;

    public ArtisansKitRuntime(
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

    public MicroblockResolution Resolution { get; private set; } =
        MicroblockResolution.Thick;

    public bool IsEquipped(InventoryStack? held) =>
        Behavior(held, ToolUseHand.Left) == "asteria:artisans_kit/remove" &&
        Behavior(held, ToolUseHand.Right) == "asteria:artisans_kit/restore";

    public MicroblockResolution CycleResolution()
    {
        Resolution = Resolution switch
        {
            MicroblockResolution.Thick => MicroblockResolution.Thin,
            MicroblockResolution.Thin => MicroblockResolution.ExtraThin,
            MicroblockResolution.ExtraThin => MicroblockResolution.Thick,
            _ => throw new InvalidOperationException("Unknown Artisan's Kit resolution."),
        };
        return Resolution;
    }

    public bool TryEdit(
        InventoryStack? held,
        ToolUseHand hand,
        VoxelWorldHit target,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        WorldAabb playerBounds)
    {
        if (!IsEquipped(held) ||
            !Enum.IsDefined(hand) ||
            !IsFinite(rayOrigin) || !IsFinite(rayDirection) ||
            rayDirection.LengthSquared() < 0.000001f ||
            !target.HasSurfaceNormal ||
            !_world.TryGetCell(target.Voxel, out var cell) || cell.IsEmpty)
            return false;

        var definition = _blocks.GetDefinition(cell.Block);
        if (!definition.SupportsMicroblocks ||
            definition.Shape.Kind != BlockShapeKind.Cube ||
            definition.Mining.Unbreakable)
            return false;

        var direction = Vector3.Normalize(rayDirection);
        var mask = cell.HasMicroblockGeometry
            ? _world.GetMicroblockMaskOrEmpty(target.Voxel)
            : MicroblockMask.Full;
        if (cell.HasMicroblockGeometry && mask.IsEmpty)
            throw new InvalidOperationException(
                "Microblock palette references a missing occupancy mask.");

        if (!TryFindOccupiedMicroCell(
                target.Voxel, rayOrigin, direction, mask,
                out var x, out var y, out var z))
            return false;

        var restoring = hand == ToolUseHand.Right;
        if (restoring)
        {
            x += target.NormalX;
            y += target.NormalY;
            z += target.NormalZ;
        }
        if ((uint)x >= MicroblockMask.Edge ||
            (uint)y >= MicroblockMask.Edge ||
            (uint)z >= MicroblockMask.Edge)
            return false;

        var updated = mask.Edit(x, y, z, Resolution, restoring);
        if (updated == mask) return false;

        if (restoring && IntersectsPlayer(
                target.Voxel, x, y, z, Resolution, playerBounds))
            return false;

        if (updated.IsEmpty)
            return _mutations.SetCellAt(
                target.Voxel, VoxelCell.Empty, out _);

        var snapshot = new BlockStateSnapshot(
            cell, updated.IsFull ? MicroblockMask.Empty : updated,
            _world.GetBlockSurfaceStateOrEmpty(target.Voxel));
        return _mutations.SetBlockStateAt(target.Voxel, snapshot, out _);
    }

    private bool TryFindOccupiedMicroCell(
        WorldVoxelCoord voxel,
        Vector3 origin,
        Vector3 direction,
        MicroblockMask mask,
        out int x, out int y, out int z)
    {
        x = y = z = -1;
        // Ray entry/exit bounds constrain a fixed-size, bounded fine query.
        var min = new Vector3(voxel.X, voxel.Y, voxel.Z);
        var max = min + Vector3.One;
        var entry = 0f;
        var exit = MaximumReach;
        for (var axis = 0; axis < 3; axis++)
        {
            var coordinate = axis switch
            {
                0 => origin.X, 1 => origin.Y, _ => origin.Z,
            };
            var component = axis switch
            {
                0 => direction.X, 1 => direction.Y, _ => direction.Z,
            };
            var lower = axis switch { 0 => min.X, 1 => min.Y, _ => min.Z };
            var upper = axis switch { 0 => max.X, 1 => max.Y, _ => max.Z };
            if (MathF.Abs(component) <= float.Epsilon)
            {
                if (coordinate < lower || coordinate >= upper) return false;
                continue;
            }

            var first = (lower - coordinate) / component;
            var last = (upper - coordinate) / component;
            if (first > last) (first, last) = (last, first);
            entry = MathF.Max(entry, first);
            exit = MathF.Min(exit, last);
            if (entry > exit) return false;
        }

        // Sample at 1/128-block increments. Each occupied 1/8-cell
        // spans at least sixteen samples, so no micro-cell is skipped.
        var step = 1f / SamplesPerVoxel;
        for (var distance = entry + step * 0.5f;
             distance <= exit;
             distance += step)
        {
            var local = origin + direction * distance - min;
            var sx = Math.Clamp((int)MathF.Floor(local.X * MicroblockMask.Edge),
                0, MicroblockMask.Edge - 1);
            var sy = Math.Clamp((int)MathF.Floor(local.Y * MicroblockMask.Edge),
                0, MicroblockMask.Edge - 1);
            var sz = Math.Clamp((int)MathF.Floor(local.Z * MicroblockMask.Edge),
                0, MicroblockMask.Edge - 1);
            if (!mask.Contains(sx, sy, sz)) continue;
            x = sx;
            y = sy;
            z = sz;
            return true;
        }
        return false;
    }

    private static bool IntersectsPlayer(
        WorldVoxelCoord voxel,
        int x, int y, int z,
        MicroblockResolution resolution,
        WorldAabb bounds)
    {
        var width = (int)resolution;
        var edge = 1f / MicroblockMask.Edge;
        var low = new Vector3(
            voxel.X + (x / width * width) * edge,
            voxel.Y + (y / width * width) * edge,
            voxel.Z + (z / width * width) * edge);
        return new WorldAabb(
            low, low + new Vector3(width * edge)).Intersects(bounds);
    }

    private string? Behavior(InventoryStack? held, ToolUseHand hand)
    {
        if (held?.Kind != InventoryEntryKind.Tool ||
            !_tools.TryGet(held.Id, out var tool) || tool is null)
            return null;
        return hand == ToolUseHand.Left
            ? tool.LeftBehavior : tool.RightBehavior;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
