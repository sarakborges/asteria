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

public readonly record struct TargetFluidHudSnapshot(
    WorldVoxelCoord Voxel, FluidCell Fluid, VoxelLight Light);

public sealed class TargetHudStateTracker
{
    private TargetHudSnapshot? _last;
    private TargetFluidHudSnapshot? _lastFluid;

    public void Reset()
    {
        _last = null;
        _lastFluid = null;
    }

    /// <summary>
    /// Reuse the Core ray hit for the HUD's source-fluid variant. A fluid
    /// hit replaces the cached block presentation; a fluid -> empty/block
    /// transition is published by the ordinary TryCapture overload.
    /// </summary>
    public bool TryCaptureFluid(
        VoxelWorld world, VoxelWorldHit? hit, bool force,
        out TargetFluidHudSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(world);
        snapshot = hit is { } resolved &&
                   world.GetCellOrEmpty(resolved.Voxel).IsEmpty &&
                   world.TryGetFluid(resolved.Voxel, out var fluid) &&
                   !fluid.IsEmpty && fluid.IsSource
            ? new TargetFluidHudSnapshot(
                resolved.Voxel, fluid,
                world.GetLightOrDark(resolved.Voxel + (0, 1, 0)))
            : null;

        if (snapshot is null)
            return false;

        _last = null;
        var changed = force || _lastFluid != snapshot;
        _lastFluid = snapshot;
        return changed;
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

        // Clearing a fluid target must publish an empty/block HUD even if
        // the last block snapshot was already empty.
        var hadFluid = _lastFluid is not null;
        _lastFluid = null;
        if (!force && !hadFluid && _last == snapshot)
            return false;

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
