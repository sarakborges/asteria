using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

public readonly record struct TargetHudSnapshot(
    string BlockId,
    int X,
    int Y,
    int Z,
    byte SkyLight,
    byte BlockLight,
    BlockSurfaceState Surface,
    VoxelCell Cell,
    BlockFace? HitFace,
    string? ActiveLayerId = null);

public sealed class TargetHudStateTracker
{
    private TargetHudSnapshot? _last;

    public void Reset()
    {
        _last = null;
    }

    public bool TryCapture(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelWorldHit? hit,
        bool force,
        out TargetHudSnapshot? snapshot) =>
        TryCapture(world, blocks, null, hit, force, out snapshot);

    public bool TryCapture(
        VoxelWorld world,
        BlockRegistry blocks,
        AttachedLayerRegistry? layers,
        VoxelWorldHit? hit,
        bool force,
        out TargetHudSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(
            world);
        ArgumentNullException.ThrowIfNull(
            blocks);

        snapshot =
            BuildSnapshot(
                world,
                blocks,
                layers,
                hit);

        if (!force &&
            _last == snapshot)
        {
            return false;
        }

        _last = snapshot;
        return true;
    }

    private static TargetHudSnapshot? BuildSnapshot(
        VoxelWorld world,
        BlockRegistry blocks,
        AttachedLayerRegistry? layers,
        VoxelWorldHit? hit)
    {
        if (hit is not
            { } resolved ||
            !world.TryGetCell(
                resolved.Voxel,
                out var cell) ||
            cell.IsEmpty)
        {
            return null;
        }

        var definition =
            blocks.GetDefinition(
                cell.Block);
        var lightOffset =
            resolved.HasSurfaceNormal
                ? (
                    resolved.NormalX,
                    resolved.NormalY,
                    resolved.NormalZ)
                : (0, 1, 0);
        var light =
            world.GetLightOrDark(
                resolved.Voxel +
                lightOffset);

        var surface = world.GetBlockSurfaceStateOrEmpty(resolved.Voxel);
        var hitFace = VoxelHitFace.TryResolve(resolved, out var face)
            ? face : (BlockFace?)null;
        string? activeLayerId = null;
        if (layers is not null && hitFace is { } worldFace &&
            surface.TryGetTop(worldFace, out var attached) &&
            layers.TryGet(attached.LayerId, out _))
            activeLayerId = attached.LayerId;

        return new TargetHudSnapshot(
            definition.Id,
            resolved.Voxel.X,
            resolved.Voxel.Y,
            resolved.Voxel.Z,
            light.Sky,
            light.BlockPeak,
            surface,
            cell,
            hitFace,
            activeLayerId);
    }
}
